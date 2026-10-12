// LoomX Browser Bridge 原生网络事件缓存，加载于扩展 Service Worker。
// 不改写页面 fetch/XHR；仅保存请求摘要，不保存请求或响应正文。
const LoomXNetwork = (() => {
  const recordsByTab = new Map();
  const maxRecords = 200;
  const excludedHeaders = new Set(["cookie", "set-cookie", "proxy-authorization"]);

  function record(tabId, method, params) {
    if (method === "Network.requestWillBeSent") {
      if (!params.request || !["XHR", "Fetch"].includes(params.type)) return;
      let records = recordsByTab.get(tabId);
      if (!records) { records = []; recordsByTab.set(tabId, records); }
      const request = params.request;
      let model = null;
      // 模型名只作公开线索；正文不进入缓存。
      if (typeof request.postData === "string" && request.postData.length <= 8192) {
        try { const value = JSON.parse(request.postData)?.model; if (typeof value === "string") model = value; } catch (_) {}
      }
      const startedAt = Date.now();
      records.push({ request_id: params.requestId, started_at: startedAt,
        type: params.type.toLowerCase(), url: request.url, method: request.method, status: 0, model,
        request_headers: Object.fromEntries(Object.entries(request.headers || {})
          .filter(([name]) => !excludedHeaders.has(name.toLowerCase())).map(([name, value]) => [name, String(value)])),
        elapsed_ms: 0, timestamp: new Date(startedAt).toISOString() });
      if (records.length > maxRecords) records.splice(0, records.length - maxRecords);
      return;
    }
    if (!["Network.responseReceived", "Network.loadingFailed"].includes(method)) return;
    const records = recordsByTab.get(tabId) || [];
    const entry = records.findLast(item => item.request_id === params.requestId);
    if (!entry) return;
    entry.status = params.response?.status || 0;
    entry.elapsed_ms = Date.now() - entry.started_at;
  }

  function recent(tabId, urlContains, limit) {
    const count = Math.min(maxRecords, Math.max(1, Number(limit) || 50));
    return { requests: (recordsByTab.get(tabId) || []).filter(item => !urlContains || item.url.includes(urlContains))
      .slice(-count).map(({ request_id, started_at, ...summary }) => summary) };
  }
  return { record, recent, forget: tabId => recordsByTab.delete(tabId) };
})();
