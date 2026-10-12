const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../LoomX/BrowserExtension/background.js'), 'utf8');

async function snapshot(params = {}) {
  function element(tag, text, attributes, visible = true) {
    return {
      tagName: tag.toUpperCase(), innerText: text, textContent: text, id: attributes.id || '', disabled: false,
      outerHTML: tag === 'a' ? '<a href="' + attributes.href + '">' + text + '</a>' : '',
      get href() { return attributes.href ? new URL(attributes.href, 'https://ienvie.top/dashboard').href : undefined; },
      get value() { throw new Error('不得读取输入值'); },
      getAttribute(name) { if (name === 'value') throw new Error('不得读取输入值'); return attributes[name] || null; },
      getClientRects() { return visible ? [{}] : []; },
      matches(selector) { return selector === 'a[href]' && tag === 'a'; },
      querySelectorAll() { return []; },
    };
  }
  const keys = element('a', 'API 密钥', { id: 'keys-link', href: '/keys' });
  const hidden = element('a', '隐藏入口', { id: 'hidden-link', href: '/hidden' }, false);
  const button = element('button', '创建 API 密钥', { id: 'create-key' });
  const input = element('input', '', { id: 'key-name', type: 'text', placeholder: '密钥名称' });
  const body = { innerText: '控制台 API 密钥 创建 API 密钥', innerHTML: '<a href="/keys">API 密钥</a>',
    matches() { return false; }, querySelectorAll(selector) { return selector === 'a[href]' ? [keys, hidden] : [button, input]; } };
  const document = { title: '控制台', body, querySelector(selector) { return selector === '#keys-link' ? keys : body; } };
  const dom = { document, location: { href: 'https://ienvie.top/dashboard' }, CSS: { escape: String },
    getComputedStyle: () => ({ display: 'block', visibility: 'visible' }), URL };
  const event = { addListener() {} };
  let context;
  context = vm.createContext({
    importScripts(file) { vm.runInContext(fs.readFileSync(path.join(__dirname, "../../LoomX/BrowserExtension", file), "utf8"), context); },
    chrome: { tabs: { onRemoved: event }, runtime: { onStartup: event, onInstalled: event },
      alarms: { create() {}, onAlarm: event }, debugger: { onEvent: event, async sendCommand(_, method, parameters) {
        assert.equal(method, 'Runtime.evaluate');
        return { result: { value: vm.runInNewContext(parameters.expression, dom) } };
      } } },
    fetch: async () => ({ status: 404 }), AbortController,
    setTimeout: () => 1, clearTimeout() {}, setInterval: () => 1, clearInterval() {},
  });
  vm.runInContext(source, context);
  return JSON.parse(JSON.stringify(await context.readPage(1, params)));
}

test('页面读取返回真实密钥导航链接与可操作选择器', async () => {
  const result = await snapshot();
  assert.equal(result.links.length, 1);
  assert.equal(result.links[0].href, 'https://ienvie.top/keys');
  assert.equal(result.links[0].selector, '#keys-link');
  assert.equal(result.controls[0].label, '创建 API 密钥');
  assert.equal(result.controls[0].selector, '#create-key');
  assert.equal(result.controls[1].type, 'text');
  assert.equal(result.controls[1].value, undefined);
  assert.equal(JSON.stringify(result).includes('/api-keys'), false);
});

test('读取单个链接时仍保留该根元素的导航信息', async () => {
  const result = await snapshot({ selector: '#keys-link', maxLength: 2 });
  assert.equal(result.content.length, 2);
  assert.equal(result.truncated, true);
  assert.equal(result.links[0].href, 'https://ienvie.top/keys');
});

test('HTML读取保留所选根元素的真实href属性', async () => {
  const result = await snapshot({ selector: '#keys-link', mode: 'html' });
  assert.ok(result.content.includes('href="/keys"'));
});
