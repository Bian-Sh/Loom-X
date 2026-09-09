// LoomX Browser Bridge - MV3 background service worker.
// 职责（仅此五项）：Chrome API / chrome.debugger / chrome.tabs / WebSocket / target-session 映射。
// 不实现 Provider/Relay 业务、AI Agent、Skill 或模型发现逻辑——那些属于 LoomX。

const BRIDGE_URL = "ws://127.0.0.1:17831/loomx-browser/";
const PROTOCOL_VERSION = 1;
const RECONNECT_DELAY_MS = 3000;

/** targetId -> { sessionId, tabId, url, title } 仅登记本扩展创建的 automation tab。 */
const targets = new Map();
let socket = null;
let connectTimer = null;

function connect() {
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
      params: { protocol: PROTOCOL_VERSION, extension: chrome.runtime.getManifest().version },
    });
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
    detachAllTargets();
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
          targets: Array.from(targets.entries()).map(([targetId, info]) => ({
            targetId,
            sessionId: info.sessionId,
            tabId: info.tabId,
            url: info.url,
            title: info.title,
          })),
        });
        break;

      case "Target.createTarget": {
        const tab = await chrome.tabs.create({ url: params.url, active: true });
        const targetId = `target-${tab.id}`;
        await chrome.debugger.attach({ tabId: tab.id }, "1.3");
        const sessionId = `session-${tab.id}`;
        targets.set(targetId, { sessionId, tabId: tab.id, url: params.url, title: tab.title || "" });
        // 开启网络捕获（browser.network 的数据来源）
        await chrome.debugger.sendCommand({ tabId: tab.id }, "Network.enable", {});
        emitEvent("targetCreated", {
          targetId,
          sessionId,
          tabId: tab.id,
          url: params.url,
          title: tab.title || "",
        });
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
      const capture = await chrome.tabs.captureVisibleTab(null, { format: "png" });
      return { format: "png", base64: capture.split(",")[1] || capture };
    }
    case "Network.getRecent":
      return await evaluateInTab(info.tabId, networkSnippet(params.urlContains, params.limit));
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
    throw new Error(result.exceptionDetails.text || "page evaluation failed");
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
    let content;
    if (${escapeJs(mode)} === "html") {
      content = root.innerHTML;
    } else if (${escapeJs(mode)} === "markdown") {
      content = root.innerText;
    } else {
      content = root.innerText;
    }
    const maxLength = ${Number(params.maxLength) || 20000};
    return {
      url: location.href,
      title: document.title,
      content: content.slice(0, maxLength),
      truncated: content.length > maxLength,
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
      const el = document.querySelector(${escapeJs(params.selector)});
      if (el) return { found: true };
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
    return { found: false, timeout: true };
  })()`;
  return await evaluateInTab(tabId, expression);
}

function networkSnippet(urlContains, limit) {
  return `(() => {
    const records = (window.__loomxNetwork || []);
    const filtered = records.filter((item) =>
      ${urlContains ? `item.url.includes(${escapeJs(urlContains)})` : "true"});
    return { requests: filtered.slice(-${Number(limit) || 50}) };
  })()`;
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
  emitEvent("targetClosed", { targetId });
}

function detachAllTargets() {
  for (const [targetId, info] of targets) {
    chrome.debugger.detach({ tabId: info.tabId }).catch(() => {});
    emitEvent("targetClosed", { targetId });
  }
  targets.clear();
}

// 用户手动关闭 automation tab 时同步登记
chrome.tabs.onRemoved.addListener((tabId) => {
  for (const [targetId, info] of targets) {
    if (info.tabId === tabId) {
      targets.delete(targetId);
      emitEvent("targetClosed", { targetId });
    }
  }
});

// MV3 service worker 休眠后恢复时保持连接
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
connect();
