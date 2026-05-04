// editor.js - mounts into #editor-root
(function () {
    // create editor DOM (a compacted version of previous UI for clarity)
    const root = document.getElementById('editor-root');
    root.innerHTML = `
  <div class="editor-wrapper position-relative">
    <div id="dropOverlay" class="drop-overlay">Drop images here</div>
    <div class="d-flex mb-2">
      <button id="themeToggle" class="btn btn-sm btn-dark">🌙 Dark Mode</button>
      <div class="ms-auto small text-muted">Words: <span id="wordCount">0</span> Chars: <span id="charCount">0</span></div>
    </div>
    <div class="editor-toolbar p-2 mb-2 bg-white border rounded">
      <button onclick="format('bold')" class="btn btn-sm btn-light">B</button>
      <button onclick="format('italic')" class="btn btn-sm btn-light">I</button>
      <button onclick="format('underline')" class="btn btn-sm btn-light">U</button>
      <select onchange="applyHeading(this.value)"><option value='p'>Paragraph</option><option value='h1'>H1</option><option value='h2'>H2</option><option value='h3'>H3</option></select>
      <button id="btnExport" class="btn btn-sm btn-primary">Export PDF</button>
    </div>
    <div class="row gx-3">
      <div class="col-md-8"><div id="editor" contenteditable="true" spellcheck="true"></div></div>
      <div class="col-md-4"><div id="previewArea" class="paged-preview"></div></div>
    </div>
  </div>`;

    // expose helpers to global scope used by toolbar
    window.format = function (cmd) { document.execCommand(cmd, false, null); editor.focus(); scheduleAutoSave(); updateCounts(); };
    window.applyHeading = function (tag) { document.execCommand('formatBlock', false, tag); scheduleAutoSave(); };

    const editor = document.getElementById('editor');
    const preview = document.getElementById('previewArea');
    const wordCountEl = document.getElementById('wordCount');
    const charCountEl = document.getElementById('charCount');

    // AutoSave
    let AutoSaveTimer = null;
    function scheduleAutoSave() { if (AutoSaveTimer) clearTimeout(AutoSaveTimer); AutoSaveTimer = setTimeout(doAutoSave, 1000); }
    async function doAutoSave() {
        const content = editor.innerHTML;
        try {
            const res = await fetch('/Books/AutoSave', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ bookId: 0, content }) });
            const j = await res.json();
            console.log('AutoSave', j);
        } catch (e) { console.warn(e); }
    }

    // Counts
    function stripHtml(html) { const tmp = document.createElement('div'); tmp.innerHTML = html; return tmp.textContent || tmp.innerText || ''; }
    function updateCounts() { const text = stripHtml(editor.innerHTML).replace(/\u00A0/g, ' '); const words = text.trim() ? text.trim().split(/\s+/).length : 0; wordCountEl.textContent = words; charCountEl.textContent = text.length; }
    editor.addEventListener('input', () => { updateCounts(); scheduleAutoSave(); paginateDebounced(); });
    updateCounts();

    // Drag & drop image simple handler (fallback to insert base64)
    editor.addEventListener('drop', async e => { e.preventDefault(); const f = e.dataTransfer.files[0]; if (f && f.type.startsWith('image/')) { await uploadFile(f); } });

    async function uploadFile(file) { const fd = new FormData(); fd.append('file', file); const res = await fetch('/Books/UploadImage', { method: 'POST', body: fd }); const j = await res.json(); const url = j.url || await fileToDataURL(file); document.execCommand('insertImage', false, url); scheduleAutoSave(); }
    function fileToDataURL(file) { return new Promise((res, rej) => { const r = new FileReader(); r.onload = e => res(e.target.result); r.onerror = rej; r.readAsDataURL(file); }); }

    // Pagination
    const A4_HEIGHT = 1123; function createPage() { const d = document.createElement('div'); d.className = 'page'; return d; }
    function paginate() {
        preview.innerHTML = ''; const tmp = document.createElement('div'); tmp.style.width = '794px'; tmp.innerHTML = editor.innerHTML; document.body.appendChild(tmp);
        let page = createPage(); preview.appendChild(page); let currHeight = 0; Array.from(tmp.childNodes).forEach(node => {
            page.appendChild(node.cloneNode(true));
            if (page.scrollHeight > A4_HEIGHT) {
                page.removeChild(page.lastChild);
                page = createPage(); preview.appendChild(page);
                page.appendChild(node.cloneNode(true));
            }
        });
        document.body.removeChild(tmp);
        // page numbers
        Array.from(preview.children).forEach((p, i) => { const pn = document.createElement('div'); pn.className = 'page-number'; pn.textContent = 'Page ' + (i + 1); p.appendChild(pn); });
    }
    const paginateDebounced = debounce(paginate, 500);

    // Export PDF — calls server endpoint
    document.getElementById('btnExport').addEventListener('click', async () => {
        const html = editor.innerHTML;
        const res = await fetch('/Books/ExportPdf', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ html }) });
        if (!res.ok) { alert('Export failed'); return; }
        const blob = await res.blob(); const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url; a.download = 'ebook.pdf'; document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url);
    });

    // utilities
    function debounce(fn, wait) { let t; return function (...args) { clearTimeout(t); t = setTimeout(() => fn.apply(this, args), wait); }; }

    // expose paginate for manual render
    window.renderPagedPreview = paginate;

})();
