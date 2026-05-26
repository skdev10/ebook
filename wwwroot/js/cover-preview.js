'use strict';

const KDP = {
    WHITE_THICK:     0.002252,
    CREAM_THICK:     0.002347,
    COVER_BOARDS:    0.06,
    TRIM_W:          6.0,
    BLEED:           0.125,
    FULL_H_INCHES:   9.25,
    PREVIEW_PX:      560,
    PANEL_HEIGHT_PX: 310,
    MIN_SPINE_PX:    4,
    SPINE_TEXT_MIN:  0.25,
};

const state = {
    pages: 150, paper: 'white', title: 'My Book', author: 'Author Name',
    description: '', bg: '#1a3a5c', text: '#e8f4fd', accent: '#4fc3f7',
    spine: '#0d2137', fontSize: 13, lineHeight: 1.6, dims: null,
};

function calcDimensions(pages, paper) {
    const p = Math.min(828, Math.max(24, parseInt(pages) || 24));
    const thick = (paper === 'cream' || paper === 'Cream paper')
        ? KDP.CREAM_THICK : KDP.WHITE_THICK;
    const spineInches     = (p * thick) + KDP.COVER_BOARDS;
    const spineMm         = spineInches * 25.4;
    const fullWInches     = KDP.TRIM_W + spineInches + KDP.TRIM_W + (KDP.BLEED * 2);
    const fullWMm         = fullWInches * 25.4;
    const spineTextAllowed = spineInches >= KDP.SPINE_TEXT_MIN;
    let warning = null;
    if (spineInches < 0.0625) {
        warning = `Spine is only ${spineMm.toFixed(2)}mm — too narrow. Use image only.`;
    } else if (!spineTextAllowed) {
        warning = `Spine is ${spineMm.toFixed(2)}mm — text not recommended below ${(KDP.SPINE_TEXT_MIN * 25.4).toFixed(1)}mm.`;
    }
    return {
        pages: p, paper,
        spineInches:      parseFloat(spineInches.toFixed(3)),
        spineMm:          parseFloat(spineMm.toFixed(2)),
        totalWInches:     parseFloat(fullWInches.toFixed(3)),
        totalWMm:         parseFloat(fullWMm.toFixed(2)),
        totalHInches:     KDP.FULL_H_INCHES,
        totalHMm:         parseFloat((KDP.FULL_H_INCHES * 25.4).toFixed(2)),
        spineTextAllowed, warning,
    };
}

function buildLayout(dims) {
    const scale   = KDP.PREVIEW_PX / dims.totalWInches;
    const sidePx  = Math.round((KDP.TRIM_W + KDP.BLEED) * scale);
    const spinePx = Math.max(KDP.MIN_SPINE_PX, Math.round(dims.spineInches * scale));
    const totalPx = sidePx * 2 + spinePx;
    const overflow = totalPx - KDP.PREVIEW_PX;
    const frontPx  = sidePx - Math.ceil(overflow / 2);
    const backPx   = sidePx - Math.floor(overflow / 2);
    return { backPx, spinePx, frontPx };
}

function applyLayout(layout) {
    const panelBack  = document.getElementById('panelBack');
    const panelSpine = document.getElementById('panelSpine');
    const panelFront = document.getElementById('panelFront');
    const labelBack  = document.getElementById('labelBack');
    const labelSpine = document.getElementById('labelSpine');
    const labelFront = document.getElementById('labelFront');
    if (!panelBack || !panelSpine || !panelFront) return;
    [[panelBack, layout.backPx], [panelFront, layout.frontPx]].forEach(([el, px]) => {
        el.style.width = el.style.minWidth = el.style.maxWidth = px + 'px';
        el.style.height = KDP.PANEL_HEIGHT_PX + 'px';
    });
    const spx = layout.spinePx + 'px';
    panelSpine.style.width = panelSpine.style.minWidth = panelSpine.style.maxWidth = spx;
    panelSpine.style.height = KDP.PANEL_HEIGHT_PX + 'px';
    if (labelBack)  labelBack.style.width  = layout.backPx  + 'px';
    if (labelSpine) labelSpine.style.width = layout.spinePx + 'px';
    if (labelFront) labelFront.style.width = layout.frontPx + 'px';
}

function applyContent(dims) {
    const lsv = document.getElementById('labelSpineValue');
    if (lsv) lsv.textContent = `${dims.spineInches}" (${dims.spineMm}mm)`;
    const spineTextEl = document.getElementById('spineText');
    if (spineTextEl) {
        const layout = buildLayout(dims);
        if (dims.spineTextAllowed) {
            spineTextEl.classList.remove('spine-hidden');
            spineTextEl.textContent = dims.spineInches > 0.5
                ? (state.title + ' — ' + state.author) : state.title;
            spineTextEl.style.fontSize = Math.min(13, Math.max(7, layout.spinePx * 0.55)) + 'px';
        } else {
            spineTextEl.classList.add('spine-hidden');
        }
    }
    const t = document.getElementById('frontTitle');
    const a = document.getElementById('frontAuthor');
    const d = document.getElementById('backDescription');
    if (t) t.textContent = state.title  || 'My Book';
    if (a) a.textContent = state.author || 'Author Name';
    if (d) d.textContent = state.description || 'Your book description will appear here.';
}

