/*
 * Standalone controller for the LINE Flex Message viewer.
 *
 * Runs entirely in the browser with NO server and NO Copilot App:
 * open standalone.html directly (file://) or serve the web/ folder with any
 * static server. Persistence uses localStorage; JSON can be imported/exported
 * as a file or shared via a URL hash (#json=<base64>).
 */
(function () {
  "use strict";

  const STORAGE_KEY = "line-flex-viewer:standalone:content";

  const editor = document.getElementById("editor");
  const preview = document.getElementById("preview");
  const status = document.getElementById("status");
  const dirtyFlag = document.getElementById("dirty-flag");
  const previewScroll = document.getElementById("preview-scroll");
  const fileInput = document.getElementById("file-input");

  let renderTimer = null;
  let saveTimer = null;
  let lastSavedText = "";

  // --- status helpers -----------------------------------------------------

  function setStatus(msg, kind) {
    status.textContent = msg || "";
    status.className = "status-bar" + (kind ? " " + kind : "");
  }

  function setDirty(v) {
    dirtyFlag.textContent = v ? "● Unsaved" : "";
  }

  // --- parse + render -----------------------------------------------------

  function parseEditor() {
    const text = editor.value;
    if (!text.trim()) return { ok: false, error: "JSON is empty" };
    try {
      return { ok: true, value: JSON.parse(text) };
    } catch (e) {
      return { ok: false, error: "JSON syntax error: " + e.message };
    }
  }

  function render() {
    const parsed = parseEditor();
    if (!parsed.ok) {
      setStatus(parsed.error, "error");
      return null;
    }
    try {
      window.FlexRenderer.render(preview, parsed.value);
      const warnings = window.FlexRenderer.validate(parsed.value);
      if (warnings.length) {
        setStatus("⚠ " + warnings.join(" / "), "error");
      } else {
        setStatus("✓ Rendered successfully", "ok");
      }
      return parsed.value;
    } catch (e) {
      setStatus("Render error: " + e.message, "error");
      return null;
    }
  }

  // --- persistence (localStorage) -----------------------------------------

  function saveLocal(text) {
    try {
      localStorage.setItem(STORAGE_KEY, text);
    } catch (_) {}
    lastSavedText = text;
    setDirty(false);
  }

  function scheduleSave(text) {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => saveLocal(text), 400);
  }

  function loadLocal() {
    try {
      return localStorage.getItem(STORAGE_KEY);
    } catch (_) {
      return null;
    }
  }

  // --- events -------------------------------------------------------------

  function onEdit() {
    setDirty(editor.value !== lastSavedText);
    if (renderTimer) clearTimeout(renderTimer);
    renderTimer = setTimeout(() => {
      const value = render();
      if (value !== null) scheduleSave(editor.value);
    }, 350);
  }

  function doRenderNow() {
    const value = render();
    if (value !== null) saveLocal(editor.value);
  }

  function formatJson() {
    const parsed = parseEditor();
    if (!parsed.ok) {
      setStatus(parsed.error, "error");
      return;
    }
    editor.value = JSON.stringify(parsed.value, null, 2);
    onEdit();
  }

  function copyJson() {
    const text = editor.value;
    const done = () => setStatus("Copied to clipboard", "ok");
    const fail = () => setStatus("Failed to copy", "error");
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done, fail);
    } else {
      try {
        editor.select();
        document.execCommand("copy");
        done();
      } catch (_) {
        fail();
      }
    }
  }

  function setContent(value) {
    editor.value = typeof value === "string" ? value : JSON.stringify(value, null, 2);
    render();
    saveLocal(editor.value);
  }

  // --- file import / export -----------------------------------------------

  function openFile() {
    fileInput.value = "";
    fileInput.click();
  }

  function onFileChosen() {
    const file = fileInput.files && fileInput.files[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      try {
        const parsed = JSON.parse(String(reader.result));
        setContent(parsed);
        setStatus(`Loaded: ${file.name}`, "ok");
      } catch (e) {
        setStatus("Failed to parse JSON from file: " + e.message, "error");
      }
    };
    reader.onerror = () => setStatus("Failed to read the file", "error");
    reader.readAsText(file);
  }

  function downloadJson() {
    const parsed = parseEditor();
    if (!parsed.ok) {
      setStatus(parsed.error, "error");
      return;
    }
    const text = JSON.stringify(parsed.value, null, 2);
    const blob = new Blob([text], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "flex-message.json";
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    setStatus("Downloaded flex-message.json", "ok");
  }

  // --- share via URL hash (#json=<base64>) --------------------------------

  function encodeHash(text) {
    // Handle non-ASCII (unescape/encodeURIComponent trick) before base64.
    return btoa(unescape(encodeURIComponent(text)));
  }

  function decodeHash(b64) {
    return decodeURIComponent(escape(atob(b64)));
  }

  function shareLink() {
    const parsed = parseEditor();
    if (!parsed.ok) {
      setStatus(parsed.error, "error");
      return;
    }
    const text = JSON.stringify(parsed.value);
    let hash;
    try {
      hash = "#json=" + encodeHash(text);
    } catch (e) {
      setStatus("Failed to generate the share link", "error");
      return;
    }
    const url = location.origin + location.pathname + hash;
    // Update the address bar without reloading.
    try {
      history.replaceState(null, "", hash);
    } catch (_) {}
    const done = () => setStatus("Copied share link (also reflected in the URL)", "ok");
    const fail = () => setStatus("Couldn't copy the link. Copy the URL from the address bar instead.", "error");
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(url).then(done, fail);
    } else {
      fail();
    }
  }

  function contentFromHash() {
    const h = location.hash || "";
    const m = h.match(/^#json=(.+)$/);
    if (!m) return null;
    try {
      return decodeHash(m[1]);
    } catch (_) {
      return null;
    }
  }

  // Tab inserts spaces; Ctrl/Cmd+Enter renders immediately.
  function handleKeys(e) {
    if (e.key === "Tab") {
      e.preventDefault();
      const s = editor.selectionStart;
      const eSel = editor.selectionEnd;
      editor.value = editor.value.slice(0, s) + "  " + editor.value.slice(eSel);
      editor.selectionStart = editor.selectionEnd = s + 2;
      onEdit();
    } else if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
      e.preventDefault();
      doRenderNow();
    }
  }

  // --- samples ------------------------------------------------------------

  function populateSamples() {
    const sel = document.getElementById("sample-select");
    (window.FLEX_SAMPLES || []).forEach((s) => {
      const opt = document.createElement("option");
      opt.value = s.id;
      opt.textContent = s.label;
      sel.appendChild(opt);
    });
    sel.addEventListener("change", () => {
      const s = (window.FLEX_SAMPLES || []).find((x) => x.id === sel.value);
      if (s) setContent(s.value);
      sel.value = "";
    });
  }

  function toggleBg() {
    previewScroll.dataset.bg = previewScroll.dataset.bg === "dark" ? "light" : "dark";
  }

  // --- init ---------------------------------------------------------------

  function init() {
    document.getElementById("btn-render").addEventListener("click", doRenderNow);
    document.getElementById("btn-format").addEventListener("click", formatJson);
    document.getElementById("btn-copy").addEventListener("click", copyJson);
    document.getElementById("btn-open").addEventListener("click", openFile);
    document.getElementById("btn-download").addEventListener("click", downloadJson);
    document.getElementById("btn-share").addEventListener("click", shareLink);
    document.getElementById("btn-bg").addEventListener("click", toggleBg);
    fileInput.addEventListener("change", onFileChosen);
    editor.addEventListener("input", onEdit);
    editor.addEventListener("keydown", handleKeys);
    populateSamples();

    // Seed priority: URL hash > localStorage > first sample.
    const fromHash = contentFromHash();
    if (fromHash) {
      editor.value = fromHash;
      formatJson();
      render();
      saveLocal(editor.value);
      setStatus("Loaded from share link", "ok");
      return;
    }
    const saved = loadLocal();
    if (saved && saved.trim()) {
      editor.value = saved;
      lastSavedText = saved;
      render();
      return;
    }
    const first = (window.FLEX_SAMPLES || [])[0];
    if (first) {
      setContent(first.value);
      setStatus("Showing a sample. Your edits are auto-saved locally.", "ok");
    } else {
      setStatus("Enter Flex JSON.");
    }
  }

  init();
})();
