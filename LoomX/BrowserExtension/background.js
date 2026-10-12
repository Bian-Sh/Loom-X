// LoomX Browser Bridge - MV3 background service worker.
// 职责（仅此五项）：Chrome API / chrome.debugger / chrome.tabs / WebSocket / target-session 映射。
// 不实现 Provider/Relay 业务、AI Agent、Skill 或模型发现逻辑——那些属于 LoomX。

importScripts("content.js");

const BRIDGE_URL = "ws://127.0.0.1:17831/loomx-browser/";
const BRIDGE_HEALTH_URL = "http://127.0.0.1:17831/loomx-browser/";
const PROTOCOL_VERSION = 1;
const PROBE_TIMEOUT_MS = 1000;
const RECONNECT_DELAY_MS = 3000;
const KEEPALIVE_INTERVAL_MS = 20000;
const RECONNECT_ALARM_NAME = "loomx-browser-reconnect";
const RECONNECT_ALARM_PERIOD_MINUTES = 0.5;

/** targetId -> { sessionId, tabId, url, title } 仅登记本扩展创建的 automation tab。 */
const targets = new Map();
let socket = null;
let connectPromise = null;
let connectTimer = null;
let keepAliveTimer = null;


function currentTargets() {
  return Array.from(targets.entries()).map(([targetId, info]) => ({
    targetId,
    sessionId: info.sessionId,
    tabId: info.tabId,
    url: info.url,
    title: info.title,
  }));
}

function ensureReconnectAlarm() {
  chrome.alarms.create(RECONNECT_ALARM_NAME, {
    periodInMinutes: RECONNECT_ALARM_PERIOD_MINUTES,
  });
}
function connect() {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    return;
  }
  if (connectPromise) return;

  connectPromise = connectAfterProbe().finally(() => {
    connectPromise = null;
  });
}

async function connectAfterProbe() {
  if (!(await probeBridge())) {
    scheduleReconnect();
    return;
  }
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
    return;
  }

  try {
    socket = new WebSocket(BRIDGE_URL);
  } catch (error) {
    scheduleReconnect();
    return;
  }

  socket.onopen = () => {
    send({
      method: "Bridge.hello",
      params: {
        protocol: PROTOCOL_VERSION,
        extension: chrome.runtime.getManifest().version,
        targets: currentTargets(),
      },
    });
    startKeepAlive();
  };

  socket.onmessage = (event) => {
    let message;
    try {
      message = JSON.parse(event.data);
    } catch (error) {
      return;
    }
    handleCommand(message);
  };

  socket.onclose = () => {
    stopKeepAlive();
    socket = null;
    scheduleReconnect();
  };

  socket.onerror = () => {
    try {
      socket.close();
    } catch (error) {
      // 忽略，onclose 会处理重连
    }
  };
}

async function probeBridge() {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), PROBE_TIMEOUT_MS);
  try {
    const response = await fetch(BRIDGE_HEALTH_URL, {
      method: "GET",
      cache: "no-store",
      signal: controller.signal,
    });
    return response.status === 204;
  } catch (error) {
    return false;
  } finally {
    clearTimeout(timeout);
  }
}

function startKeepAlive() {
  stopKeepAlive();
  keepAliveTimer = setInterval(() => {
    send({ method: "Bridge.keepAlive" });
  }, KEEPALIVE_INTERVAL_MS);
}

function stopKeepAlive() {
  if (!keepAliveTimer) return;
  clearInterval(keepAliveTimer);
  keepAliveTimer = null;
}

function scheduleReconnect() {
  if (connectTimer) return;
  connectTimer = setTimeout(() => {
    connectTimer = null;
    connect();
  }, RECONNECT_DELAY_MS);
}

function send(message) {
  if (socket && socket.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify(message));
  }
}

function respond(id, result) {
  send({ id, result: result === undefined ? {} : result });
}

function respondError(id, message) {
  send({ id, error: { message: String(message) } });
}

function emitEvent(name, params) {
  send({ method: "Browser.event", params: { name, ...params } });
}

