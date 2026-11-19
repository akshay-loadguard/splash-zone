// Provides navigator.serial inside the desktop app (WebView2 has no Web Serial).
// Bytes come from the app's native serial port code via chrome.webview messages.
(() => {
  if (!window.chrome || !chrome.webview) return;
  const wv = chrome.webview;
  let seq = 0, active = null;
  const pending = new Map();

  wv.addEventListener('message', e => {
    const m = e.data;
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
    if (m.type === 'data' && active && active._ctrl) active._ctrl.enqueue(new Uint8Array(m.bytes));
    if (m.type === 'closed' && active) {
      const p = active; active = null; p.readable = null;
      try { p._ctrl && p._ctrl.close(); } catch {}
      p._ctrl = null;
    }
  });
  const call = (cmd, extra = {}) => new Promise(res => { const id = ++seq; pending.set(id, res); wv.postMessage({ cmd, id, ...extra }); });

  class Port {
    constructor(info) { this.path = info.path; this.name = info.name; this.readable = null; this._ctrl = null; }
    getInfo() { return {}; }
    async open({ baudRate = 9600 } = {}) {
      const r = await call('open', { path: this.path, baud: baudRate | 0 });
      if (!r.ok) throw new DOMException(r.error || 'Could not open port', 'NetworkError');
      try { localStorage.setItem('__lastPort', this.path); } catch {}
      active = this;
      this.readable = new ReadableStream({
        start: c => { this._ctrl = c; },
        cancel: () => { this._ctrl = null; this.readable = null; if (active === this) active = null; call('close'); },
      });
    }
    async close() { this.readable = null; this._ctrl = null; if (active === this) active = null; await call('close'); }
  }

  function pickPort(ports) {
    return new Promise((resolve, reject) => {
      const wrap = document.createElement('div');
      wrap.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.6);display:flex;align-items:center;justify-content:center;z-index:99999;font-family:Segoe UI,sans-serif';
      const box = document.createElement('div');
      box.style.cssText = 'background:#1f2540;color:#fff;border-radius:16px;padding:20px 24px;width:min(520px,92vw);max-height:80vh;overflow:auto';
      box.innerHTML = '<h2 style="margin:0 0 12px">Choose the CSI port</h2>';
      const done = p => { wrap.remove(); p ? resolve(new Port(p)) : reject(new DOMException('No port selected', 'NotFoundError')); };
      if (!ports.length) box.insertAdjacentHTML('beforeend', '<p style="color:#a9b2d4">No COM ports found. Pair or plug in the switch interface, then try again.</p>');
      ports.forEach(p => {
        const b = document.createElement('button');
        b.textContent = p.name;
        b.style.cssText = 'display:block;width:100%;text-align:left;margin:6px 0;padding:12px;border:0;border-radius:10px;background:#343c66;color:#fff;font-size:15px;cursor:pointer';
        b.onclick = () => done(p);
        box.appendChild(b);
      });
      const c = document.createElement('button');
      c.textContent = 'Cancel';
      c.style.cssText = 'margin-top:10px;padding:8px 16px;border:0;border-radius:8px;background:#555;color:#fff;cursor:pointer';
      c.onclick = () => done(null);
      box.appendChild(c); wrap.appendChild(box); document.body.appendChild(wrap);
    });
  }

  const serial = {
    async requestPort() { const r = await call('list'); return pickPort(r.ports || []); },
    async getPorts() {
      let last = null; try { last = localStorage.getItem('__lastPort'); } catch {}
      if (!last) return [];
      const r = await call('list');
      const p = (r.ports || []).find(x => x.path === last);
      return p ? [new Port(p)] : [];
    },
  };
  Object.defineProperty(navigator, 'serial', { value: serial, configurable: true });

  document.addEventListener('keydown', e => { if (e.key === 'F11') { e.preventDefault(); wv.postMessage({ cmd: 'fullscreen' }); } });
})();
