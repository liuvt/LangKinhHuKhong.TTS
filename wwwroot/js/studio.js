// Lăng Kính Hư Không v3.2. Văn bản dài ở trong DOM, chỉ gửi text qua SignalR khi render.
window.lkhStudio = (() => {
  const MAX = 100000;
  const formatter = new Intl.NumberFormat('vi-VN');
  const wordRegex = /[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)*/gu;
  let sessionId = null;
  let sessionKey = null;
  let editor = null;
  let callback = null;
  let heartbeat = null;
  let statsTimer = null;
  let statusTimer = null;
  let lastStats = { chars: -1, words: -1 };
  let listenersBound = false;
  const SESSION_STORE = 'lkh.tts.tab-session';
  const documentNonce = crypto.randomUUID();
  const tabChannel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('lkh-tts-active-tabs') : null;
  if (tabChannel) tabChannel.addEventListener('message', event => {
    const message = event.data;
    if (message?.type === 'probe' && sessionId === message.id && message.nonce !== documentNonce) {
      tabChannel.postMessage({ type: 'owned', id: message.id, nonce: message.nonce });
    }
  });

  // Duplicate Tab của Chrome có thể sao chép sessionStorage. Thăm dò tab khác
  // trước khi nối lại để tránh 2 tab dùng chung một thư mục MP3.
  async function isOwnedByAnotherTab(id) {
    if (!tabChannel) return true; // An toàn: trình duyệt cũ tạo phiên mới.
    return new Promise(resolve => {
      const handler = event => {
        if (event.data?.type === 'owned' && event.data.id === id && event.data.nonce === documentNonce) {
          clearTimeout(timer);
          tabChannel.removeEventListener('message', handler);
          resolve(true);
        }
      };
      tabChannel.addEventListener('message', handler);
      const timer = setTimeout(() => {
        tabChannel.removeEventListener('message', handler);
        resolve(false);
      }, 180);
      tabChannel.postMessage({ type: 'probe', id, nonce: documentNonce });
    });
  }

  function apiUrl(type) {
    return `/api/session/${encodeURIComponent(sessionId)}/${type}?key=${encodeURIComponent(sessionKey)}`;
  }

  function closeTab() {
    if (!sessionId || !sessionKey) return;
    if (heartbeat) clearInterval(heartbeat);
    try {
      const url = apiUrl('leave');
      if (navigator.sendBeacon) navigator.sendBeacon(url, new Blob([], { type: 'text/plain' }));
      else fetch(url, { method: 'POST', keepalive: true }).catch(() => {});
    } catch (_) { /* Đóng Chrome đột ngột: backend dọn phiên hết hạn. */ }
  }

  function getEditor() { return editor || document.getElementById('lkh-editor'); }

  function refresh() {
    const el = getEditor();
    if (!el) return;
    const chars = el.value.length; // JavaScript UTF-16 khớp String.Length C#
    const over = chars > MAX;
    document.getElementById('lkh-char-count').textContent = formatter.format(chars);
    document.querySelector('.site-shell')?.classList.toggle('input-too-long', over);
    const warn = document.getElementById('lkh-input-warning');
    if (warn) { warn.hidden = !over; warn.style.display = over ? 'flex' : 'none'; }
    const excess = document.getElementById('lkh-excess-count');
    if (excess) excess.textContent = formatter.format(Math.max(0, chars - MAX));
    const meter = document.getElementById('lkh-counter-bar');
    if (meter) meter.style.width = `${Math.min(100, chars / MAX * 100)}%`;
    const button = document.getElementById('lkh-render-button');
    if (button) button.style.display = over ? 'none' : '';
    if (statsTimer) clearTimeout(statsTimer);
    statsTimer = setTimeout(() => {
      const current = getEditor();
      if (!current) return;
      const value = current.value;
      const words = (value.match(wordRegex) || []).length;
      const output = document.getElementById('lkh-word-count');
      if (output) output.textContent = `${formatter.format(words)} từ`;
      if (lastStats.chars === value.length && lastStats.words === words) return;
      lastStats = { chars: value.length, words };
      callback?.invokeMethodAsync('UpdateEditorStats', value.length, words).catch(() => {});
    }, 140);
  }

  function toast(message, duration = 4500) {
    const el = document.getElementById('lkh-editor-status');
    if (!el) return;
    el.textContent = message;
    el.hidden = false;
    el.style.display = 'block';
    if (statusTimer) clearTimeout(statusTimer);
    statusTimer = setTimeout(() => { el.hidden = true; el.style.display = 'none'; }, duration);
  }

  // Chỉ làm sạch cú pháp Markdown phổ biến dùng trong truyện, không xóa cốt truyện.
  function markdownToText(text) {
    return text
      .replace(/^\uFEFF/, '')
      .replace(/^\s*```[^\n]*$/gm, '')
      .replace(/^\s{0,3}#{1,6}\s+/gm, '')
      .replace(/^\s{0,3}>\s?/gm, '')
      .replace(/^\s*[-*_]{3,}\s*$/gm, '')
      .replace(/^\s*[-*+]\s+(?=\S)/gm, '')
      .replace(/^\s*\d+[.)]\s+(?=\S)/gm, '')
      .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/(?<!\*)\*\*([^*\n]+)\*\*(?!\*)/g, '$1')
      .replace(/__([^_\n]+)__/g, '$1')
      .replace(/\*([^*\n]+)\*/g, '$1')
      .replace(/`([^`\n]+)`/g, '$1');
  }

  function normalize(text, isMarkdown = false) {
    text = (isMarkdown ? markdownToText(text) : text).replace(/\r\n?/g, '\n').replace(/^\uFEFF/, '');
    return text.split('\n').map(line => {
      line = line.trimEnd();
      if (!line.trim()) return '';
      const close = line.match(/[”’"')\]]+$/u);
      const pos = close ? line.length - close[0].length - 1 : line.length - 1;
      if (pos >= 0 && !/[.!?…;:]/u.test(line.charAt(pos)))
        return line.slice(0, pos + 1) + '.' + line.slice(pos + 1);
      return line;
    }).join('\n');
  }

  function updateEditor(value) {
    const el = getEditor();
    if (!el) return;
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
  }

  async function importFile(event) {
    const file = event.target.files?.[0];
    event.target.value = ''; // Có thể import lại cùng file.
    if (!file) return;
    if (!/\.(txt|md)$/i.test(file.name)) { toast('Chỉ nhận file .txt hoặc .md.'); return; }
    if (file.size > 5 * 1024 * 1024) { toast('File quá lớn (tối đa 5 MB).'); return; }
    try {
      const value = normalize(await file.text(), /\.md$/i.test(file.name));
      updateEditor(value);
      toast(`Đã nhập ${file.name} • ${formatter.format(value.length)} ký tự • đã chuẩn hóa dấu câu.`);
      if (value.length > MAX) toast(`File vượt ${formatter.format(MAX)} ký tự. Nội dung vẫn được giữ nguyên trong khung soạn thảo.`, 6500);
      getEditor()?.focus();
    } catch (error) { toast(`Không đọc được file: ${error.message || error}`); }
  }

  async function pasteText() {
    const el = getEditor();
    if (!el) return;
    try {
      const value = await navigator.clipboard.readText();
      if (!value) { toast('Bộ nhớ tạm đang trống.'); return; }
      el.setRangeText(value, el.selectionStart ?? el.value.length, el.selectionEnd ?? el.value.length, 'end');
      el.dispatchEvent(new Event('input', { bubbles: true }));
    } catch (_) { toast('Nhấn Ctrl + V để dán văn bản.'); }
    el.focus();
  }

  function clearText() { updateEditor(''); getEditor()?.focus(); }
  function normalizeEditor() {
    const el = getEditor();
    if (!el) return;
    const before = el.value;
    updateEditor(normalize(before));
    toast('Đã bổ sung dấu kết câu ở cuối các dòng và giữ nguyên ngắt đoạn.');
    el.focus();
  }

  function attachActions() {
    if (listenersBound) return;
    document.getElementById('lkh-paste-button')?.addEventListener('click', pasteText);
    document.getElementById('lkh-clear-button')?.addEventListener('click', clearText);
    document.getElementById('lkh-normalize-button')?.addEventListener('click', normalizeEditor);
    document.getElementById('lkh-file-input')?.addEventListener('change', importFile);
    listenersBound = true;
  }

  return {
    register: async (id, secret, dotnet) => {
      sessionId = id;
      sessionKey = secret;
      callback = dotnet;
      editor = document.getElementById('lkh-editor');
      editor?.addEventListener('input', refresh);
      attachActions();
      refresh();
      window.addEventListener('pagehide', closeTab, { once: true });
      try {
        const saved = JSON.parse(sessionStorage.getItem(SESSION_STORE) || 'null');
        if (saved?.id && saved?.key && !(await isOwnedByAnotherTab(saved.id))) {
          const ok = await dotnet.invokeMethodAsync('RestoreSession', saved.id, saved.key);
          if (ok) { sessionId = saved.id; sessionKey = saved.key; }
        }
      } catch (_) { /* Phiên mất / cache cũ: tạo một phiên tab mới. */ }
      try { sessionStorage.setItem(SESSION_STORE, JSON.stringify({ id: sessionId, key: sessionKey })); }
      catch (_) { /* Browser cấm storage: dùng phiên một lượt. */ }
      fetch(apiUrl('touch'), { method: 'POST' }).catch(() => {});
      if (heartbeat) clearInterval(heartbeat);
      heartbeat = setInterval(() => {
        if (sessionId && sessionKey)
          fetch(apiUrl('touch'), { method: 'POST' }).catch(() => {});
      }, 60 * 1000);
    },
    getEditorText: () => {
      const value = getEditor()?.value || '';
      if (value.length > MAX) throw new Error('LKH_100000_CHARACTERS_LIMIT');
      return value;
    },
    download: url => {
      const link = document.createElement('a');
      link.href = url;
      link.download = '';
      link.style.display = 'none';
      document.body.appendChild(link);
      link.click();
      link.remove();
    }
  };
})();
