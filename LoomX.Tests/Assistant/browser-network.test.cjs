const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const folder = path.join(__dirname, '../../LoomX/BrowserExtension');
function harness() {
  const calls = [], handlers = {};
  const event = { addListener() {} };
  let context;
  const chrome = {
    tabs: { onRemoved: event, async create(options) { calls.push(['create', options.url]); return { id: 7, title: '空白' }; },
      async update(id, options) { calls.push(['navigate', options.url]); return { id, url: options.url }; }, async remove() {} },
    runtime: { onStartup: event, onInstalled: event }, alarms: { create() {}, onAlarm: event },
    debugger: { onEvent: { addListener(callback) { handlers.network = callback; } },
      async attach() { calls.push(['attach']); }, async detach() {},
      async sendCommand(_, method, parameters) {
        if (method === 'Runtime.evaluate') return { result: { value: await vm.runInNewContext(parameters.expression,
          { document: { readyState: 'complete', querySelector(selector) { if (!selector) throw new Error('空选择器'); return null; } }, Date }) } };
        calls.push([method]); return {};
      } },
  };
  context = vm.createContext({ chrome, Date, Map, JSON, fetch: async () => ({ status: 404 }), AbortController,
    importScripts(file) { vm.runInContext(fs.readFileSync(path.join(folder, file), 'utf8'), context); },
    setTimeout: () => 1, clearTimeout() {}, setInterval: () => 1, clearInterval() {} });
  vm.runInContext(fs.readFileSync(path.join(folder, 'background.js'), 'utf8'), context);
  return { context, handlers, calls };
}

test('原生网络事件只记录自动化标签页且不存Cookie或正文', async () => {
  const h = harness();
  vm.runInContext('targets.set("target-7", {tabId:7, sessionId:"session-7"})', h.context);
  assert.equal(typeof h.handlers.network, 'function');
  const params = { requestId: 'request-1', type: 'Fetch', request: { url: 'https://ienvie.top/v1/models', method: 'GET',
    headers: { Authorization: '测试令牌', Cookie: '不应采集的Cookie' }, postData: '{"model":"test-model","prompt":"不应保留的正文"}' } };
  h.handlers.network({ tabId: 99 }, 'Network.requestWillBeSent', params);
  h.handlers.network({ tabId: 7 }, 'Network.requestWillBeSent', params);
  h.handlers.network({ tabId: 7 }, 'Network.responseReceived', { requestId: 'request-1', response: { status: 200 } });
  const result = JSON.parse(JSON.stringify(await h.context.handleSessionCommand({ tabId: 7 }, 'Network.getRecent', {})));
  assert.equal(result.requests.length, 1);
  assert.equal(result.requests[0].status, 200);
  assert.equal(result.requests[0].model, 'test-model');
  assert.equal(result.requests[0].request_headers.Authorization, '测试令牌');
  assert.equal(JSON.stringify(result).includes('不应采集的Cookie'), false);
  assert.equal(JSON.stringify(result).includes('不应保留的正文'), false);
});

test('先在空白自动化页启用网络事件再导航以捕获初始请求', async () => {
  const h = harness();
  await h.context.handleCommand({ id: 1, method: 'Target.createTarget', params: { url: 'https://ienvie.top/keys' } });
  assert.deepEqual(h.calls.map(item => item[0]), ['create', 'attach', 'Network.enable', 'navigate']);
  assert.equal(h.calls[0][1], 'about:blank');
  assert.equal(h.calls[3][1], 'https://ienvie.top/keys');
});

test('默认等待页面加载时不使用空CSS选择器', async () => {
  const h = harness();
  const result = await h.context.waitFor(7, {});
  assert.equal(result.loaded, true);
});

test('页面异常提供可恢复错误码但不泄露页面异常内容', async () => {
  const h = harness();
  h.context.chrome.debugger.sendCommand = async () => ({ exceptionDetails: { text: 'Uncaught', exception: { description: 'Error: element not found: 敏感页面值' } } });
  await assert.rejects(h.context.evaluateInTab(7, '任意表达式'), error => error.message === 'element_not_found');
  h.context.chrome.debugger.sendCommand = async () => ({ exceptionDetails: { text: 'Uncaught', exception: { description: 'SyntaxError: Failed to execute querySelector: is not a valid selector. 敏感页面值' } } });
  await assert.rejects(h.context.evaluateInTab(7, '任意表达式'), error => error.message === 'invalid_selector');
  h.context.chrome.debugger.sendCommand = async () => ({ exceptionDetails: { text: '敏感页面值' } });
  await assert.rejects(h.context.evaluateInTab(7, '任意表达式'), error => error.message === 'page_evaluation_failed');
});
