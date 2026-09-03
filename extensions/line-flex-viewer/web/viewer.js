/* Client controller for the Flex Message viewer iframe. */
(function () {
  "use strict";

  const cfg = window.__FLEX_CFG__ || { docId: "default" };
  const editor = document.getElementById("editor");
  const preview = document.getElementById("preview");
  const status = document.getElementById("status");
  const dirtyFlag = document.getElementById("dirty-flag");
  const previewScroll = document.getElementById("preview-scroll");

  let renderTimer = null;
  let saveTimer = null;
  let dirty = false;
  let lastServerText = "";

  // --- status helpers -----------------------------------------------------

  function setStatus(msg, kind) {
    status.textContent = msg || "";
    status.className = "status-bar" + (kind ? " " + kind : "");
  }

  function setDirty(v) {
    dirty = v;
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

  // --- persistence --------------------------------------------------------

  function saveToServer(value) {
    fetch("./api/state", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ docId: cfg.docId, content: value }),
    })
      .then(() => {
        lastServerText = editor.value;
        setDirty(false);
      })
      .catch(() => {});
  }

  function scheduleSave(value) {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => saveToServer(value), 400);
  }

  // --- events -------------------------------------------------------------

  function onEdit() {
    setDirty(editor.value !== lastServerText);
    if (renderTimer) clearTimeout(renderTimer);
    renderTimer = setTimeout(() => {
      const value = render();
      if (value !== null) scheduleSave(value);
    }, 350);
  }

  function doRenderNow() {
    const value = render();
    if (value !== null) saveToServer(value);
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
    navigator.clipboard.writeText(editor.value).then(
      () => setStatus("Copied to clipboard", "ok"),
      () => setStatus("Failed to copy", "error")
    );
  }

  function setContent(value, opts) {
    opts = opts || {};
    editor.value = JSON.stringify(value, null, 2);
    lastServerText = editor.value;
    setDirty(false);
    render();
    if (opts.flash) setStatus("The assistant updated the content", "ok");
  }

  // Tab key inserts spaces instead of leaving the textarea.
  function handleTab(e) {
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

  // --- SSE (assistant pushes) ---------------------------------------------

  function connectEvents() {
    try {
      const es = new EventSource("./api/events?docId=" + encodeURIComponent(cfg.docId));
      es.addEventListener("content", (ev) => {
        try {
          const data = JSON.parse(ev.data);
          if (data && data.content !== undefined) {
            setContent(data.content, { flash: true });
          }
        } catch (_) {}
      });
      es.onerror = () => {
        /* browser auto-reconnects */
      };
    } catch (_) {}
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
      if (s) {
        setContent(s.value);
        saveToServer(s.value);
      }
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
    document.getElementById("btn-bg").addEventListener("click", toggleBg);
    editor.addEventListener("input", onEdit);
    editor.addEventListener("keydown", handleTab);
    populateSamples();
    connectEvents();

    fetch("./api/state?docId=" + encodeURIComponent(cfg.docId))
      .then((r) => r.json())
      .then((data) => {
        if (data && data.content !== undefined && data.content !== null) {
          setContent(data.content);
        } else {
          setStatus("Enter Flex JSON or load a sample.");
        }
      })
      .catch(() => setStatus("Failed to load state."));
  }

  init();
})();
