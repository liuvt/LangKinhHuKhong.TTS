// Test nhanh client không phụ thuộc trình duyệt: node Tests/studio-smoke.test.js
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const controls = new Map();
const element = id => {
  if (!controls.has(id)) controls.set(id, {
    id, value: '', textContent: '', hidden: false, style: {},
    handlers: {},
    addEventListener(type, callback) { this.handlers[type] = callback; },
    dispatchEvent(event) { this.handlers[event.type]?.(event); },
    focus() {},
    setRangeText(text, begin, end) { this.value = this.value.slice(0, begin) + text + this.value.slice(end); },
  });
  return controls.get(id);
};
const fakeStorage = new Map();
const ctx = {
  window: { addEventListener() {} },
  document: {
    getElementById: element,
    querySelector: () => ({ classList: { toggle() {} } }),
    createElement: () => ({ style: {}, setAttribute() {}, click() {}, remove() {} }),
    body: { appendChild() {} }
  },
  Event: class Event { constructor(type) { this.type = type; } },
  navigator: { clipboard: { readText: async () => 'Nội dung dán' }, sendBeacon() { return true; } },
  crypto: { randomUUID: () => 'random-id' },
  sessionStorage: { getItem: key => fakeStorage.get(key) ?? null, setItem: (key, val) => fakeStorage.set(key, val) },
  fetch: async () => ({ status: 204 }),
  setTimeout, clearTimeout, setInterval: () => 1, clearInterval() {},
  console, Intl, Blob,
};
vm.createContext(ctx);
vm.runInContext(fs.readFileSync('wwwroot/js/studio.js', 'utf8'), ctx);
const studio = ctx.window.lkhStudio;
(async () => {
  const callback = { invokeMethodAsync: async () => true };
  await studio.register('1'.repeat(32), '2'.repeat(32), callback);
  element('lkh-editor').value = 'Cao Dương bình thản nói\n“Ngươi là ai”\nĐúng vậy!\n\nĐoạn tiếp';
  assert.equal(studio.getEditorText(), 'Cao Dương bình thản nói\n“Ngươi là ai”\nĐúng vậy!\n\nĐoạn tiếp');
  element('lkh-editor').value = 'a'.repeat(100000);
  assert.equal(studio.getEditorText().length, 100000);
  element('lkh-editor').value = 'a'.repeat(100001);
  assert.throws(() => studio.getEditorText(), /100000/);
  element('lkh-editor').value = '';
  await element('lkh-file-input').handlers.change({ target: {
    files: [{ name: 'chapter.md', size: 80, text: async () => '# Chương 1\n\n**Đế quân** ra lệnh\nKết thúc?' }],
    value: 'chapter.md'
  } });
  assert.equal(element('lkh-editor').value, 'Chương 1.\n\nĐế quân ra lệnh.\nKết thúc?');
  element('lkh-editor').value = 'Tạm biệt\nCâu thứ hai';
  element('lkh-normalize-button').handlers.click();
  assert.equal(element('lkh-editor').value, 'Tạm biệt.\nCâu thứ hai.');
  assert.equal(fakeStorage.has('lkh.tts.tab-session'), true);
  console.log('PASS: 100k limit, text normalize, MD import, per-tab token storage');
})().catch(e => { console.error(e); process.exitCode = 1; });
