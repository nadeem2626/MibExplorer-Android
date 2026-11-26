// Runs the real bridge.js against a minimal fake DOM and fake xterm. Checks: DOM paste is intercepted and routed to the host
// (never reaches term.onData), Ctrl+V / Shift+Insert route to the host, copy sends the selection, output goes to term.write only
// (no HTML path), unknown host messages are ignored, and only the documented message types are posted.
const fs = require("fs"), path = require("path"), vm = require("vm");
const src = fs.readFileSync(path.join(__dirname, "../../MibExplorer.Android/Resources/Raw/terminal/bridge.js"), "utf8");
let pass = 0, fail = 0; const check = (n, ok) => { console.log((ok ? "PASS " : "FAIL ") + n); ok ? pass++ : fail++; };

function run() {
  const posted = [], listeners = { document: {}, window: {} }, written = [], pasted = []; let dataCb = null, selection = "";
  const mk = () => ({ contains: () => true, addEventListener() {}, hidden: true, querySelector: () => null, style: {}, getBoundingClientRect: () => ({ width: 0, height: 0 }), textContent: "", closest: () => null });
  const root = mk(), menu = mk();
  const doc = {
    getElementById: id => id === "terminal" ? root : id === "terminal-menu" ? menu : null,
    addEventListener(t, fn, cap) { (listeners.document[t] = listeners.document[t] || []).push(fn); },
    fonts: { ready: { then() {} } }
  };
  class Terminal {
    loadAddon() {} open() {} focus() {} clear() {} selectAll() {}
    onData(cb) { dataCb = cb; } onBinary() {} onResize() {}
    hasSelection() { return !!selection; } getSelection() { return selection; }
    attachCustomKeyEventHandler() {} write(d) { written.push(d); } paste(d) { pasted.push(d); }
  }
  const win = { mibHost: { postMessage: s => posted.push(JSON.parse(s)) }, Terminal, FitAddon: { FitAddon: class { fit() {} } },
    addEventListener(t, fn) { (listeners.window[t] = listeners.window[t] || []).push(fn); } };
  const ctx = { window: win, document: doc, requestAnimationFrame: f => 0, ResizeObserver: class { observe() {} }, btoa: s => Buffer.from(s, "binary").toString("base64"), JSON, console };
  win.window = win; vm.createContext(ctx); vm.runInContext(src, ctx);
  const fire = (t, ev) => { ev = Object.assign({ target: root, preventDefault() { ev.prevented = true; }, stopImmediatePropagation() { ev.stopped = true; } }, ev); (listeners.document[t] || []).forEach(f => { if (!ev.stopped) f(ev); }); return ev; };
  return { posted, written, pasted, fire, win, setSel: s => { selection = s; }, typeData: d => dataCb(d) };
}

let t = run();
check("posts ready on start", t.posted.some(m => m.type === "ready"));
let e = t.fire("paste", { clipboardData: { getData: () => "rm -rf /\nls" } });
check("DOM paste is prevented and stopped", e.prevented === true && e.stopped === true);
check("DOM paste asks the host (clipboardPaste) and carries no text", t.posted.filter(m => m.type === "clipboardPaste").length === 1 && !JSON.stringify(t.posted).includes("rm -rf"));
check("DOM paste produced no input message", !t.posted.some(m => m.type === "input"));

t = run(); e = t.fire("keydown", { key: "v", ctrlKey: true, shiftKey: false, altKey: false, metaKey: false, isComposing: false });
check("Ctrl+V routes to host paste request, prevented", e.prevented && t.posted.some(m => m.type === "clipboardPaste") && !t.posted.some(m => m.type === "input"));
t = run(); e = t.fire("keydown", { key: "Insert", ctrlKey: false, shiftKey: true, altKey: false, metaKey: false, isComposing: false });
check("Shift+Insert routes to host paste request", t.posted.some(m => m.type === "clipboardPaste"));

t = run(); t.setSel("hello"); t.fire("keydown", { key: "c", ctrlKey: true, shiftKey: false, altKey: false, metaKey: false, isComposing: false });
check("Ctrl+C with selection copies the selection", t.posted.some(m => m.type === "clipboardCopy" && m.data === "hello"));
t = run(); e = t.fire("keydown", { key: "c", ctrlKey: true, shiftKey: false, altKey: false, metaKey: false, isComposing: false });
check("Ctrl+C without selection is NOT intercepted (reaches the shell as 0x03)", !e.prevented);
t = run(); t.setSel("sel"); t.win.__mibCopySelection();
check("host Copy button hook posts the selection", t.posted.some(m => m.type === "clipboardCopy" && m.data === "sel"));
t = run(); t.win.__mibCopySelection();
check("host Copy with nothing selected posts an empty string", t.posted.some(m => m.type === "clipboardCopy" && m.data === ""));

t = run(); t.typeData("ls\r");
check("typed data is posted as input", t.posted.some(m => m.type === "input" && m.data === "ls\r"));

t = run(); t.win.__mibRecv({ type: "output", data: "<img src=x onerror=alert(1)>\u001b[31mred" });
check("output goes to term.write verbatim (no HTML path exists)", t.written.length === 1 && t.written[0].startsWith("<img"));
t.win.__mibRecv({ type: "bogus", data: "x" }); t.win.__mibRecv(null); t.win.__mibRecv({ type: 5 });
check("unknown or malformed host messages are ignored", t.written.length === 1 && t.pasted.length === 0);
const types = new Set(t.posted.map(m => m.type));
check("only documented message types are posted", [...types].every(x => ["ready", "input", "binary", "resize", "clipboardCopy", "clipboardPaste"].includes(x)));
check("source never assigns innerHTML or calls eval/Function", !/innerHTML|outerHTML|insertAdjacentHTML|\beval\s*\(|new Function|document\.write/.test(src));
console.log(`terminal bridge js: ${pass} pass, ${fail} fail`); process.exit(fail ? 1 : 0);
