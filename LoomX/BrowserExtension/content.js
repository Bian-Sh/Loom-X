// LoomX Browser Bridge - content script。
// 唯一职责：在页面内捕获 XHR/fetch 网络请求摘要，供 browser.network 查询。
// 不包含任何 Provider/Relay/AI 业务逻辑。
(function () {
  if (window.__loomxNetworkHooked) return;
  window.__loomxNetworkHooked = true;
  window.__loomxNetwork = [];

  const MAX_RECORDS = 200;

  function sanitizeHeaders(headers) {
    const result = {};
    for (const [key, value] of Object.entries(headers || {})) {
      result[key] = String(value);
    }
    return result;
  }

  function record(entry) {
    window.__loomxNetwork.push(entry);
    if (window.__loomxNetwork.length > MAX_RECORDS) {
      window.__loomxNetwork.splice(0, window.__loomxNetwork.length - MAX_RECORDS);
    }
  }

  function guessModel(body) {
    if (!body) return null;
    const match = /"model"\s*:\s*"([^"]+)"/.exec(body);
    return match ? match[1] : null;
  }

  // fetch 钩子
  const originalFetch = window.fetch;
  window.fetch = async function (input, init) {
    const url = typeof input === "string" ? input : input && input.url;
    const method = (init && init.method) || (input && input.method) || "GET";
    const headers = sanitizeHeaders(
      Object.assign(
        {},
        input && input.headers ? Object.fromEntries(new Headers(input.headers).entries()) : {},
        init && init.headers ? Object.fromEntries(new Headers(init.headers).entries()) : {}
      )
    );
    const body = init && typeof init.body === "string" ? init.body.slice(0, 2000) : null;
    const startedAt = Date.now();
    try {
      const response = await originalFetch.apply(this, arguments);
      record({
        type: "fetch",
        url,
        method,
        status: response.status,
        request_headers: headers,
        model: guessModel(body),
        elapsed_ms: Date.now() - startedAt,
        timestamp: new Date().toISOString(),
      });
      return response;
    } catch (error) {
      record({
        type: "fetch",
        url,
        method,
        status: 0,
        request_headers: headers,
        model: guessModel(body),
        elapsed_ms: Date.now() - startedAt,
        timestamp: new Date().toISOString(),
      });
      throw error;
    }
  };

  // XHR 钩子
  const OriginalXHR = window.XMLHttpRequest;
  function HookedXHR() {
    const xhr = new OriginalXHR();
    const requestHeaders = {};
    let method = "GET";
    let url = "";
    let body = null;

    const originalOpen = xhr.open;
    xhr.open = function (m, u) {
      method = m;
      url = u;
      return originalOpen.apply(xhr, arguments);
    };

    const originalSetRequestHeader = xhr.setRequestHeader;
    xhr.setRequestHeader = function (key, value) {
      requestHeaders[key] = value;
      return originalSetRequestHeader.apply(xhr, arguments);
    };

    const originalSend = xhr.send;
    xhr.send = function (payload) {
      body = typeof payload === "string" ? payload.slice(0, 2000) : null;
      const startedAt = Date.now();
      xhr.addEventListener("loadend", function () {
        record({
          type: "xhr",
          url,
          method,
          status: xhr.status,
          request_headers: sanitizeHeaders(requestHeaders),
          model: guessModel(body),
          elapsed_ms: Date.now() - startedAt,
          timestamp: new Date().toISOString(),
        });
      });
      return originalSend.apply(xhr, arguments);
    };

    return xhr;
  }
  window.XMLHttpRequest = HookedXHR;
})();