async function handleCommand(message) {
  const { id, method, params } = message;
  try {
    switch (method) {
      case "Target.getTargets":
        respond(id, {
          targets: currentTargets(),
        });
        break;

      case "Target.createTarget": {
        // 先注册空白自动化页并启用原生网络事件，再开始导航，避免漏掉初始请求。
        const tab = await chrome.tabs.create({ url: "about:blank", active: false });
        const targetId = `target-${tab.id}`;
        const sessionId = `session-${tab.id}`;
        try {
          await chrome.debugger.attach({ tabId: tab.id }, "1.3");
          await chrome.debugger.sendCommand({ tabId: tab.id }, "Network.enable", {});
          targets.set(targetId, { sessionId, tabId: tab.id, url: params.url, title: "" });
          await chrome.tabs.update(tab.id, { url: params.url });
        } catch (error) {
          await closeTarget(targetId, { tabId: tab.id });
          throw error;
        }
        emitEvent("targetCreated", { targetId, sessionId, tabId: tab.id, url: params.url, title: "" });
        respond(id, { target_id: targetId, session_id: sessionId, tab_id: tab.id });
        break;
      }

      case "Target.closeTarget": {
        const info = targets.get(params.targetId);
        if (!info) throw new Error(`unknown target: ${params.targetId}`);
        await closeTarget(params.targetId, info);
        respond(id, { closed: true });
        break;
      }

      case "Session.sendCommand": {
        const info = findBySessionId(params.sessionId);
        if (!info) throw new Error(`unknown session: ${params.sessionId}`);
        const result = await handleSessionCommand(info, params.method, params.params || {});
        respond(id, result);
        break;
      }

      default:
        respondError(id, `unknown method: ${method}`);
    }
  } catch (error) {
    respondError(id, error && error.message ? error.message : error);
  }
}

function findBySessionId(sessionId) {
  for (const [targetId, info] of targets) {
    if (info.sessionId === sessionId) return { targetId, ...info };
  }
  return null;
}

async function handleSessionCommand(info, method, params) {
  switch (method) {
    case "Page.read":
      return await readPage(info.tabId, params);
    case "Page.click":
      return await evaluateInTab(info.tabId, clickSnippet(params.selector));
    case "Page.type":
      return await evaluateInTab(info.tabId, typeSnippet(params.selector, params.text, params.clear));
    case "Page.wait":
      return await waitFor(info.tabId, params);
    case "Page.captureScreenshot": {
      const capture = await chrome.debugger.sendCommand({ tabId: info.tabId }, "Page.captureScreenshot", {
        format: "png",
        captureBeyondViewport: Boolean(params.fullPage),
      });
      return { format: "png", base64: capture.data };
    }
    case "Network.getRecent":
      return LoomXNetwork.recent(info.tabId, params.urlContains, params.limit);
    default:
      throw new Error(`unknown session method: ${method}`);
  }
}

async function evaluateInTab(tabId, expression) {
  const result = await chrome.debugger.sendCommand({ tabId }, "Runtime.evaluate", {
    expression,
    returnByValue: true,
    awaitPromise: true,
  });
  if (result.exceptionDetails) {
    // 页面异常可能含凭据或选择器，只返回可恢复的固定错误码。
    const detail = result.exceptionDetails.exception?.description || result.exceptionDetails.text || "";
    const code = detail.includes("element not found") ? "element_not_found"
      : detail.includes("not a valid selector") || detail.includes("Invalid selector") ? "invalid_selector"
      : "page_evaluation_failed";
    throw new Error(code);
  }
  return result.result && result.result.value !== undefined ? result.result.value : {};
}

function escapeJs(value) {
  return JSON.stringify(String(value === undefined || value === null ? "" : value));
}

function clickSnippet(selector) {
  return `(() => {
    const el = document.querySelector(${escapeJs(selector)});
    if (!el) throw new Error("element not found: " + ${escapeJs(selector)});
    el.scrollIntoView({ block: "center" });
    el.click();
    return { clicked: true };
  })()`;
}

function typeSnippet(selector, text, clear) {
  return `(() => {
    const el = document.querySelector(${escapeJs(selector)});
    if (!el) throw new Error("element not found: " + ${escapeJs(selector)});
    el.focus();
    if (${clear !== false}) { el.value = ""; }
    el.value = ${escapeJs(text)};
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
    return { typed: true, length: el.value.length };
  })()`;
}

