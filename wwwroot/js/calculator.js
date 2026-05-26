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

    const btnDownloadWrap = document.getElementById('btnDownloadWrap');
    const wrapStatus = document.getElementById('wrapStatus');
    const btnDownloadWrapLabel = document.getElementById('btnDownloadWrapLabel');

    btnDownloadWrap?.addEventListener('click', async () => {
        const st = CoverPreview.getState();
        const pages = parseInt(elPages?.value) || st.pages;
        const paper = elPaper?.value || st.paper;
        const title = elTitle?.value || st.title;
        const author = elAuthor?.value || st.author;

        if (pages < 24 || pages > 828) {
            if (wrapStatus) wrapStatus.textContent = 'Page count must be 24–828.';
            return;
        }

        btnDownloadWrap.disabled = true;
        if (btnDownloadWrapLabel) btnDownloadWrapLabel.textContent = 'Building cover…';
        if (wrapStatus) wrapStatus.textContent = '';

        try {
            const overlayImg = document.querySelector('#panelFront .cover-image-overlay img');
            const frontUrl = overlayImg?.src || null;

            const PAPER_MULT = { 'White paper': 0.002252, 'Cream paper': 0.0025, 'Color paper': 0.002347 };
            const mult = PAPER_MULT[paper] || 0.002252;
            const TRIM_W = 6.0, TRIM_H = 9.0, BLEED = 0.125, DPI = 300;
            const spineIn = pages * mult;
            const fullW = TRIM_W * 2 + spineIn + BLEED * 2;
            const fullH = TRIM_H + BLEED * 2;
            const px = (inch) => Math.round(inch * DPI);

            const canvasW = px(fullW);
            const canvasH = px(fullH);
            const sidePanel = px(BLEED + TRIM_W);
            const spinePx = px(spineIn);

            const canvas = document.createElement('canvas');
            canvas.width = canvasW;
            canvas.height = canvasH;
            const ctx = canvas.getContext('2d');

            const bgColor = st.bg || '#1a3a5c';
            const textColor = st.text || '#e8f4fd';
            const accentColor = st.accent || '#4fc3f7';
            const spineColor = st.spine || '#0d2137';

            if (frontUrl) {
                const result = await window.CoverKdpExport.composePrintWrapFromParts(frontUrl, null, null, {
                    pageCount: pages,
                    dpi: DPI,
                    trimKey: '6x9',
                    paperType: paper,
                    title: title
                });
                const link = document.createElement('a');
                link.download = `${(title || 'book').replace(/[^\w\s-]/g, '').replace(/\s+/g, '-')}-full-wraparound-${pages}p.png`;
                link.href = result.dataUrl;
                document.body.appendChild(link);
                link.click();
                link.remove();
                if (wrapStatus) wrapStatus.textContent =
                    `Downloaded: ${result.widthPx} × ${result.heightPx} px @ 300 DPI — spine ${result.spineInches.toFixed(3)}" for ${result.pageCount} pages`;
            } else {
                ctx.fillStyle = bgColor;
                ctx.fillRect(0, 0, sidePanel, canvasH);

                const descText = elDesc?.value || 'Your book description will appear here.';
                const pad = Math.max(40, sidePanel * 0.06);
                ctx.fillStyle = textColor;
                ctx.font = `bold ${Math.max(28, sidePanel * 0.04)}px Georgia, serif`;
                ctx.fillText(title.length > 42 ? title.slice(0, 39) + '…' : title, pad, pad + 50);
                ctx.fillStyle = accentColor;
                ctx.font = `${Math.max(20, sidePanel * 0.028)}px Georgia, serif`;
                ctx.fillText(author, pad, pad + 90);
                ctx.fillStyle = textColor;
                ctx.font = `${Math.max(16, sidePanel * 0.022)}px Georgia, serif`;
                wrapTextOnCanvas(ctx, descText, pad, pad + 140, sidePanel - pad * 2, Math.max(22, sidePanel * 0.03));

                const bw = 150, bh = 60;
                const bx = sidePanel / 2 - bw / 2;
                const by = canvasH - pad - bh - 20;
                ctx.fillStyle = '#fff';
                ctx.fillRect(bx, by, bw, bh);
                ctx.strokeStyle = '#666';
                ctx.lineWidth = 2;
                ctx.strokeRect(bx, by, bw, bh);
                ctx.fillStyle = '#666';
                ctx.font = '14px monospace';
                ctx.fillText('ISBN / barcode', bx + 14, by + bh / 2 + 5);

                ctx.fillStyle = spineColor;
                ctx.fillRect(sidePanel, 0, spinePx, canvasH);

                ctx.fillStyle = bgColor;
                ctx.fillRect(sidePanel + spinePx, 0, canvasW - sidePanel - spinePx, canvasH);
                const frontX = sidePanel + spinePx;
                const frontW = canvasW - frontX;
                ctx.fillStyle = textColor;
                ctx.font = `bold ${Math.max(48, frontW * 0.05)}px Georgia, serif`;
                const titleLines = wrapTextOnCanvas(ctx, title, frontX + pad, canvasH * 0.35, frontW - pad * 2, Math.max(56, frontW * 0.06));
                ctx.fillStyle = accentColor;
                ctx.font = `${Math.max(30, frontW * 0.032)}px Georgia, serif`;
                ctx.fillText(author, frontX + pad, canvasH * 0.35 + titleLines * Math.max(56, frontW * 0.06) + 30);

                canvas.toBlob((blob) => {
                    if (!blob) return;
                    const url = URL.createObjectURL(blob);
                    const link = document.createElement('a');
                    link.download = `${(title || 'book').replace(/[^\w\s-]/g, '').replace(/\s+/g, '-')}-full-wraparound-${pages}p.png`;
                    link.href = url;
                    document.body.appendChild(link);
                    link.click();
                    link.remove();
                    URL.revokeObjectURL(url);
                }, 'image/png');

                if (wrapStatus) wrapStatus.textContent =
                    `Downloaded: ${canvasW} × ${canvasH} px @ 300 DPI — spine ${spineIn.toFixed(3)}" (${(spineIn * 25.4).toFixed(2)}mm) for ${pages} pages`;
            }
        } catch (err) {
            if (wrapStatus) wrapStatus.textContent = 'Error: ' + (err.message || 'Download failed');
        } finally {
            btnDownloadWrap.disabled = false;
            if (btnDownloadWrapLabel) btnDownloadWrapLabel.textContent = 'Download Full Wraparound (300 DPI)';
        }
    });

    function wrapTextOnCanvas(ctx, text, x, y, maxWidth, lineHeight) {
        const words = text.split(/\s+/);
        let line = '', cy = y, count = 0;
        for (let n = 0; n < words.length; n++) {
            const test = line ? line + ' ' + words[n] : words[n];
            if (ctx.measureText(test).width > maxWidth && line) {
                ctx.fillText(line, x, cy);
                line = words[n];
                cy += lineHeight;
                count++;
                if (count > 15) return count;
            } else {
                line = test;
            }
        }
        if (line) { ctx.fillText(line, x, cy); count++; }
        return count;
    }

    syncState();
    CoverPreview.renderCover();
    if (elDesc && elDescCounter) elDescCounter.textContent = `${elDesc.value.length}/500`;
    if (elSpacing && elSpacingLbl) elSpacingLbl.textContent = SPACING_LABELS[parseFloat(elSpacing.value).toFixed(1)] || 'Normal';
});
