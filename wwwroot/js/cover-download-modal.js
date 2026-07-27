/**
 * In-app KDP-style calculator + download flow (no external KDP redirects).
 */
(function (global) {
    'use strict';

    var chev = '<svg class="dbk-chev" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M19 9l-7 7-7-7"/></svg>';
    var infoIcon = '<svg class="dbk-info-ic" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path stroke-linecap="round" d="M12 16v-4M12 8h.01"/></svg>';

    function paperbackPageLimits() {
        var s = global.__kdpSpecs;
        return {
            min: (s && s.paperback && s.paperback.min) || 24,
            max: (s && s.paperback && s.paperback.max) || 828
        };
    }

    /** Sync min/max on page-count inputs after DOM insert (HTML built with paperbackPageLimits). */
    function applyPageInputLimits(input) {
        if (!input) return;
        var lim = paperbackPageLimits();
        input.min = String(lim.min);
        input.max = String(lim.max);
    }
    var labels = {
        1: 'Full Cover', 2: 'Front Cover', 3: 'Margin', 4: 'Wrap', 5: 'Hinge',
        6: 'Spine', 7: 'Spine Safe Area', 8: 'Spine Margin', 9: 'Barcode Margin'
    };

    var STORAGE_PREFIX = 'dbk_calc_v1_';

    function calcStorageKey(bookId) {
        return STORAGE_PREFIX + (bookId || 0);
    }

    function saveCalcToStorage(bookId, calc) {
        try {
            sessionStorage.setItem(calcStorageKey(bookId), JSON.stringify({ calc: calc, savedAt: Date.now() }));
        } catch (e) { /* ignore */ }
    }

    function loadCalcFromStorage(bookId) {
        try {
            var raw = sessionStorage.getItem(calcStorageKey(bookId));
            if (!raw) return null;
            var o = JSON.parse(raw);
            return o && o.calc ? o.calc : null;
        } catch (e) {
            return null;
        }
    }

    function fieldRow(prefix, label, idSuffix, opts, def) {
        var id = prefix + '_' + idSuffix;
        var o = opts.map(function (v) {
            return '<option value="' + escAttr(v) + '"' + (v === def ? ' selected' : '') + '>' + escHtml(v) + '</option>';
        }).join('');
        return '<div class="dbk-field"><label class="dbk-lbl" for="' + id + '">' + escHtml(label) + '</label>' +
            '<div class="dbk-select-wrap"><select id="' + id + '" class="dbk-sel">' + o + '</select>' + chev + '</div></div>';
    }

    function fieldRowTrim(prefix) {
        var id = prefix + '_trim';
        return '<div class="dbk-field"><label class="dbk-lbl" for="' + id + '">Interior trim size</label>' +
            '<div class="dbk-select-wrap"><select id="' + id + '" class="dbk-sel">' +
            '<option value="152.4 x 228.6">152.4 x 228.6 mm (6 x 9 in)</option>' +
            '<option value="127.0 x 203.2">127.0 x 203.2 mm (5 x 8 in)</option>' +
            '<option value="129.5 x 198.1">129.5 x 198.1 mm (5.06 x 7.81 in)</option>' +
            '</select>' + chev + '</div></div>';
    }

    function tableRow(prefix, i, desc) {
        return '<tr class="dbk-tr"><td class="dbk-td">' + i + '</td><td class="dbk-td dbk-td-link"><span>' + escHtml(desc) + chev + '</span></td>' +
            '<td id="' + prefix + '_t' + i + 'w" class="dbk-td dbk-td-num"></td><td id="' + prefix + '_t' + i + 'h" class="dbk-td dbk-td-num"></td></tr>';
    }

    function tablesHtml(prefix) {
        var head = '<thead><tr class="dbk-tr-h"><th class="dbk-th-n">#</th><th class="dbk-th-d">Description</th><th class="dbk-th-w">Width <span class="dbk-unit-label">(mm)</span></th><th class="dbk-th-h">Height <span class="dbk-unit-label">(mm)</span></th></tr></thead>';
        var rows1 = [1, 2, 3, 4, 5].map(function (i) { return tableRow(prefix, i, labels[i]); }).join('');
        var rows2 = [6, 7, 8, 9].map(function (i) { return tableRow(prefix, i, labels[i]); }).join('');
        return '<div class="dbk-tables">' +
            '<div class="dbk-twrap"><table class="dbk-table">' + head + '<tbody>' + rows1 + '</tbody></table></div>' +
            '<div class="dbk-twrap"><table class="dbk-table">' + head + '<tbody>' + rows2 + '</tbody></table></div>' +
            '</div>';
    }

    function marker(id, n, cls) {
        return '<div id="' + id + '" class="' + cls + '">' + n + '</div>';
    }

    function previewHtml(prefix) {
        return '<div class="dbk-preview-box">' +
            '<div class="dbk-preview-book" id="' + prefix + '_previewBook">' +
            '<div class="dbk-pv-back" id="' + prefix + '_leftCover">' +
            '<div class="dbk-kdp-logo" id="' + prefix + '_kdpLogo"><span>eBook AI<br><strong>print<br>preview</strong></span></div>' +
            '<div class="dbk-barcode" id="' + prefix + '_barcodeBox"></div></div>' +
            '<div class="dbk-pv-spine" id="' + prefix + '_spineBar"></div>' +
            '<div class="dbk-pv-front" id="' + prefix + '_rightCover"></div>' +
            marker(prefix + '_m1', '1', 'dbk-mk dbk-mk-l') +
            marker(prefix + '_m2', '2', 'dbk-mk dbk-mk-2') +
            marker(prefix + '_m3', '3', 'dbk-mk dbk-mk-3') +
            marker(prefix + '_m4', '4', 'dbk-mk dbk-mk-4') +
            marker(prefix + '_m5', '5', 'dbk-mk dbk-mk-5') +
            marker(prefix + '_m6', '6', 'dbk-mk dbk-mk-6') +
            marker(prefix + '_m7', '7', 'dbk-mk dbk-mk-7') +
            marker(prefix + '_m8', '8', 'dbk-mk dbk-mk-8') +
            marker(prefix + '_m9', '9', 'dbk-mk dbk-mk-9') +
            '</div></div>';
    }

    /** Full calculator + tables + preview (prefix e.g. dbk or dbc). */
    function buildCalculatorBlock(prefix, initialPages) {
        var p = initialPages || 98;
        var lim = paperbackPageLimits();
        return (
            '<div class="dbk-amz-shell">' +
            '<div class="dbk-amz-head">' +
            '<h2 class="dbk-amz-h1">Print Cover Calculator and Templates</h2>' +
            '<p class="dbk-amz-lead">Enter your book details and tap <strong>Calculate dimensions</strong>. Results and downloads stay inside this app — nothing opens Amazon in a new tab. Hardcover adds wrap and hinge; paperback uses bleed on the full spread.</p>' +
            '</div>' +
            '<div class="dbk-amz-grid">' +
            '<div class="dbk-amz-sidebar">' +
            '<h3 class="dbk-amz-h2">Enter Your Book Information</h3>' +
            '<form id="' + prefix + '_CalcForm" class="dbk-form">' +
            fieldRow(prefix, 'Binding type', 'binding', ['Hardcover', 'Paperback'], 'Paperback') +
            fieldRow(prefix, 'Interior type', 'interior', ['Premium color', 'Standard color', 'Black & white'], 'Black & white') +
            fieldRow(prefix, 'Reading Direction', 'direction', ['Right to Left', 'Left to Right'], 'Right to Left') +
            fieldRow(prefix, 'Measurement units', 'units', ['Millimeters', 'Inches'], 'Millimeters') +
            fieldRowTrim(prefix) +
            '<div class="dbk-field"><label class="dbk-lbl" for="' + prefix + '_pages">Page count</label>' +
            '<p class="dbk-help">Number of pages at your formatted trim size.</p>' +
            '<input type="number" id="' + prefix + '_pages" min="' + lim.min + '" max="' + lim.max + '" value="' + p + '" class="dbk-inp"/></div>' +
            '<div class="dbk-form-actions">' +
            '<button type="button" class="dbk-btn-calc" id="' + prefix + '_calcBtn">Calculate dimensions</button>' +
            '<button type="button" class="dbk-btn-template" id="' + prefix + '_templateBtn">Templates &amp; guide</button>' +
            '<button type="button" class="dbk-btn-reset" id="' + prefix + '_resetBtn">Reset book information</button>' +
            '</div></form></div>' +
            '<div class="dbk-amz-main">' +
            tablesHtml(prefix) +
            previewHtml(prefix) +
            '<div class="dbk-ref-note">' + infoIcon + '<span>Image for reference only — your exported PNG uses calculated sizes at 300 DPI.</span></div>' +
            '</div></div></div>'
        );
    }

    function buildEbookGuidelinesBodyHtml() {
        return (
            '<h3 class="dbk-guide-h">eBook cover — in-app checklist</h3>' +
            '<div class="dbk-guide-grid">' +
            '<div class="dbk-guide-card dbk-guide-card-preview">' +
            '<h4>Recommended pixels</h4>' +
            '<div class="dbk-guide-cover-mock" aria-hidden="true"><span class="dbk-guide-mock-label">1600×2560</span></div>' +
            '<p class="dbk-guide-big">1600 × 2560</p>' +
            '<p class="dbk-guide-small">Short side ≥ 1000 px; tallest side up to 2560 px for best Kindle quality.</p>' +
            '</div>' +
            '<div class="dbk-guide-card">' +
            '<h4>Aspect ratio</h4>' +
            '<p class="dbk-guide-big">~1 : 1.6</p>' +
            '<p class="dbk-guide-small">Height : width. Avoid ultra-wide banners; title and art should read clearly at thumbnail size.</p>' +
            '</div>' +
            '<div class="dbk-guide-card">' +
            '<h4>Format</h4>' +
            '<p class="dbk-guide-big">JPEG / TIFF</p>' +
            '<p class="dbk-guide-small">This app embeds a high-quality JPEG inside EPUB 3 as the official cover-image.</p>' +
            '</div>' +
            '<div class="dbk-guide-card">' +
            '<h4>No text cut-off</h4>' +
            '<p class="dbk-guide-big">Safe margin</p>' +
            '<p class="dbk-guide-small">Keep title and author inside the central ~80% — devices crop slightly at edges.</p>' +
            '</div>' +
            '</div>' +
            '<p class="dbk-guide-foot">Sab export yahi app se — cover design ke baad <strong>Download book</strong> → EPUB.</p>'
        );
    }

    /** Download modal: flow + EPUB/cover/PDF — no calculator (use separate modal). */
    function buildDownloadFlowHtml(bookId) {
        return (
            '<div class="dbk-root dbk-download-flow">' +
            '<div class="dbk-flow-hero">' +
            '<div class="dbk-flow-title-row"><h2 class="dbk-flow-h2">Download book</h2><span class="dbk-flow-tag">In-app</span></div>' +
            '<ol class="dbk-flow-steps">' +
            '<li><span class="dbk-step-num">1</span><span>Print cover calculator se dimensions nikaalein (same window — alag modal).</span></li>' +
            '<li><span class="dbk-step-num">2</span><span>EPUB aur print cover files yahin se download karein.</span></li>' +
            '</ol></div>' +
            '<div class="dbk-flow-actions">' +
            '<button type="button" class="dbk-flow-btn dbk-flow-publish" id="dbkBtnPublishing"><span class="dbk-flow-ic">🚀</span> Publishing</button>' +
            '<button type="button" class="dbk-flow-btn dbk-flow-calc" id="dbkBtnOpenCalc"><span class="dbk-flow-ic">📐</span> Print cover calculator</button>' +
            '<button type="button" class="dbk-flow-btn dbk-flow-guide" id="dbkBtnGuidelines" aria-expanded="false"><span class="dbk-flow-ic">📖</span> eBook cover guidelines</button>' +
            '</div>' +
            '<div id="dbkGuidelinesPanel" class="dbk-guidelines-inline dbk-guide-modal" hidden>' + buildEbookGuidelinesBodyHtml() + '</div>' +
            '<p class="dbk-calc-status" id="dbkCalcStatus">Print dimensions: <strong id="dbkCalcStatusText">not calculated yet</strong> for this book.</p>' +
            '<div class="dbk-top-split">' +
            '<div class="dbk-card dbk-card-digital">' +
            '<h3 class="dbk-card-title">eBook — EPUB</h3>' +
            '<p class="dbk-card-p">Kindle-friendly JPEG <span id="dbkEpubPx"></span> px inside EPUB 3. EPUB tab unlocks after you <strong>Calculate</strong> in the print calculator.</p>' +
            '<button type="button" class="dbk-btn-amz-yellow dbk-btn-disabled" id="dbkDownloadEpub" disabled>Download EPUB</button>' +
            '<p class="dbk-gate-msg" id="dbkEpubGateMsg">Pehle <strong>Print cover calculator</strong> khol kar <strong>Calculate dimensions</strong> dabayein.</p>' +
            '<p class="dbk-filename">File: <span id="dbkEpubName"></span></p>' +
            '</div>' +
            '<div class="dbk-card dbk-card-cover">' +
            '<h3 class="dbk-card-title">Cover design (print)</h3>' +
            '<p class="dbk-card-p">Back · spine · front — alag PNG, full wrap, ya ZIP (manifest + images). Calculator ke baad hi export sahi size par hoga.</p>' +
            '<div class="dbk-comp-wrap"><img id="dbkCompositeThumb" alt="Full wrap preview" class="dbk-comp-img"/></div>' +
            '<div class="dbk-thumb-pair">' +
            '<div><span class="dbk-mini-label">Front</span><img id="dbkImgFront" alt="" class="dbk-mini-img"/></div>' +
            '<div><span class="dbk-mini-label">Back</span><img id="dbkImgBack" alt="" class="dbk-mini-img"/></div>' +
            '</div>' +
            '<div class="dbk-cover-btns">' +
            '<button type="button" class="dbk-btn-outline" id="dbkDlBack">Back PNG</button>' +
            '<button type="button" class="dbk-btn-outline" id="dbkDlSpine">Spine PNG</button>' +
            '<button type="button" class="dbk-btn-outline" id="dbkDlFront">Front PNG</button>' +
            '<button type="button" class="dbk-btn-amz-outline" id="dbkDlWrap">Full wrap PNG</button>' +
            '<button type="button" class="dbk-btn-amz-solid" id="dbkDlZip">Cover package (ZIP)</button>' +
            '</div>' +
            '<p class="dbk-hint" id="dbkPngSpecs">—</p>' +
            '</div></div>' +
            '<div class="dbk-pdf-bar">' +
            '<button type="button" class="dbk-btn-pdf-wide" id="dbkDownloadFullPdf">Download full book PDF…</button>' +
            '</div>' +
            '</div>'
        );
    }

    function buildEbookGuidelinesHtml() {
        return '<div class="dbk-guide-modal">' + buildEbookGuidelinesBodyHtml() + '</div>';
    }

    function escHtml(s) {
        return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }
    function escAttr(s) {
        return escHtml(s).replace(/"/g, '&quot;');
    }

    function readCalcForm(root, prefix) {
        function v(suffix) {
            var el = root.querySelector('#' + prefix + '_' + suffix);
            return el ? el.value : '';
        }
        return {
            binding: v('binding'),
            interior: v('interior'),
            direction: v('direction'),
            units: v('units'),
            trim: v('trim'),
            pages: v('pages')
        };
    }

    function updateCalculatorDom(root, computeFn, prefix) {
        prefix = prefix || 'dbk';
        var f = readCalcForm(root, prefix);
        var c = computeFn(f.binding, f.interior, f.paper, f.trim, f.pages);
        var isInch = f.units === 'Inches';
        var factor = isInch ? 1 / 25.4 : 1;
        var decimals = isInch ? 3 : 2;

        var i;
        for (i = 1; i <= 9; i++) {
            var wEl = root.querySelector('#' + prefix + '_t' + i + 'w');
            var hEl = root.querySelector('#' + prefix + '_t' + i + 'h');
            if (!wEl || !hEl) continue;
            var vw = c.data[i].w * factor;
            var vh = c.data[i].h * factor;
            if (vw === 0 && vh === 0) {
                wEl.textContent = '—';
                hEl.textContent = '—';
            } else {
                wEl.textContent = vw.toFixed(decimals);
                hEl.textContent = vh.toFixed(decimals);
            }
        }
        root.querySelectorAll('.dbk-unit-label').forEach(function (el) {
            el.textContent = isInch ? '(in)' : '(mm)';
        });

        var leftCover = root.querySelector('#' + prefix + '_leftCover');
        var rightCover = root.querySelector('#' + prefix + '_rightCover');
        var spineBar = root.querySelector('#' + prefix + '_spineBar');
        var kdpLogo = root.querySelector('#' + prefix + '_kdpLogo');
        var barcodeBox = root.querySelector('#' + prefix + '_barcodeBox');
        var m9 = root.querySelector('#' + prefix + '_m9');
        var m4 = root.querySelector('#' + prefix + '_m4');
        var m5 = root.querySelector('#' + prefix + '_m5');

        var fullW = c.data[1].w;
        var spineW = c.data[6].w;
        var side = (fullW - spineW) / 2;
        var rg = (side / fullW) * 1000;
        var sg = (spineW / fullW) * 1000;
        if (leftCover) leftCover.style.flex = rg + ' ' + rg + ' 0';
        if (rightCover) rightCover.style.flex = rg + ' ' + rg + ' 0';
        if (spineBar) spineBar.style.flex = sg + ' ' + sg + ' 0';

        if (f.direction === 'Left to Right') {
            if (kdpLogo && rightCover && barcodeBox) {
                rightCover.appendChild(kdpLogo);
                rightCover.appendChild(barcodeBox);
            }
            if (rightCover) rightCover.classList.add('dbk-pv-flex');
            if (leftCover) leftCover.classList.remove('dbk-pv-flex');
            if (barcodeBox) {
                barcodeBox.classList.add('dbk-barcode-ltr');
                barcodeBox.classList.remove('dbk-barcode-rtl');
            }
            if (m9) { m9.classList.add('dbk-m9-ltr'); m9.classList.remove('dbk-m9-rtl'); }
        } else {
            if (kdpLogo && leftCover && barcodeBox) {
                leftCover.appendChild(kdpLogo);
                leftCover.appendChild(barcodeBox);
            }
            if (leftCover) leftCover.classList.add('dbk-pv-flex');
            if (rightCover) rightCover.classList.remove('dbk-pv-flex');
            if (barcodeBox) {
                barcodeBox.classList.add('dbk-barcode-rtl');
                barcodeBox.classList.remove('dbk-barcode-ltr');
            }
            if (m9) { m9.classList.add('dbk-m9-rtl'); m9.classList.remove('dbk-m9-ltr'); }
        }

        if (m4) m4.style.display = f.binding === 'Hardcover' ? 'flex' : 'none';
        if (m5) m5.style.display = f.binding === 'Hardcover' ? 'flex' : 'none';

        return c;
    }

    function injectDbkStylesOnce() {
        if (document.getElementById('dbk-modal-styles')) return;
        var st = document.createElement('style');
        st.id = 'dbk-modal-styles';
        st.textContent =
            '@keyframes dbkPopIn{from{opacity:0;transform:scale(0.96) translateY(10px)}to{opacity:1;transform:scale(1) translateY(0)}}' +
            '.dbk-glass-backdrop.swal2-container{background:rgba(15,23,42,0.52)!important;backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px);}' +
            '.dbk-glass-popup.swal2-popup{background:rgba(255,255,255,0.9)!important;backdrop-filter:blur(20px);-webkit-backdrop-filter:blur(20px);border-radius:22px!important;border:1px solid rgba(255,255,255,0.55)!important;box-shadow:0 25px 50px -12px rgba(0,0,0,0.35)!important;padding:0!important;animation:dbkPopIn .38s cubic-bezier(0.16,1,0.3,1);}' +
            '.dbk-calc-standalone-root{padding:0;max-height:min(88vh,900px);overflow:auto;}' +
            '.dbk-download-popup{border-radius:16px!important;padding:0!important;max-width:min(94vw,1100px)!important;width:92%!important;}' +
            '.dbk-download-html{margin:0!important;padding:0!important;max-height:min(88vh,860px);overflow:auto;background:linear-gradient(180deg,#f8fafc 0%,#f1f5f9 100%);}' +
            '.dbk-download-flow{padding:20px 22px 24px;font-family:Inter,Segoe UI,system-ui,sans-serif;color:#111827;text-align:left;}' +
            '.dbk-flow-hero{margin-bottom:18px;padding:16px 18px;border-radius:14px;background:rgba(255,255,255,0.75);border:1px solid #e2e8f0;box-shadow:0 4px 20px -12px rgba(15,23,42,0.12);}' +
            '.dbk-flow-title-row{display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:10px;}' +
            '.dbk-flow-h2{margin:0;font-size:1.35rem;font-weight:700;color:#0f172a;}' +
            '.dbk-flow-tag{font-size:10px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;background:#ede9fe;color:#5b21b6;padding:4px 10px;border-radius:999px;}' +
            '.dbk-flow-steps{margin:0;padding-left:1.2rem;font-size:13px;color:#475569;line-height:1.55;}' +
            '.dbk-flow-steps li{margin-bottom:6px;}' +
            '.dbk-step-num{display:inline-flex;align-items:center;justify-content:center;width:22px;height:22px;border-radius:50%;background:#7c3aed;color:#fff;font-size:11px;font-weight:700;margin-right:8px;}' +
            '.dbk-flow-actions{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:14px;}' +
            '.dbk-flow-btn{display:inline-flex;align-items:center;gap:8px;padding:10px 16px;border-radius:12px;border:1px solid #e2e8f0;background:#fff;font-size:13px;font-weight:600;color:#334155;cursor:pointer;box-shadow:0 2px 8px -4px rgba(15,23,42,0.15);transition:transform .15s,box-shadow .15s;}' +
            '.dbk-flow-btn:hover{transform:translateY(-1px);box-shadow:0 8px 20px -8px rgba(91,33,182,0.25);border-color:#c4b5fd;}' +
            '.dbk-flow-publish{color:#5b21b6;}' +
            '.dbk-flow-calc{color:#0369a1;border-color:#bae6fd;background:linear-gradient(180deg,#fff,#f0f9ff);}' +
            '.dbk-flow-guide{color:#b45309;border-color:#fde68a;background:linear-gradient(180deg,#fff,#fffbeb);}' +
            '.dbk-flow-ic{font-size:16px;line-height:1;}' +
            '.dbk-calc-status{font-size:13px;color:#64748b;margin:0 0 16px;padding:10px 12px;background:#fff;border-radius:10px;border:1px solid #e2e8f0;}' +
            '.dbk-calc-status strong{color:#0f172a;}' +
            '.dbk-btn-disabled{opacity:0.55;cursor:not-allowed!important;filter:grayscale(0.2);}' +
            '.dbk-gate-msg{font-size:12px;color:#b45309;margin:8px 0 0;line-height:1.45;}' +
            '.dbk-guidelines-inline{margin-bottom:14px;padding:16px 18px;border-radius:14px;border:1px solid #e9d5ff;background:linear-gradient(145deg,rgba(250,245,255,0.95),#fff);box-shadow:0 8px 28px -16px rgba(91,33,182,0.25);}' +
            '.dbk-guide-cover-mock{aspect-ratio:1600/2560;max-height:140px;width:100%;max-width:88px;margin:0 auto 10px;border-radius:8px;background:linear-gradient(145deg,#4c1d95,#7c3aed 40%,#c4b5fd);box-shadow:0 6px 20px -8px rgba(76,29,149,0.5);position:relative;display:flex;align-items:center;justify-content:center;}' +
            '.dbk-guide-mock-label{font-size:9px;font-weight:700;color:rgba(255,255,255,0.9);text-shadow:0 1px 2px rgba(0,0,0,0.25);}' +
            '.dbk-guide-card-preview .dbk-guide-big{margin-top:4px;}' +
            '.dbk-guide-modal{text-align:left;padding:4px 8px 8px;max-width:640px;font-family:Inter,system-ui,sans-serif;}' +
            '.dbk-guidelines-inline.dbk-guide-modal{padding:16px 18px;max-width:none;}' +
            '.dbk-guide-h{margin:0 0 14px;font-size:1.15rem;color:#0f172a;}' +
            '.dbk-guide-grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;}' +
            '@media(max-width:520px){.dbk-guide-grid{grid-template-columns:1fr;}}' +
            '.dbk-guide-card{background:linear-gradient(145deg,#faf5ff,#fff);border:1px solid #e9d5ff;border-radius:12px;padding:14px;}' +
            '.dbk-guide-card h4{margin:0 0 6px;font-size:11px;text-transform:uppercase;letter-spacing:0.06em;color:#6b21a8;}' +
            '.dbk-guide-big{font-size:1.35rem;font-weight:800;color:#0f172a;margin:0 0 6px;}' +
            '.dbk-guide-small{margin:0;font-size:12px;color:#64748b;line-height:1.45;}' +
            '.dbk-guide-foot{margin:14px 0 0;font-size:12px;color:#475569;}' +
            '.dbk-top-split{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:16px;}' +
            '@media(max-width:900px){.dbk-top-split{grid-template-columns:1fr;}}' +
            '.dbk-card{background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:18px;box-shadow:0 4px 18px -10px rgba(15,23,42,0.12);}' +
            '.dbk-card-title{margin:0 0 8px;font-size:16px;font-weight:600;color:#111;}' +
            '.dbk-card-p{margin:0 0 12px;font-size:13px;color:#6b7280;line-height:1.5;}' +
            '.dbk-btn-amz-yellow{width:100%;padding:10px 16px;border:none;border-radius:9999px;background:#ffd814;color:#111827;font-weight:600;font-size:15px;cursor:pointer;margin-bottom:4px;box-shadow:0 1px 2px rgba(0,0,0,.08);}' +
            '.dbk-btn-amz-yellow:hover:not(:disabled){background:#f7ca00;}' +
            '.dbk-filename{font-size:11px;color:#9ca3af;margin:8px 0 0;}' +
            '.dbk-comp-wrap{background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:8px;margin-bottom:10px;max-height:140px;overflow:hidden;}' +
            '.dbk-comp-img{width:100%;height:auto;max-height:120px;object-fit:contain;display:block;}' +
            '.dbk-thumb-pair{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:12px;}' +
            '.dbk-mini-label{font-size:10px;font-weight:600;text-transform:uppercase;color:#6b7280;display:block;margin-bottom:4px;}' +
            '.dbk-mini-img{width:100%;aspect-ratio:2/3;object-fit:cover;border-radius:6px;background:#1e1b4b;}' +
            '.dbk-cover-btns{display:flex;flex-wrap:wrap;gap:8px;}' +
            '.dbk-btn-outline{padding:8px 12px;border:1px solid #d1d5db;border-radius:8px;background:#fff;font-size:12px;font-weight:600;color:#374151;cursor:pointer;}' +
            '.dbk-btn-outline:hover{background:#f9fafb;}' +
            '.dbk-btn-amz-outline{padding:8px 14px;border:1px solid #d1d5db;border-radius:9999px;background:#fff;font-size:13px;font-weight:600;cursor:pointer;}' +
            '.dbk-btn-amz-solid{padding:8px 14px;border:none;border-radius:9999px;background:#232f3e;color:#fff;font-size:13px;font-weight:600;cursor:pointer;}' +
            '.dbk-btn-amz-solid:hover{background:#37475a;}' +
            '.dbk-hint{font-size:12px;color:#6b7280;margin:10px 0 0;}' +
            '.dbk-pdf-bar{margin-bottom:8px;}' +
            '.dbk-btn-pdf-wide{width:100%;padding:12px;border-radius:12px;border:2px solid #c4b5fd;background:#faf5ff;color:#5b21b6;font-weight:700;font-size:14px;cursor:pointer;}' +
            '.dbk-btn-pdf-wide:hover{background:#ede9fe;}' +
            '.dbk-amz-shell{background:#fff;border:1px solid #e5e7eb;border-radius:12px;overflow:hidden;box-shadow:0 4px 24px -12px rgba(15,23,42,0.15);}' +
            '.dbk-amz-head{padding:18px 22px;border-bottom:1px solid #e5e7eb;background:linear-gradient(180deg,#fff,#fafafa);}' +
            '.dbk-amz-h1{margin:0 0 8px;font-size:1.25rem;font-weight:700;color:#111827;}' +
            '.dbk-amz-lead{margin:0;font-size:14px;color:#4b5563;line-height:1.55;}' +
            '.dbk-amz-grid{display:grid;grid-template-columns:1fr;}' +
            '@media(min-width:1024px){.dbk-amz-grid{grid-template-columns:minmax(260px,3fr) minmax(0,9fr);}}' +
            '.dbk-amz-sidebar{background:#fafafa;border-bottom:1px solid #e5e7eb;padding:18px 22px;}' +
            '@media(min-width:1024px){.dbk-amz-sidebar{border-bottom:none;border-right:1px solid #e5e7eb;}}' +
            '.dbk-amz-h2{font-size:16px;font-weight:600;margin:0 0 16px;color:#111;}' +
            '.dbk-form{display:flex;flex-direction:column;gap:14px;}' +
            '.dbk-field{display:flex;flex-direction:column;gap:6px;}' +
            '.dbk-lbl{font-size:13px;font-weight:500;color:#374151;}' +
            '.dbk-help{font-size:12px;color:#6b7280;margin:0;line-height:1.4;}' +
            '.dbk-select-wrap{position:relative;}' +
            '.dbk-sel,.dbk-inp{width:100%;padding:8px 32px 8px 10px;border:1px solid #d1d5db;border-radius:6px;font-size:14px;background:#fff;color:#111;box-sizing:border-box;}' +
            '.dbk-select-wrap .dbk-chev{position:absolute;right:10px;top:50%;transform:translateY(-50%);pointer-events:none;color:#6b7280;width:16px;height:16px;}' +
            '.dbk-form-actions{display:flex;flex-direction:column;gap:10px;padding-top:8px;}' +
            '.dbk-btn-calc{width:100%;padding:10px 16px;border:none;border-radius:9999px;background:#ffd814;color:#111827;font-weight:600;font-size:15px;cursor:pointer;}' +
            '.dbk-btn-calc:hover{background:#f7ca00;}' +
            '.dbk-btn-template{width:100%;padding:10px 16px;border:1px solid #d1d5db;border-radius:9999px;background:#fff;color:#374151;font-weight:500;font-size:14px;cursor:pointer;}' +
            '.dbk-btn-reset{background:none;border:none;color:#7c3aed;font-size:13px;font-weight:500;cursor:pointer;padding:8px;text-align:center;}' +
            '.dbk-amz-main{padding:18px 22px;background:#fff;}' +
            '.dbk-tables{display:grid;grid-template-columns:1fr;gap:16px;margin-bottom:20px;}' +
            '@media(min-width:768px){.dbk-tables{grid-template-columns:1fr 1fr;}}' +
            '.dbk-twrap{overflow-x:auto;}' +
            '.dbk-table{width:100%;font-size:13px;border-collapse:collapse;}' +
            '.dbk-tr-h{border-bottom:2px solid #e5e7eb;}' +
            '.dbk-th-n,.dbk-th-d,.dbk-th-w,.dbk-th-h{padding:8px 6px;font-weight:500;color:#6b7280;text-align:left;}' +
            '.dbk-th-w,.dbk-th-h{text-align:right;}' +
            '.dbk-tr{border-bottom:1px solid #f3f4f6;}' +
            '.dbk-tr:hover{background:#f9fafb;}' +
            '.dbk-td{padding:10px 6px;color:#111;}' +
            '.dbk-td-link span{display:inline-flex;align-items:center;gap:4px;color:#5b21b6;cursor:default;}' +
            '.dbk-td-num{text-align:right;font-variant-numeric:tabular-nums;}' +
            '.dbk-preview-box{background:#f8f9fa;border:1px solid #e5e7eb;border-radius:8px;padding:16px;min-height:280px;display:flex;align-items:center;justify-content:center;}' +
            '.dbk-preview-book{position:relative;display:flex;width:100%;max-width:520px;aspect-ratio:1.45/1;border:2px solid #9ca3af;background:#fff;}' +
            '.dbk-pv-back,.dbk-pv-front{position:relative;border-right:1px dashed #9ca3af;min-width:0;}' +
            '.dbk-pv-front{border-right:none;}' +
            '.dbk-pv-spine{border-right:1px dashed #9ca3af;background:rgba(249,250,251,.9);min-width:8px;}' +
            '.dbk-pv-flex{display:flex;align-items:center;justify-content:center;padding:12px;}' +
            '.dbk-kdp-logo{width:72px;height:72px;border-radius:50%;border:1px solid #d1d5db;display:flex;align-items:center;justify-content:center;text-align:center;font-size:8px;color:#6b7280;line-height:1.2;opacity:.75;}' +
            '.dbk-kdp-logo strong{font-size:9px;color:#374151;font-weight:600;}' +
            '.dbk-barcode{position:absolute;bottom:16px;width:48px;height:32px;border:1px solid #d1d5db;background:#fff;}' +
            '.dbk-barcode-rtl{right:16px;left:auto;}' +
            '.dbk-barcode-ltr{left:16px;right:auto;}' +
            '.dbk-mk{position:absolute;width:22px;height:22px;background:#000;color:#fff;border-radius:50%;font-size:10px;font-weight:700;display:flex;align-items:center;justify-content:center;z-index:5;}' +
            '.dbk-mk-l{left:-10px;top:50%;transform:translateY(-50%);}' +
            '.dbk-mk-2{left:18px;top:36px;}' +
            '.dbk-mk-3{left:22%;top:36px;}' +
            '.dbk-mk-4{left:50%;top:36px;transform:translateX(-50%);}' +
            '.dbk-mk-5{right:22%;top:36px;}' +
            '.dbk-mk-6{left:calc(50% + 12px);top:22%;}' +
            '.dbk-mk-7{left:calc(50% + 12px);top:32%;}' +
            '.dbk-mk-8{left:calc(50% + 12px);top:42%;}' +
            '.dbk-mk-9{bottom:48px;}' +
            '.dbk-m9-rtl{right:72px;left:auto;}' +
            '.dbk-m9-ltr{left:72px;right:auto;}' +
            '.dbk-ref-note{display:flex;align-items:center;gap:8px;margin-top:12px;font-size:12px;color:#6b7280;}' +
            '.dbk-info-ic{flex-shrink:0;color:#fff;background:#7c3aed;border-radius:50%;padding:2px;}' +
            '.dbk-td-link .dbk-chev{display:inline-block;vertical-align:middle;width:14px;height:14px;}';
        document.head.appendChild(st);
    }

    global.CoverDownloadModal = {
        buildDownloadFlowHtml: buildDownloadFlowHtml,
        buildCalculatorBlock: buildCalculatorBlock,
        buildEbookGuidelinesHtml: buildEbookGuidelinesHtml,
        injectStyles: injectDbkStylesOnce,
        readCalcForm: readCalcForm,
        updateCalculatorDom: updateCalculatorDom,
        saveCalcToStorage: saveCalcToStorage,
        loadCalcFromStorage: loadCalcFromStorage,
        calcStorageKey: calcStorageKey,
        paperbackPageLimits: paperbackPageLimits,
        applyPageInputLimits: applyPageInputLimits
    };
})(typeof window !== 'undefined' ? window : this);