async function readPage(tabId, params) {
  const mode = params.mode || "text";
  const expression = `(() => {
    const root = ${params.selector ? `document.querySelector(${escapeJs(params.selector)})` : "document.body"};
    if (!root) throw new Error("element not found");
    const content = ${escapeJs(mode)} === "html"
      ? (${Boolean(params.selector)} ? root.outerHTML : root.innerHTML) || ""
      : root.innerText || root.textContent || "";
    const maxLength = ${Math.min(20000, Math.max(1, Number(params.maxLength) || 20000))};
    // 导航和交互必须基于页面真实元素，不在扩展中推断任何业务路由。
    const visible = element => element.getClientRects().length > 0
      && getComputedStyle(element).visibility !== "hidden"
      && getComputedStyle(element).display !== "none";
    const candidates = selector => {
      const elements = Array.from(root.querySelectorAll(selector));
      if (root.matches && root.matches(selector)) elements.unshift(root);
      return elements.filter(visible);
    };
    const label = element => (element.getAttribute("aria-label") || element.getAttribute("title")
      || element.innerText || element.getAttribute("placeholder") || element.getAttribute("name") || "").trim().slice(0, 120);
    const selectorFor = element => {
      if (element.id) return "#" + CSS.escape(element.id);
      if (element.tagName === "A" && element.getAttribute("href"))
        return 'a[href="' + CSS.escape(element.getAttribute("href")) + '"]';
      const parts = [];
      for (let node = element; node && node.nodeType === 1; node = node.parentElement) {
        if (node.id) { parts.unshift("#" + CSS.escape(node.id)); break; }
        const siblings = node.parentElement ? Array.from(node.parentElement.children).filter(item => item.tagName === node.tagName) : [node];
        parts.unshift(node.tagName.toLowerCase() + ":nth-of-type(" + (siblings.indexOf(node) + 1) + ")");
      }
      return parts.join(" > ");
    };
    const linkElements = candidates("a[href]").filter(element => element.href
      && (element.href.startsWith("http://") || element.href.startsWith("https://")));
    const controlElements = candidates("button,input,select,textarea,[role='button']");
    return {
      url: location.href,
      title: document.title,
      content: content.slice(0, maxLength),
      truncated: content.length > maxLength,
      links: linkElements.slice(0, 60).map(element => ({ label: label(element), href: element.href, selector: selectorFor(element) })),
      // 不读取输入值，避免把密码、凭据或用户尚未提交的内容带入通用控件摘要。
      controls: controlElements.slice(0, 60).map(element => ({ kind: element.tagName.toLowerCase(),
        label: label(element), selector: selectorFor(element), type: element.getAttribute("type") || "",
        disabled: Boolean(element.disabled) || element.getAttribute("aria-disabled") === "true", read_only: Boolean(element.readOnly) })),
      dom_truncated: linkElements.length > 60 || controlElements.length > 60,
    };
  })()`;
  return await evaluateInTab(tabId, expression);
}

async function waitFor(tabId, params) {
  const timeoutMs = Number(params.timeoutMs) || 15000;
  if (params.milliseconds && !params.selector) {
    await new Promise((resolve) => setTimeout(resolve, Number(params.milliseconds)));
    return { waited: true };
  }
  const expression = `(async () => {
    const deadline = Date.now() + ${timeoutMs};
    while (Date.now() < deadline) {
      ${params.selector
        ? `const el = document.querySelector(${escapeJs(params.selector)}); if (el) return { found: true };`
        : `if (document.readyState === "complete") return { loaded: true };`}
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
    return { found: false, timeout: true };
  })()`;
  return await evaluateInTab(tabId, expression);
}

async function closeTarget(targetId, info) {
  try {
    await chrome.debugger.detach({ tabId: info.tabId });
  } catch (error) {
    // 标签页可能已关闭
  }
  try {
    await chrome.tabs.remove(info.tabId);
  } catch (error) {
    // 标签页可能已关闭
  }
  targets.delete(targetId);
  LoomXNetwork.forget(info.tabId);
  emitEvent("targetClosed", { targetId });
}

// 只处理本扩展登记的自动化页面，其他用户标签页不进入网络缓存。
chrome.debugger.onEvent.addListener((source, method, params) => {
  if (Array.from(targets.values()).some(info => info.tabId === source.tabId))
    LoomXNetwork.record(source.tabId, method, params);
});

// 用户手动关闭 automation tab 时同步登记
chrome.tabs.onRemoved.addListener((tabId) => {
  for (const [targetId, info] of targets) {
    if (info.tabId === tabId) {
      targets.delete(targetId);
      LoomXNetwork.forget(tabId);
      emitEvent("targetClosed", { targetId });
    }
  }
});

// MV3 service worker 休眠后通过 alarm 唤醒并恢复连接
chrome.runtime.onStartup.addListener(() => {
  ensureReconnectAlarm();
  connect();
});
chrome.runtime.onInstalled.addListener(() => {
  ensureReconnectAlarm();
  connect();
});
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === RECONNECT_ALARM_NAME) connect();
});
ensureReconnectAlarm();
connect();