function applyColors() {
    const r = document.documentElement;
    r.style.setProperty('--cover-bg',     state.bg);
    r.style.setProperty('--cover-text',   state.text);
    r.style.setProperty('--cover-accent', state.accent);
    r.style.setProperty('--cover-spine',  state.spine);
}

function applyTypography() {
    const r = document.documentElement;
    r.style.setProperty('--cover-font-size',   state.fontSize + 'px');
    r.style.setProperty('--cover-line-height', state.lineHeight);
}

function updateSummary(dims) {
    const set = (id, v) => { const e = document.getElementById(id); if (e) e.textContent = v; };
    set('sumSpine',      `${dims.spineInches}" (${dims.spineMm}mm)`);
    set('sumTotalWidth', `${dims.totalWInches}" (${dims.totalWMm}mm)`);
    set('sumPages',      dims.pages);
    set('sumPaper',      dims.paper);
    set('sumSpineText',  dims.spineTextAllowed ? 'Visible \u2713' : 'Too narrow \u2717');
    const row = document.getElementById('sumSpineRow');
    if (row) row.style.backgroundColor = dims.spineInches < 0.25 ? '#fff9c4' : '';
    const wb = document.getElementById('spineWarning');
    const wt = document.getElementById('spineWarningText');
    if (wb && wt) {
        if (dims.warning) { wt.textContent = dims.warning; wb.classList.remove('d-none'); }
        else { wb.classList.add('d-none'); }
    }
}

function renderCover() {
    const dims = calcDimensions(state.pages, state.paper);
    state.dims = dims;
    const layout = buildLayout(dims);
    applyColors();
    applyTypography();
    applyLayout(layout);
    applyContent(dims);
    updateSummary(dims);
}

async function generateApiCover() {
    if (!state.dims) return;
    const dims = state.dims;
    const payload = {
        title: state.title, author_name: state.author,
        category: '', cover_style: '', size: '1536x1024', quality: 'medium',
        Interior_trim_size: '6 x 9 in', page_count: state.pages,
    };
    showPanelLoading(true);
    try {
        const resp = await fetch('/BookDesign/GenerateSpineCover', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        if (!resp.ok) throw new Error(`Server error: ${resp.status}`);
        const data = await resp.json();
        const imgUrl = data.imageUrl || data.image_url;
        if (!imgUrl) throw new Error('No image URL in response');
        applyApiImageToPanels(imgUrl, dims);
    } catch (err) {
        showPanelError(err.message);
    } finally {
        showPanelLoading(false);
    }
}

function applyApiImageToPanels(imgUrl, dims) {
    const IMG_W = 1536;
    const srcScale   = IMG_W / dims.totalWInches;
    const bleedPx    = Math.round(KDP.BLEED * srcScale);
    const sidePx     = Math.round(KDP.TRIM_W * srcScale);
    const spineSrcPx = Math.round(dims.spineInches * srcScale);
    const backEndX   = bleedPx + sidePx;
    const spineEndX  = backEndX + spineSrcPx;
    const layout     = buildLayout(dims);
    const panels = [
        { id: 'panelBack',  srcX: 0,        srcW: backEndX,          dispW: layout.backPx  },
        { id: 'panelSpine', srcX: backEndX,  srcW: spineSrcPx,        dispW: layout.spinePx },
        { id: 'panelFront', srcX: spineEndX, srcW: IMG_W - spineEndX, dispW: layout.frontPx },
    ];
    panels.forEach(({ id, srcX, srcW }) => {
        const panel = document.getElementById(id);
        if (!panel) return;
        const old = panel.querySelector('.cover-image-overlay');
        if (old) old.remove();
        const wrap = document.createElement('div');
        wrap.className = 'cover-image-overlay';
        wrap.style.cssText = 'position:absolute;inset:0;overflow:hidden;z-index:2;';
        const img = document.createElement('img');
        img.src = imgUrl;
        img.alt = '';
        img.style.cssText = `position:absolute;height:100%;width:auto;top:0;left:-${(srcX/IMG_W*100)}%;min-width:${(IMG_W/srcW*100)}%;`;
        wrap.appendChild(img);
        panel.appendChild(wrap);
    });
}

function showPanelLoading(show) {
    ['panelBack','panelSpine','panelFront'].forEach(id => {
        const p = document.getElementById(id);
        if (!p) return;
        const ex = p.querySelector('.cover-image-loading');
        if (show && !ex) {
            const d = document.createElement('div');
            d.className = 'cover-image-loading';
            d.innerHTML = '<div class="spinner-border spinner-border-sm" role="status"></div> Generating\u2026';
            p.appendChild(d);
        } else if (!show && ex) { ex.remove(); }
    });
}

function showPanelError(msg) {
    const w = document.getElementById('spineWarning');
    const t = document.getElementById('spineWarningText');
    if (w && t) { t.textContent = 'Cover generation failed: ' + msg; w.classList.remove('d-none'); }
}

window.CoverPreview = { renderCover, generateApiCover, calcDimensions, getState: () => state, setState: p => Object.assign(state, p) };
