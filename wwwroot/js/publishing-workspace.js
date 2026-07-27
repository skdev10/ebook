(function () {
  const root = document.getElementById('pub-workspace');
  const projectId = root ? parseInt(root.getAttribute('data-project-id'), 10) : 0;
  const indicator = document.getElementById('save-indicator');
  let saveTimer = null;

  function setSaving(state) {
    if (!indicator) return;
    indicator.textContent = state;
  }

  function debounceSave(fn) {
    setSaving('Saving…');
    clearTimeout(saveTimer);
    saveTimer = setTimeout(async () => {
      try { await fn(); setSaving('Saved'); }
      catch { setSaving('Save failed — retrying on next change'); }
    }, 2000);
  }

  async function postJson(url, body) {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    return res.json();
  }

  function collectSetup() {
    const ptype = document.querySelector('input[name="ptype"]:checked');
    const trimSel = document.getElementById('trim-select');
    let w = parseFloat(document.getElementById('trim-w')?.value || '6');
    let h = parseFloat(document.getElementById('trim-h')?.value || '9');
    let custom = false;
    if (trimSel && trimSel.value !== 'custom') {
      const opt = trimSel.options[trimSel.selectedIndex];
      w = parseFloat(opt.getAttribute('data-w'));
      h = parseFloat(opt.getAttribute('data-h'));
    } else if (trimSel?.value === 'custom') custom = true;
    return {
      projectId,
      projectType: ptype ? parseInt(ptype.value, 10) : 1,
      trimWidthIn: w,
      trimHeightIn: h,
      isCustomTrim: custom,
      paperType: parseInt(document.getElementById('paper-type')?.value || '0', 10),
      hasBleed: document.getElementById('bleed-mode')?.value === '1'
    };
  }

  document.querySelectorAll('.setup-field').forEach(el => {
    el.addEventListener('change', () => {
      const printSetup = document.getElementById('print-setup');
      const ptype = document.querySelector('input[name="ptype"]:checked');
      if (printSetup && ptype) printSetup.classList.toggle('hidden', ptype.value === '0');
      const useRec = document.getElementById('use-recommended');
      ['m-top', 'm-bottom', 'm-out', 'm-in'].forEach(id => {
        const i = document.getElementById(id);
        if (i && useRec) i.readOnly = !!useRec.checked;
      });
      const trimSel = document.getElementById('trim-select');
      const custom = document.getElementById('custom-trim');
      if (trimSel && custom) custom.classList.toggle('hidden', trimSel.value !== 'custom');
      debounceSave(async () => {
        const data = await postJson('/Publishing/SaveSetup', collectSetup());
        if (data.gutterBand) {
          const gb = document.getElementById('gutter-band');
          if (gb) gb.textContent = data.gutterBand;
        }
        if (data.margins) {
          const m = data.margins;
          const map = { 'm-top': m.marginTopIn, 'm-bottom': m.marginBottomIn, 'm-out': m.marginOutsideIn, 'm-in': m.marginInsideIn };
          Object.keys(map).forEach(id => { const el = document.getElementById(id); if (el && map[id] != null) el.value = map[id]; });
        }
      });
    });
  });

  document.querySelectorAll('.typo-field').forEach(el => {
    el.addEventListener('change', () => debounceSave(async () => {
      const ebook = document.getElementById('typo-form')?.getAttribute('data-ebook') === '1';
      await postJson('/Publishing/SaveTypography', {
        projectId,
        isEbookProfile: ebook,
        profile: {
          bodyFontFamily: document.getElementById('body-font')?.value,
          bodyFontSizePt: parseFloat(document.getElementById('body-size')?.value || '11'),
          lineSpacing: parseFloat(document.getElementById('line-spacing')?.value || '1.15'),
          textAlignment: document.getElementById('align')?.value,
          h1FontSizePt: parseFloat(document.getElementById('h1-size')?.value || '18'),
          firstLineIndentIn: parseFloat(document.getElementById('indent')?.value || '0.25'),
          noIndentOnFirstPara: !!document.getElementById('no-indent-first')?.checked,
          useRecommendedMargins: true
        }
      });
      const frame = document.getElementById('preview-frame');
      if (frame) frame.src = frame.src.split('?')[0] + '?t=' + Date.now();
    }));
  });

  document.getElementById('reset-typo')?.addEventListener('click', () => {
    const body = document.getElementById('body-size');
    if (body) body.value = '11';
    const ls = document.getElementById('line-spacing');
    if (ls) ls.value = '1.15';
    document.querySelector('.typo-field')?.dispatchEvent(new Event('change'));
  });

  document.getElementById('run-paginate')?.addEventListener('click', async () => {
    setSaving('Paginating…');
    const data = await postJson('/Publishing/Paginate/' + projectId, {});
    if (data.pageCount != null) {
      const el = document.getElementById('live-page-count');
      if (el) el.textContent = data.pageCount;
    }
    if (data.gutterBand) {
      const gb = document.getElementById('gutter-band');
      if (gb) gb.textContent = data.gutterBand;
    }
    setSaving('Saved');
  });

  document.querySelectorAll('.kdp-tip').forEach(btn => {
    btn.addEventListener('click', () => alert(btn.getAttribute('data-tip')));
  });

  // Preview pane: single/spread toggle, table-of-contents scrolling, and page-by-page navigation.
  const previewFrame = document.getElementById('preview-frame');
  function getPreviewPages() {
    if (!previewFrame) return [];
    try {
      const doc = previewFrame.contentDocument || previewFrame.contentWindow.document;
      return Array.from(doc.querySelectorAll('.preview-page'));
    } catch { return []; }
  }
  let currentPreviewIndex = 0;
  function scrollToPreviewIndex(idx) {
    const pages = getPreviewPages();
    if (!pages.length) return;
    currentPreviewIndex = Math.max(0, Math.min(idx, pages.length - 1));
    pages[currentPreviewIndex].scrollIntoView({ behavior: 'smooth', block: 'start' });
    const goto = document.getElementById('goto-page');
    if (goto) goto.value = currentPreviewIndex + 1;
  }
  function setPreviewMode(spread) {
    if (!previewFrame) return;
    const base = previewFrame.src.split('?')[0];
    previewFrame.src = base + '?spread=' + (spread ? 'true' : 'false') + '&t=' + Date.now();
    const single = document.getElementById('mode-single');
    const spreadBtn = document.getElementById('mode-spread');
    single?.classList.toggle('bg-violet-50', !spread);
    single?.classList.toggle('border-violet-300', !spread);
    spreadBtn?.classList.toggle('bg-violet-50', !!spread);
    spreadBtn?.classList.toggle('border-violet-300', !!spread);
  }
  document.getElementById('mode-single')?.addEventListener('click', () => setPreviewMode(false));
  document.getElementById('mode-spread')?.addEventListener('click', () => setPreviewMode(true));
  document.getElementById('prev-page')?.addEventListener('click', () => scrollToPreviewIndex(currentPreviewIndex - 1));
  document.getElementById('next-page')?.addEventListener('click', () => scrollToPreviewIndex(currentPreviewIndex + 1));
  document.getElementById('goto-page')?.addEventListener('change', e => scrollToPreviewIndex(parseInt(e.target.value || '1', 10) - 1));
  document.querySelectorAll('.toc-link').forEach(a => {
    a.addEventListener('click', e => {
      e.preventDefault();
      const targetId = (a.getAttribute('href') || '').replace('#', '');
      if (!previewFrame || !targetId) return;
      try {
        const doc = previewFrame.contentDocument || previewFrame.contentWindow.document;
        const el = doc.getElementById(targetId);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      } catch { /* cross-origin fallback: nothing to do */ }
    });
  });

  // Sortable pages panel
  const sortableEl = document.getElementById('pages-sortable');
  if (sortableEl && window.Sortable) {
    Sortable.create(sortableEl, {
      animation: 150,
      onEnd: () => debounceSave(async () => {
        const items = [...sortableEl.querySelectorAll('[data-id]')];
        const sections = items.map((li, idx) => ({
          id: parseInt(li.getAttribute('data-id'), 10),
          orderIndex: idx,
          title: li.querySelector('.sec-title')?.value || ''
        }));
        await postJson('/Publishing/SaveSections', { projectId, sections });
      })
    });
  }

  async function startExport(btn) {
    const format = parseInt(btn.getAttribute('data-format') || document.querySelector('input[name="exfmt"]:checked')?.value || '0', 10);
    const pid = parseInt(btn.getAttribute('data-project-id') || projectId, 10);
    if (!pid) { alert('Upload a manuscript first, then export from the workspace.'); return; }
    const data = await postJson('/Publishing/Export', {
      projectId: pid,
      format,
      includeCover: false,
      includeFrontMatter: document.getElementById('ex-front')?.checked !== false,
      includeBackMatter: document.getElementById('ex-back')?.checked !== false
    });
    const warn = document.getElementById('export-warnings');
    if (warn && data.warnings?.length) warn.textContent = data.warnings.join(' ');
    const prog = document.getElementById('export-progress');
    if (prog) { prog.classList.remove('hidden'); prog.textContent = 'Export started…'; }
    if (!data.jobId) return;
    const timer = setInterval(async () => {
      const st = await fetch('/Publishing/ExportStatus/' + data.jobId).then(r => r.json());
      if (prog) prog.textContent = (st.message || st.status) + ' (' + (st.percent || 0) + '%)';
      if (st.status === 'Succeeded' && st.downloadUrl) {
        clearInterval(timer);
        if (prog) prog.innerHTML = 'Done. <a class="text-violet-700 font-semibold" href="' + st.downloadUrl + '">Download your file</a>';
      }
      if (st.status === 'Failed') {
        clearInterval(timer);
        if (prog) prog.textContent = st.error || 'Export failed. Please try again.';
      }
    }, 1000);
  }

  document.querySelectorAll('.pub-export-btn').forEach(btn => {
    btn.addEventListener('click', () => startExport(btn));
  });
})();
