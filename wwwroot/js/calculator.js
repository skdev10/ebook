'use strict';

document.addEventListener('DOMContentLoaded', () => {

    const elPages       = document.getElementById('Pages');
    const elPaper       = document.getElementById('PaperType');
    const elTitle       = document.getElementById('Title');
    const elAuthor      = document.getElementById('Author');
    const elDesc        = document.getElementById('Description');
    const elDescCounter = document.getElementById('descCounter');
    const elPalette     = document.getElementById('colorPalette');
    const elSizeGroup   = document.getElementById('textSizeGroup');
    const elSpacing     = document.getElementById('lineSpacing');
    const elSpacingLbl  = document.getElementById('lineSpacingLabel');
    const btnCopy       = document.getElementById('btnCopyDims');
    const btnCopyLbl    = document.getElementById('btnCopyLabel');
    const btnSpec       = document.getElementById('btnSpecSheet');

    const SPACING_LABELS = { '1.4':'Tight','1.6':'Normal','1.8':'Relaxed','2.0':'Loose','2':'Loose' };

    function debounce(fn, ms) { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; }

    function syncState() {
        CoverPreview.setState({
            pages:       parseInt(elPages?.value)  || 150,
            paper:       elPaper?.value            || 'White paper',
            title:       elTitle?.value            || 'My Book',
            author:      elAuthor?.value           || 'Author Name',
            description: elDesc?.value             || '',
        });
    }

    const fetchAndRender = debounce(async () => {
        syncState();
        CoverPreview.renderCover();
        const st = CoverPreview.getState();
        try {
            const resp = await fetch(`/Calculator/Dimensions?pages=${st.pages}&paper=${encodeURIComponent(st.paper)}`);
            if (!resp.ok) return;
            const dims = await resp.json();
            const set = (id, v) => { const e = document.getElementById(id); if (e) e.textContent = v; };
            set('sumSpine',      `${dims.spineInches}" (${dims.spineMm}mm)`);
            set('sumTotalWidth', `${dims.totalWidthInches}" (${dims.totalWidthMm}mm)`);
            set('sumPages',      dims.pages);
            set('sumPaper',      dims.paperType || st.paper);
            set('sumSpineText',  dims.warning ? 'Too narrow \u2717' : 'Visible \u2713');
            const row = document.getElementById('sumSpineRow');
            if (row) row.style.backgroundColor = dims.spineInches < 0.25 ? '#fff9c4' : '';
            const wb = document.getElementById('spineWarning');
            const wt = document.getElementById('spineWarningText');
            if (wb && wt) {
                if (dims.warning) { wt.textContent = dims.warning; wb.classList.remove('d-none'); }
                else { wb.classList.add('d-none'); }
            }
        } catch (_) {}
    }, 120);

    elPages?.addEventListener('input', () => {
        const v = parseInt(elPages.value);
        const err = document.getElementById('pagesError');
        if (isNaN(v) || v < 24 || v > 828) {
            elPages.classList.add('is-invalid');
            if (err) err.textContent = 'Pages must be between 24 and 828.';
        } else {
            elPages.classList.remove('is-invalid');
            if (err) err.textContent = '';
        }
        fetchAndRender();
    });

    elPaper?.addEventListener('change', fetchAndRender);

    elTitle?.addEventListener('input', () => {
        const err = document.getElementById('titleError');
        if (!elTitle.value.trim()) {
            elTitle.classList.add('is-invalid');
            if (err) err.textContent = 'Title is required.';
        } else {
            elTitle.classList.remove('is-invalid');
            if (err) err.textContent = '';
        }
        fetchAndRender();
    });

    elAuthor?.addEventListener('input', () => {
        const err = document.getElementById('authorError');
        if (!elAuthor.value.trim()) {
            elAuthor.classList.add('is-invalid');
            if (err) err.textContent = 'Author name is required.';
        } else {
            elAuthor.classList.remove('is-invalid');
            if (err) err.textContent = '';
        }
        fetchAndRender();
    });

    elDesc?.addEventListener('input', () => {
        if (elDescCounter) elDescCounter.textContent = `${elDesc.value.length}/500`;
        fetchAndRender();
    });

    elPalette?.querySelectorAll('.swatch').forEach(sw => {
        sw.addEventListener('click', () => {
            elPalette.querySelectorAll('.swatch').forEach(s => s.classList.remove('active'));
            sw.classList.add('active');
            CoverPreview.setState({ bg: sw.dataset.bg, text: sw.dataset.text, accent: sw.dataset.accent, spine: sw.dataset.spine });
            CoverPreview.renderCover();
        });
    });

    elSizeGroup?.querySelectorAll('[data-size]').forEach(btn => {
        btn.addEventListener('click', () => {
            elSizeGroup.querySelectorAll('[data-size]').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            CoverPreview.setState({ fontSize: parseInt(btn.dataset.size) });
            CoverPreview.renderCover();
        });
    });

    elSpacing?.addEventListener('input', () => {
        const v = parseFloat(elSpacing.value).toFixed(1);
        if (elSpacingLbl) elSpacingLbl.textContent = SPACING_LABELS[v] || v;
        CoverPreview.setState({ lineHeight: parseFloat(v) });
        CoverPreview.renderCover();
    });

    btnCopy?.addEventListener('click', async () => {
        const dims = CoverPreview.getState().dims;
        if (!dims) return;
        const st = CoverPreview.getState();
        const text = [
            'KDP Cover Dimensions',
            '\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500',
            `Title:        ${st.title}`,
            `Author:       ${st.author}`,
            `Pages:        ${dims.pages}`,
            `Paper:        ${dims.paper}`,
            `Spine:        ${dims.spineInches}" / ${dims.spineMm}mm`,
            `Total width:  ${dims.totalWInches}" / ${dims.totalWMm}mm`,
            `Height:       ${dims.totalHInches}" / ${dims.totalHMm}mm`,
            `Barcode area: 2.000" \u00d7 1.200" (bottom-right of back cover)`,
            dims.warning || '',
        ].filter(Boolean).join('\n');
        try {
            await navigator.clipboard.writeText(text);
            if (btnCopyLbl) btnCopyLbl.textContent = 'Copied!';
            setTimeout(() => { if (btnCopyLbl) btnCopyLbl.textContent = 'Copy dimensions'; }, 2000);
        } catch (_) { if (btnCopyLbl) btnCopyLbl.textContent = 'Copy failed'; }
    });

    btnSpec?.addEventListener('click', () => {
        const st = CoverPreview.getState();
        window.location.href = `/Calculator/SpecSheet?pages=${st.pages}&paper=${encodeURIComponent(st.paper)}&title=${encodeURIComponent(st.title)}&author=${encodeURIComponent(st.author)}`;
    });

    document.getElementById('btnGenerateCover')?.addEventListener('click', () => {
        syncState();
        CoverPreview.generateApiCover();
    });

    syncState();
    CoverPreview.renderCover();
    if (elDesc && elDescCounter) elDescCounter.textContent = `${elDesc.value.length}/500`;
    if (elSpacing && elSpacingLbl) elSpacingLbl.textContent = SPACING_LABELS[parseFloat(elSpacing.value).toFixed(1)] || 'Normal';
});
