/**
 * Cover export: EPUB, print PNGs (back / spine / front / wrap), ZIP + manifest, PDF hook.
 * Depends: SweetAlert2, cover-download-modal.js (CoverDownloadModal), JSZip, optional html2canvas.
 */
(function (global) {
    'use strict';

    var EBOOK_COVER = { w: 1600, h: 2560 }; // KDP: min 1000px shortest side; 2560px longest recommended
    var BLEED_IN = 0.125;
    var PRINT_DPI = 300;
    var KDP_DEFAULT_PAPER = 'White paper';
    /** Case-bound diagram: 1" cloth wrap margin on all sides. */
    var CASE_WRAP_MARGIN_IN = 1.0;
    /** Hinge joint between spine board and front/back panels (3/8"). */
    var CASE_HINGE_GAP_IN = 0.375;
    /** 150 pages → 3/8" spine board in reference diagram. */
    var CASE_SPINE_PER_PAGE_IN = 0.0025;

    var TRIM_SIZES = {
        '5x8': { w: 5, h: 8 },
        '5.25x8': { w: 5.25, h: 8 },
        '4.75x5.25': { w: 4.75, h: 5.25 },
        '5.5x8.5': { w: 5.5, h: 8.5 },
        '6x9': { w: 6, h: 9 },
        '6.14x9.21': { w: 6.14, h: 9.21 },
        '7x10': { w: 7, h: 10 },
        '8x10': { w: 8, h: 10 },
        '8.5x11': { w: 8.5, h: 11 }
    };

    function loadScriptOnce(src, dataAttr) {
        return new Promise(function (resolve, reject) {
            var sel = 'script[data-asset="' + dataAttr + '"]';
            if (document.querySelector(sel)) {
                resolve();
                return;
            }
            var s = document.createElement('script');
            s.src = src;
            s.async = true;
            s.setAttribute('data-asset', dataAttr);
            s.onload = function () { resolve(); };
            s.onerror = function () { reject(new Error('Failed to load ' + src)); };
            document.head.appendChild(s);
        });
    }

    function ensureJsZip() {
        if (typeof JSZip !== 'undefined') return Promise.resolve();
        return loadScriptOnce('https://cdn.jsdelivr.net/npm/jszip@3.10.1/dist/jszip.min.js', 'jszip');
    }

    function escapeXml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&apos;');
    }

    function sanitizeFilename(s) {
        var x = String(s || 'book').replace(/[^\w\-\s]/g, '').replace(/\s+/g, '-').replace(/^-|-$/g, '');
        return x || 'book';
    }

    /** Spine estimate (inches) — mirrors KDP calculator defaults for white paper. */
    function estimateSpineInches(pageCount, _paperType, interiorBw) {
        var p = Math.max(24, Math.min(828, parseInt(pageCount, 10) || 200));
        var mult = 0.002252;
        if (interiorBw === false) mult = 0.002347;
        var sp = p * mult;
        return Math.max(0.055, sp);
    }

    function parseTrim(trimKey) {
        var t = TRIM_SIZES[trimKey] || TRIM_SIZES['6x9'];
        return { w: t.w, h: t.h };
    }

    function printLayoutInches(trimKey, pageCount, _paperType, interiorBw, savedSpine) {
        var trim = parseTrim(trimKey);
        var spine = savedSpine > 0 ? savedSpine : estimateSpineInches(pageCount, KDP_DEFAULT_PAPER, interiorBw !== false);
        var totalW = 2 * trim.w + spine + 2 * BLEED_IN;
        var totalH = trim.h + 2 * BLEED_IN;
        var backPanel = trim.w + BLEED_IN;
        var frontPanel = trim.w + BLEED_IN;
        return {
            trim: trim,
            spine: spine,
            totalW: totalW,
            totalH: totalH,
            backPanel: backPanel,
            frontPanel: frontPanel,
            bleed: BLEED_IN,
            equalSplit: false
        };
    }

    function loadImageElement(src) {
        return new Promise(function (resolve, reject) {
            if (!src) {
                resolve(null);
                return;
            }
            var img = new Image();
            img.crossOrigin = 'anonymous';
            img.onload = function () { resolve(img); };
            img.onerror = function () { reject(new Error('Image load failed: ' + src)); };
            img.src = src;
        });
    }

    function drawCoverContain(ctx, img, x, y, w, h) {
        if (!img || !img.width) return;
        var ir = img.width / img.height;
        var tr = w / h;
        var dw, dh, ox, oy;
        if (ir > tr) {
            dh = h;
            dw = dh * ir;
            ox = x + (w - dw) / 2;
            oy = y;
        } else {
            dw = w;
            dh = dw / ir;
            ox = x;
            oy = y + (h - dh) / 2;
        }
        ctx.drawImage(img, ox, oy, dw, dh);
    }

    /** Crop-to-fill within exact panel bounds (print trim + bleed slot). */
    function drawCoverFill(ctx, img, x, y, w, h) {
        if (!img || !img.width || w < 1 || h < 1) return;
        var ir = img.width / img.height;
        var tr = w / h;
        var sw, sh, sx, sy;
        if (ir > tr) {
            sh = img.height;
            sw = sh * tr;
            sx = (img.width - sw) / 2;
            sy = 0;
        } else {
            sw = img.width;
            sh = sw / tr;
            sx = 0;
            sy = (img.height - sh) / 2;
        }
        ctx.drawImage(img, sx, sy, sw, sh, x, y, w, h);
    }

    function resizeCoverForEbook(img, tw, th) {
        var c = document.createElement('canvas');
        c.width = tw;
        c.height = th;
        var ctx = c.getContext('2d');
        ctx.fillStyle = '#0f172a';
        ctx.fillRect(0, 0, tw, th);
        drawCoverContain(ctx, img, 0, 0, tw, th);
        return c.toDataURL('image/jpeg', 0.92);
    }

    function drawBackPanel(ctx, x, y, w, h, meta) {
        var title = meta.title || 'Title';
        var author = meta.author || '';
        var blurb = (meta.synopsis || '').trim() || 'Add your back-cover description in Image Direction or your formatter — this area is a print-safe placeholder you can replace in any design tool.';
        if (blurb.length > 520) blurb = blurb.slice(0, 517) + '…';

        var g = ctx.createLinearGradient(x, y, x + w, y + h);
        g.addColorStop(0, '#1e1b4b');
        g.addColorStop(0.45, '#312e81');
        g.addColorStop(1, '#0f172a');
        ctx.fillStyle = g;
        ctx.fillRect(x, y, w, h);

        ctx.save();
        ctx.fillStyle = 'rgba(255,255,255,0.06)';
        ctx.beginPath();
        ctx.arc(x + w * 0.85, y + h * 0.2, w * 0.35, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();

        var pad = Math.max(12, w * 0.06);
        ctx.fillStyle = '#f8fafc';
        ctx.font = 'bold ' + Math.max(14, w * 0.045) + 'px Inter, Segoe UI, system-ui, sans-serif';
        ctx.fillText(title.length > 42 ? title.slice(0, 39) + '…' : title, x + pad, y + pad + 18);

        ctx.fillStyle = '#a5b4fc';
        ctx.font = Math.max(11, w * 0.028) + 'px Inter, Segoe UI, system-ui, sans-serif';
        ctx.fillText(author, x + pad, y + pad + 38);

        ctx.fillStyle = '#e2e8f0';
        ctx.font = Math.max(10, w * 0.024) + 'px Georgia, Merriweather, serif';
        wrapText(ctx, blurb, x + pad, y + pad + 68, w - pad * 2, Math.max(13, w * 0.032));

        var bw = Math.min(120, w * 0.28);
        var bh = 36;
        var bx = x + (w - bw) / 2;
        var by = y + h - pad - bh - 8;
        ctx.fillStyle = '#fff';
        ctx.fillRect(bx, by, bw, bh);
        ctx.strokeStyle = '#94a3b8';
        ctx.lineWidth = 1;
        ctx.strokeRect(bx + 0.5, by + 0.5, bw - 1, bh - 1);
        ctx.fillStyle = '#64748b';
        ctx.font = '9px monospace';
        ctx.fillText('ISBN / barcode', bx + 8, by + bh / 2 + 3);

        ctx.fillStyle = 'rgba(148,163,184,0.85)';
        ctx.font = '8px Inter, sans-serif';
        ctx.fillText('KDP print — leave barcode clear', x + pad, y + h - 8);
    }

    function wrapText(ctx, text, x, y, maxWidth, lineHeight) {
        var words = text.split(/\s+/);
        var line = '';
        var cy = y;
        for (var n = 0; n < words.length; n++) {
            var test = line ? line + ' ' + words[n] : words[n];
            if (ctx.measureText(test).width > maxWidth && line) {
                ctx.fillText(line, x, cy);
                line = words[n];
                cy += lineHeight;
                if (cy > y + lineHeight * 12) return;
            } else {
                line = test;
            }
        }
        if (line) ctx.fillText(line, x, cy);
    }

    function drawSpine(ctx, x, y, w, h, title) {
        var g = ctx.createLinearGradient(x, 0, x + w, 0);
        g.addColorStop(0, '#3730a3');
        g.addColorStop(1, '#1e1b4b');
        ctx.fillStyle = g;
        ctx.fillRect(x, y, w, h);

        ctx.save();
        ctx.translate(x + w / 2, y + h / 2);
        ctx.rotate(-Math.PI / 2);
        ctx.fillStyle = '#f1f5f9';
        ctx.font = 'bold ' + Math.max(10, Math.min(w * 1.2, 14)) + 'px Inter, system-ui, sans-serif';
        var t = (title || 'Title').toUpperCase();
        if (t.length > 48) t = t.slice(0, 45) + '…';
        var tw = ctx.measureText(t).width;
        ctx.fillText(t, -tw / 2, 4);
        ctx.restore();
    }

    function buildPrintCanvas(frontImg, layoutInches, meta) {
        var lay = layoutInches;
        var W = Math.round(lay.totalW * PRINT_DPI);
        var H = Math.round(lay.totalH * PRINT_DPI);
        var c = document.createElement('canvas');
        c.width = W;
        c.height = H;
        var ctx = c.getContext('2d');
        ctx.fillStyle = '#0f172a';
        ctx.fillRect(0, 0, W, H);

        var spineW = lay.spine * PRINT_DPI;
        var backW;
        var frontW;
        if (lay.equalSplit) {
            var restPx = W - spineW;
            backW = restPx / 2;
            frontW = restPx / 2;
        } else {
            backW = lay.backPanel * PRINT_DPI;
            frontW = lay.frontPanel * PRINT_DPI;
        }
        var x0 = 0;
        var x1 = backW;
        var x2 = backW + spineW;

        drawBackPanel(ctx, x0, 0, backW, H, meta);
        drawSpine(ctx, x1, 0, spineW, H, meta.title);
        drawCoverContain(ctx, frontImg, x2, 0, frontW, H);

        return c;
    }

    /**
     * KDP-style dimension model (mm) aligned to Amazon calculator.
     * Reference: https://kdp.amazon.com/cover-calculator
     * Paper type is intentionally fixed to KDP white paper to keep this automatic.
     */
    function computeKdpDimensionsMm(binding, interior, paper, trimStr, pages) {
        var raw = String(trimStr || '').split(/[x×]/i);
        var trimW = parseFloat((raw[0] || '152.4').trim()) || 152.4;
        var trimH = parseFloat((raw[1] || '228.6').trim()) || 228.6;
        var p = Math.max(24, Math.min(828, parseInt(pages, 10) || 100));

        var thickness = 0.0572;
        if (interior === 'Premium color') thickness = 0.0596;

        var spine = p * thickness;
        var bleed = 3.175;
        var wrap = binding === 'Hardcover' ? 15 : 0;
        var hinge = binding === 'Hardcover' ? 10 : 0;
        var spineMargin = 1.59;
        var barcodeMarginW = 6.35;
        var barcodeMarginH = 9.52;

        var data = {};
        if (binding === 'Hardcover') {
            data[1] = { w: (trimW * 2) + spine + (hinge * 2) + (wrap * 2), h: trimH + (wrap * 2) };
            data[2] = { w: trimW, h: trimH };
            data[3] = { w: bleed, h: bleed };
            data[4] = { w: wrap, h: wrap };
            data[5] = { w: hinge, h: trimH + (wrap * 2) };
            data[6] = { w: spine, h: trimH + (wrap * 2) };
            data[7] = { w: Math.max(0, spine - spineMargin * 2), h: trimH + (wrap * 2) - bleed * 2 };
            data[8] = { w: spineMargin, h: spineMargin };
            data[9] = { w: barcodeMarginW, h: barcodeMarginH };
        } else {
            data[1] = { w: (trimW * 2) + spine + (bleed * 2), h: trimH + (bleed * 2) };
            data[2] = { w: trimW, h: trimH };
            data[3] = { w: bleed, h: bleed };
            data[4] = { w: 0, h: 0 };
            data[5] = { w: 0, h: 0 };
            data[6] = { w: spine, h: trimH + (bleed * 2) };
            data[7] = { w: Math.max(0, spine - spineMargin * 2), h: trimH + (bleed * 2) - bleed * 2 };
            data[8] = { w: spineMargin, h: spineMargin };
            data[9] = { w: barcodeMarginW, h: barcodeMarginH };
        }

        return {
            data: data,
            binding: binding,
            interior: interior,
            paper: KDP_DEFAULT_PAPER,
            trimW: trimW,
            trimH: trimH,
            spineMm: spine,
            pages: p
        };
    }

    function buildPrintCanvasFromCalc(frontImg, calc, meta) {
        var scale = PRINT_DPI / 25.4;
        var W = Math.round(calc.data[1].w * scale);
        var H = Math.round(calc.data[1].h * scale);
        var spineW = Math.round(calc.data[6].w * scale);
        var restPx = W - spineW;
        var backW = Math.floor(restPx / 2);
        var frontW = restPx - backW;

        var c = document.createElement('canvas');
        c.width = W;
        c.height = H;
        var ctx = c.getContext('2d');
        ctx.fillStyle = '#0f172a';
        ctx.fillRect(0, 0, W, H);

        drawBackPanel(ctx, 0, 0, backW, H, meta);
        drawSpine(ctx, backW, 0, spineW, H, meta.title);
        drawCoverContain(ctx, frontImg, backW + spineW, 0, frontW, H);

        c._dbkParts = { backW: backW, spineW: spineW, frontW: frontW, H: H };
        return c;
    }

    function canvasToBlob(canvas, type, quality) {
        return new Promise(function (resolve) {
            canvas.toBlob(function (b) { resolve(b); }, type || 'image/png', quality);
        });
    }

    function buildCoverZipBlobs(title, author, calc, compositeCanvas, partBlobs) {
        var manifest = {
            title: title,
            author: author,
            generatedAtUtc: new Date().toISOString(),
            kdpPrintCover: {
                fullCoverMm: { width: calc.data[1].w, height: calc.data[1].h },
                spineMm: calc.data[6].w,
                binding: calc.binding,
                interior: calc.interior,
                paper: calc.paper,
                trimMm: { width: calc.trimW, height: calc.trimH },
                pageCount: calc.pages
            },
            files: [
                'cover-back.png',
                'cover-spine.png',
                'cover-front.png',
                'cover-full-wrap.png',
                'manifest.json'
            ],
            note: 'Physical print uses full-wrap PNG at 300 DPI. eBook uses separate EPUB export.'
        };
        return ensureJsZip().then(function () {
            var zip = new JSZip();
            zip.file('manifest.json', JSON.stringify(manifest, null, 2));
            zip.file('cover-full-wrap.png', partBlobs.wrap);
            zip.file('cover-back.png', partBlobs.back);
            zip.file('cover-spine.png', partBlobs.spine);
            zip.file('cover-front.png', partBlobs.front);
            return zip.generateAsync({ type: 'blob', mimeType: 'application/zip' });
        });
    }

    function sliceCanvasRegion(cv, x, y, w, h) {
        var o = document.createElement('canvas');
        o.width = w;
        o.height = h;
        o.getContext('2d').drawImage(cv, x, y, w, h, 0, 0, w, h);
        return o;
    }

    function randomUuidUrn() {
        try {
            if (global.crypto && typeof global.crypto.randomUUID === 'function') {
                return 'urn:uuid:' + global.crypto.randomUUID();
            }
        } catch (e) { /* ignore */ }
        var s = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = (Math.random() * 16) | 0;
            var v = c === 'x' ? r : (r & 0x3) | 0x8;
            return v.toString(16);
        });
        return 'urn:uuid:' + s;
    }

    function buildEpubBlob(title, author, coverJpegDataUrl) {
        var uuid = randomUuidUrn();
        var now = new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
        var base = sanitizeFilename(title);

        var jpegB64 = coverJpegDataUrl.split(',')[1];
        var jpegBytes = Uint8Array.from(atob(jpegB64), function (ch) { return ch.charCodeAt(0); });

        var container = '<?xml version="1.0" encoding="UTF-8"?>\n' +
            '<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">\n' +
            '  <rootfiles>\n' +
            '    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>\n' +
            '  </rootfiles>\n</container>';

        var opf = '<?xml version="1.0" encoding="UTF-8"?>\n' +
            '<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="pub-id">\n' +
            '  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">\n' +
            '    <dc:identifier id="pub-id">' + escapeXml(uuid) + '</dc:identifier>\n' +
            '    <dc:title>' + escapeXml(title || 'Book') + '</dc:title>\n' +
            '    <dc:creator>' + escapeXml(author || 'Author') + '</dc:creator>\n' +
            '    <dc:language>en</dc:language>\n' +
            '    <meta property="dcterms:modified">' + escapeXml(now) + '</meta>\n' +
            '  </metadata>\n' +
            '  <manifest>\n' +
            '    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>\n' +
            '    <item id="cover-image" href="images/cover.jpg" media-type="image/jpeg" properties="cover-image"/>\n' +
            '    <item id="cover-page" href="cover.xhtml" media-type="application/xhtml+xml"/>\n' +
            '    <item id="c1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>\n' +
            '  </manifest>\n' +
            '  <spine>\n' +
            '    <itemref idref="cover-page"/>\n' +
            '    <itemref idref="c1"/>\n' +
            '  </spine>\n</package>';

        var nav = '<?xml version="1.0" encoding="UTF-8"?>\n' +
            '<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">\n<head><title>Nav</title></head>\n<body>\n' +
            '<nav epub:type="toc"><ol><li><a href="cover.xhtml">Cover</a></li><li><a href="chapter1.xhtml">Start</a></li></ol></nav>\n</body></html>';

        var coverXhtml = '<?xml version="1.0" encoding="UTF-8"?>\n' +
            '<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Cover</title><style>body{margin:0;text-align:center;} img{max-width:100%;height:auto;}</style></head>\n' +
            '<body><img src="images/cover.jpg" alt="Cover"/></body></html>';

        var ch1 = '<?xml version="1.0" encoding="UTF-8"?>\n' +
            '<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Start</title></head><body><p>This EPUB contains your Kindle cover image at KDP-recommended pixel dimensions. Replace interior files in your main manuscript EPUB or upload this as a cover-only asset per KDP guidance.</p></body></html>';

        return ensureJsZip().then(function () {
            var zip = new JSZip();
            zip.file('mimetype', 'application/epub+zip', { compression: 'STORE' });
            zip.folder('META-INF').file('container.xml', container);
            var oebps = zip.folder('OEBPS');
            oebps.file('content.opf', opf);
            oebps.file('nav.xhtml', nav);
            oebps.file('cover.xhtml', coverXhtml);
            oebps.file('chapter1.xhtml', ch1);
            oebps.folder('images').file('cover.jpg', jpegBytes);
            return zip.generateAsync({ type: 'blob', mimeType: 'application/epub+zip', compression: 'DEFLATE' });
        });
    }

    function triggerDownload(blob, filename) {
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    }


    function openCalculatorModal(options) {
        if (typeof Swal === 'undefined') {
            alert('SweetAlert2 is required.');
            return;
        }
        if (!global.CoverDownloadModal) {
            alert('Cover download modal script failed to load.');
            return;
        }
        CoverDownloadModal.injectStyles();

        var bookId = options.bookId || 0;
        var initialPages = options.initialPages || 98;
        var remindReopenDownload = options.remindReopenDownload === true;

        function wireCalculatorRoot(root, prefix, persistBookId) {
            function runCalc(persist) {
                var c = CoverDownloadModal.updateCalculatorDom(root, computeKdpDimensionsMm, prefix);
                if (persist && persistBookId && c) {
                    CoverDownloadModal.saveCalcToStorage(persistBookId, c);
                    try {
                        document.dispatchEvent(new CustomEvent('dbkcalccomplete', { detail: { bookId: persistBookId } }));
                    } catch (e) { /* ignore */ }
                    Swal.fire({
                        toast: true,
                        position: 'top-end',
                        icon: 'success',
                        title: 'Print dimensions saved',
                        text: 'Download book se EPUB ab unlock hai.',
                        showConfirmButton: false,
                        timer: 2800
                    });
                }
                return c;
            }

            var calcBtn = root.querySelector('#' + prefix + '_calcBtn');
            if (calcBtn) calcBtn.addEventListener('click', function () { runCalc(true); });

            var resetBtn = root.querySelector('#' + prefix + '_resetBtn');
            if (resetBtn) resetBtn.addEventListener('click', function () {
                var f = root.querySelector('#' + prefix + '_CalcForm');
                if (f) f.reset();
                runCalc(false);
            });

            var tplBtn = root.querySelector('#' + prefix + '_templateBtn');
            if (tplBtn) tplBtn.addEventListener('click', function () {
                Swal.fire({
                    toast: true,
                    position: 'top',
                    width: '36em',
                    icon: 'info',
                    title: 'Templates & full wrap',
                    html: '<p style="margin:0;color:#475569;font-size:13px;line-height:1.5;text-align:left">Download book → Full wrap PNG ya ZIP. Tables ke mm sizes par design align karein — sab isi app se.</p>',
                    showConfirmButton: false,
                    timer: 6500
                });
            });

            ['_binding', '_interior', '_direction', '_units', '_trim', '_pages'].forEach(function (suf) {
                var sel = root.querySelector('#' + prefix + suf);
                if (sel) {
                    sel.addEventListener('change', function () { runCalc(false); });
                    sel.addEventListener('keyup', function () { runCalc(false); });
                }
            });

            fetch('/BookDesign/GetCoverDesignByBook?bookId=' + encodeURIComponent(persistBookId))
                .then(function (r) { return r.json(); })
                .then(function (d) {
                    if (!d || !d.success) {
                        runCalc(false);
                        return;
                    }
                    if (d.pagesCount) {
                        var pg = root.querySelector('#' + prefix + '_pages');
                        if (pg) pg.value = d.pagesCount;
                    }
                    var b = root.querySelector('#' + prefix + '_binding');
                    if (b && d.bindingType) {
                        var bt = String(d.bindingType).toLowerCase();
                        b.value = bt.indexOf('hard') >= 0 ? 'Hardcover' : 'Paperback';
                    }
                    var intSel = root.querySelector('#' + prefix + '_interior');
                    if (intSel && d.interiorType) {
                        var it = String(d.interiorType).toLowerCase();
                        intSel.value = (it.indexOf('premium') >= 0 || (it.indexOf('color') >= 0 && it.indexOf('black') < 0))
                            ? 'Premium color' : 'Black & white';
                    }
                    var pap = root.querySelector('#' + prefix + '_paper');
                    if (pap && d.paperType) {
                        var pt = String(d.paperType).toLowerCase();
                        pap.value = pt.indexOf('cream') >= 0 ? 'Cream paper' : 'White paper';
                    }
                    runCalc(false);
                })
                .catch(function () { runCalc(false); });
        }

        Swal.fire({
            title: '',
            html: '<div class="dbk-calc-standalone-root">' + CoverDownloadModal.buildCalculatorBlock('dbc', initialPages) + '</div>',
            width: '94%',
            maxWidth: 1220,
            showConfirmButton: false,
            showCloseButton: true,
            customClass: { popup: 'dbk-glass-popup', container: 'dbk-glass-backdrop' },
            didOpen: function () {
                var root = Swal.getHtmlContainer();
                wireCalculatorRoot(root, 'dbc', bookId);
            },
            didClose: function () {
                if (!remindReopenDownload) return;
                Swal.fire({
                    toast: true,
                    position: 'top-end',
                    icon: 'info',
                    title: 'Download book',
                    text: 'Ab Download book se EPUB aur print files lein.',
                    showConfirmButton: false,
                    timer: 4200
                });
            }
        });
    }

    function openModal(options) {
        if (typeof Swal === 'undefined') {
            alert('SweetAlert2 is required.');
            return;
        }
        if (!global.CoverDownloadModal) {
            alert('Cover download modal script failed to load.');
            return;
        }
        CoverDownloadModal.injectStyles();

        var bookId = options.bookId || 0;
        var title = options.title || 'My Book';
        var author = options.author || '';
        var synopsis = options.synopsis || '';
        var capturePreviewShell = options.capturePreviewShell;
        var getFrontSrc = options.getFrontSrc;

        var state = {
            frontImg: null,
            lastComposite: null,
            sequential: null
        };

        var meta = { title: title, author: author, synopsis: synopsis };
        var base = sanitizeFilename(title);

        function loadSequentialImageSet() {
            var seq = global.__coverDesignSequentialAssets;
            if (!seq || !seq.front || !seq.back || !seq.spine) {
                state.sequential = null;
                return Promise.resolve(null);
            }
            return Promise.all([
                loadImageElement(seq.front),
                loadImageElement(seq.back),
                loadImageElement(seq.spine),
                seq.wrap ? loadImageElement(seq.wrap).catch(function () { return null; }) : Promise.resolve(null)
            ]).then(function (imgs) {
                state.sequential = {
                    front: imgs[0],
                    back: imgs[1],
                    spine: imgs[2],
                    wrap: imgs[3]
                };
                if (!state.frontImg) state.frontImg = imgs[0];
                return state.sequential;
            }).catch(function () {
                state.sequential = null;
                return null;
            });
        }

        function buildCompositeFromState(calc) {
            if (!calc) return null;
            var scale = PRINT_DPI / 25.4;
            var W = Math.round(calc.data[1].w * scale);
            var H = Math.round(calc.data[1].h * scale);
            var spineW = Math.round(calc.data[6].w * scale);
            var restPx = W - spineW;
            var backW = Math.floor(restPx / 2);
            var frontW = restPx - backW;

            var c = document.createElement('canvas');
            c.width = W;
            c.height = H;
            var ctx = c.getContext('2d');
            ctx.fillStyle = '#0f172a';
            ctx.fillRect(0, 0, W, H);

            if (state.sequential && state.sequential.back && state.sequential.spine && state.sequential.front) {
                drawCoverContain(ctx, state.sequential.back, 0, 0, backW, H);
                drawCoverContain(ctx, state.sequential.spine, backW, 0, spineW, H);
                drawCoverContain(ctx, state.sequential.front, backW + spineW, 0, frontW, H);
            } else if (state.frontImg) {
                drawBackPanel(ctx, 0, 0, backW, H, meta);
                drawSpine(ctx, backW, 0, spineW, H, meta.title);
                drawCoverContain(ctx, state.frontImg, backW + spineW, 0, frontW, H);
            } else {
                return null;
            }

            c._dbkParts = { backW: backW, spineW: spineW, frontW: frontW, H: H };
            return c;
        }

        function syncCalcFromStorage() {
            return CoverDownloadModal.loadCalcFromStorage(bookId);
        }

        function renderBackThumb(calc) {
            if (!calc) return '';
            var scale = PRINT_DPI / 25.4;
            var W = Math.round(calc.data[1].w * scale);
            var H = Math.round(calc.data[1].h * scale);
            var spineW = Math.round(calc.data[6].w * scale);
            var backW = Math.floor((W - spineW) / 2);
            var c = document.createElement('canvas');
            c.width = Math.min(360, backW);
            var sc = c.width / backW;
            c.height = Math.round(H * sc);
            var ctx = c.getContext('2d');
            ctx.scale(sc, sc);
            drawBackPanel(ctx, 0, 0, backW, H, meta);
            return c.toDataURL('image/png');
        }

        function updateCalcStatusLine(root) {
            var calc = syncCalcFromStorage();
            var st = root.querySelector('#dbkCalcStatusText');
            if (st) {
                if (calc) {
                    var mmW = calc.data[1].w.toFixed(2);
                    var mmH = calc.data[1].h.toFixed(2);
                    st.textContent = 'saved — full cover ' + mmW + ' × ' + mmH + ' mm (spine ' + calc.data[6].w.toFixed(2) + ' mm)';
                } else {
                    st.textContent = 'not calculated yet';
                }
            }
        }

        function updateEpubGate(root) {
            var calc = syncCalcFromStorage();
            var btn = root.querySelector('#dbkDownloadEpub');
            var msg = root.querySelector('#dbkEpubGateMsg');
            if (btn) {
                btn.disabled = !calc;
                if (calc) btn.classList.remove('dbk-btn-disabled');
                else btn.classList.add('dbk-btn-disabled');
            }
            if (msg) {
                msg.textContent = calc
                    ? 'Print dimensions calculated. Ab EPUB download khol sakte hain.'
                    : 'Pehle Print cover calculator khol kar Calculate dimensions dabayein — phir EPUB unlock ho jayega.';
            }
        }

        function refreshCoverUi(root) {
            var calc = syncCalcFromStorage();
            var spec = root.querySelector('#dbkPngSpecs');
            if (spec) {
                if (calc) {
                    var pxW = Math.round(calc.data[1].w * PRINT_DPI / 25.4);
                    var pxH = Math.round(calc.data[1].h * PRINT_DPI / 25.4);
                    spec.textContent = 'Full wrap ' + pxW + ' × ' + pxH + ' px @ 300 DPI (saved calculator)';
                } else {
                    spec.textContent = 'Pehle print calculator se dimensions save karein — phir yahan preview aur exports sahi size par honge.';
                }
            }
            var backImg = root.querySelector('#dbkImgBack');
            if (backImg) {
                try { backImg.src = renderBackThumb(calc); } catch (e) { backImg.src = ''; }
            }
            if (!calc || (!state.frontImg && !state.sequential)) {
                var cf = root.querySelector('#dbkCompositeThumb');
                if (cf) cf.src = '';
                state.lastComposite = null;
                return;
            }
            try {
                var canvas = buildCompositeFromState(calc);
                state.lastComposite = canvas;
                var url = canvas ? canvas.toDataURL('image/png') : '';
                var comp = root.querySelector('#dbkCompositeThumb');
                if (comp) comp.src = url;
            } catch (e) {
                state.lastComposite = null;
            }
        }

        function onCalcStored(ev) {
            if (!ev || !ev.detail || ev.detail.bookId !== bookId) return;
            if (typeof Swal === 'undefined' || !Swal.isVisible()) return;
            var root = Swal.getHtmlContainer();
            if (!root || !root.querySelector('#dbkDownloadEpub')) return;
            updateCalcStatusLine(root);
            updateEpubGate(root);
            refreshCoverUi(root);
        }

        Swal.fire({
            title: '',
            html: CoverDownloadModal.buildDownloadFlowHtml(bookId),
            width: '92%',
            maxWidth: 1280,
            showConfirmButton: false,
            showCloseButton: true,
            customClass: { popup: 'dbk-download-popup', htmlContainer: 'dbk-download-html' },
            didOpen: function () {
                var root = Swal.getHtmlContainer();
                document.addEventListener('dbkcalccomplete', onCalcStored);

                var epubPx = root.querySelector('#dbkEpubPx');
                if (epubPx) epubPx.textContent = EBOOK_COVER.w + ' × ' + EBOOK_COVER.h;
                var en = root.querySelector('#dbkEpubName');
                if (en) en.textContent = base + '-kdp-cover.epub';

                updateCalcStatusLine(root);
                updateEpubGate(root);

                var pubBtn = root.querySelector('#dbkBtnPublishing');
                if (pubBtn) {
                    pubBtn.addEventListener('click', function () {
                        Swal.close();
                        setTimeout(function () {
                            window.location.href = '/Dashboard/Publish?bookId=' + encodeURIComponent(bookId);
                        }, 150);
                    });
                }

                var calcOpenBtn = root.querySelector('#dbkBtnOpenCalc');
                if (calcOpenBtn) {
                    calcOpenBtn.addEventListener('click', function () {
                        Swal.close();
                        setTimeout(function () {
                            openCalculatorModal({ bookId: bookId, initialPages: 98, remindReopenDownload: true });
                        }, 220);
                    });
                }

                var guideBtn = root.querySelector('#dbkBtnGuidelines');
                var guidePanel = root.querySelector('#dbkGuidelinesPanel');
                if (guideBtn && guidePanel) {
                    guideBtn.addEventListener('click', function () {
                        var show = guidePanel.hasAttribute('hidden');
                        if (show) guidePanel.removeAttribute('hidden');
                        else guidePanel.setAttribute('hidden', '');
                        guideBtn.setAttribute('aria-expanded', show ? 'true' : 'false');
                        if (show) {
                            try { guidePanel.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); } catch (e) { /* ignore */ }
                        }
                    });
                }

                function resolveFrontImage() {
                    return loadSequentialImageSet().then(function () {
                        var src = getFrontSrc && getFrontSrc();
                        if (src) {
                            return loadImageElement(src).then(function (img) {
                                state.frontImg = img;
                                var im = root.querySelector('#dbkImgFront');
                                if (im) im.src = src;
                                refreshCoverUi(root);
                            });
                        }
                        if (capturePreviewShell) {
                            return capturePreviewShell().then(function (dataUrl) {
                                if (!dataUrl) throw new Error('No cover');
                                return loadImageElement(dataUrl).then(function (img) {
                                    state.frontImg = img;
                                    var im = root.querySelector('#dbkImgFront');
                                    if (im) im.src = dataUrl;
                                    refreshCoverUi(root);
                                });
                            });
                        }
                        if (state.sequential && state.sequential.front) {
                            var im2 = root.querySelector('#dbkImgFront');
                            if (im2 && global.__coverDesignSequentialAssets && global.__coverDesignSequentialAssets.front) {
                                im2.src = global.__coverDesignSequentialAssets.front;
                            }
                            refreshCoverUi(root);
                            return;
                        }
                        return Promise.reject(new Error('No cover'));
                    });
                }

                resolveFrontImage().catch(function () {
                    var im = root.querySelector('#dbkImgFront');
                    if (im) im.src = '';
                    refreshCoverUi(root);
                });

                function requireCover() {
                    if (!state.frontImg && !(state.sequential && state.sequential.front)) {
                        Swal.fire({ icon: 'warning', title: 'No cover', text: 'Generate or select a cover first.', confirmButtonColor: '#7c3aed' });
                        return false;
                    }
                    return true;
                }

                function requireCalcForExport() {
                    var calc = syncCalcFromStorage();
                    if (!calc) {
                        Swal.fire({
                            icon: 'info',
                            title: 'Calculator pehle',
                            html: '<p style="color:#64748b">Print cover calculator khol kar <strong>Calculate dimensions</strong> dabayein. Uske baad EPUB aur print PNG/ZIP yahin se kaam karenge.</p>',
                            confirmButtonColor: '#7c3aed'
                        });
                        return null;
                    }
                    return calc;
                }

                function getComposite() {
                    var calc = requireCalcForExport();
                    if (!calc || (!state.frontImg && !state.sequential)) return null;
                    return buildCompositeFromState(calc);
                }

                root.querySelector('#dbkDownloadEpub').addEventListener('click', function () {
                    if (!requireCover()) return;
                    var calc = syncCalcFromStorage();
                    if (!calc) {
                        requireCalcForExport();
                        return;
                    }
                    Swal.fire({ title: 'Building EPUB', html: '<p style="color:#64748b">Embedding cover…</p>', allowOutsideClick: false, showConfirmButton: false, didOpen: function () { Swal.showLoading(); } });
                    try {
                        var jpegDataUrl = resizeCoverForEbook(state.frontImg, EBOOK_COVER.w, EBOOK_COVER.h);
                        buildEpubBlob(title, author, jpegDataUrl).then(function (blob) {
                            Swal.close();
                            triggerDownload(blob, base + '-kdp-cover.epub');
                            Swal.fire({ icon: 'success', title: 'EPUB ready', timer: 2200, showConfirmButton: false });
                        }).catch(function (e) { Swal.close(); Swal.fire({ icon: 'error', title: 'EPUB failed', text: e.message }); });
                    } catch (e) { Swal.close(); Swal.fire({ icon: 'error', title: 'EPUB failed', text: e.message }); }
                });

                root.querySelector('#dbkDlWrap').addEventListener('click', function () {
                    if (!requireCover()) return;
                    var cv = getComposite();
                    if (!cv) return;
                    cv.toBlob(function (blob) {
                        if (blob) triggerDownload(blob, base + '-print-full-wrap.png');
                    }, 'image/png');
                });

                function downloadSlice(which) {
                    if (!requireCover()) return;
                    var cv = getComposite();
                    if (!cv || !cv._dbkParts) return;
                    var p = cv._dbkParts;
                    var slice;
                    if (which === 'back') slice = sliceCanvasRegion(cv, 0, 0, p.backW, p.H);
                    else if (which === 'spine') slice = sliceCanvasRegion(cv, p.backW, 0, p.spineW, p.H);
                    else slice = sliceCanvasRegion(cv, p.backW + p.spineW, 0, p.frontW, p.H);
                    slice.toBlob(function (blob) {
                        if (blob) triggerDownload(blob, base + '-print-' + which + '.png');
                    }, 'image/png');
                }

                root.querySelector('#dbkDlBack').addEventListener('click', function () { downloadSlice('back'); });
                root.querySelector('#dbkDlSpine').addEventListener('click', function () { downloadSlice('spine'); });
                root.querySelector('#dbkDlFront').addEventListener('click', function () { downloadSlice('front'); });

                root.querySelector('#dbkDlZip').addEventListener('click', function () {
                    if (!requireCover()) return;
                    var calc = requireCalcForExport();
                    if (!calc) return;
                    var cv = getComposite();
                    if (!cv || !cv._dbkParts) return;
                    var p = cv._dbkParts;
                    var backCv = sliceCanvasRegion(cv, 0, 0, p.backW, p.H);
                    var spineCv = sliceCanvasRegion(cv, p.backW, 0, p.spineW, p.H);
                    var frontCv = sliceCanvasRegion(cv, p.backW + p.spineW, 0, p.frontW, p.H);
                    Swal.fire({ title: 'Building ZIP', showConfirmButton: false, didOpen: function () { Swal.showLoading(); } });
                    Promise.all([
                        canvasToBlob(cv, 'image/png'),
                        canvasToBlob(backCv, 'image/png'),
                        canvasToBlob(spineCv, 'image/png'),
                        canvasToBlob(frontCv, 'image/png')
                    ]).then(function (blobs) {
                        return buildCoverZipBlobs(title, author, calc, cv, { wrap: blobs[0], back: blobs[1], spine: blobs[2], front: blobs[3] });
                    }).then(function (zipBlob) {
                        Swal.close();
                        triggerDownload(zipBlob, base + '-cover-design.zip');
                        Swal.fire({ icon: 'success', title: 'Package downloaded', text: 'manifest.json + 4 PNG files', timer: 2500, showConfirmButton: false });
                    }).catch(function (e) {
                        Swal.close();
                        Swal.fire({ icon: 'error', title: 'ZIP failed', text: e.message || 'Error' });
                    });
                });

                var pdfBtn = root.querySelector('#dbkDownloadFullPdf');
                if (typeof options.onDownloadFullBookPdf === 'function' && pdfBtn) {
                    pdfBtn.addEventListener('click', function () {
                        var runPdf = options.onDownloadFullBookPdf;
                        Swal.close();
                        setTimeout(function () { try { runPdf(); } catch (e) { console.error(e); } }, 200);
                    });
                } else if (pdfBtn) {
                    pdfBtn.style.display = 'none';
                }

                refreshCoverUi(root);
            },
            didClose: function () {
                document.removeEventListener('dbkcalccomplete', onCalcStored);
            }
        });
    }


    function trimToMmString(trimKey) {
        var t = TRIM_SIZES[trimKey] || TRIM_SIZES['6x9'];
        return String(Math.round(t.w * 25.4 * 100) / 100) + 'x' + String(Math.round(t.h * 25.4 * 100) / 100);
    }

    /**
     * Panel layout (inches): [wrap margin] back | hinge | spine | hinge | front [wrap margin].
     * Matches case-bound cover diagram; paperback uses bleed margin and zero hinge gap.
     */
    function spineInchesPerPage(binding, paper, interior) {
        if (binding === 'Hardcover') return CASE_SPINE_PER_PAGE_IN;
        if (interior === 'Premium color' || interior === 'Standard color') return 0.002347;
        if (paper && paper.indexOf('Cream') >= 0) return CASE_SPINE_PER_PAGE_IN;
        return 0.002252;
    }

    function applyPageCountToLayout(layout, pages, binding, paper, interior) {
        if (!layout) return layout;
        var perPage = spineInchesPerPage(binding || layout.bindingType, paper, interior);
        layout.spineIn = Math.max(0.055, pages * perPage);
        layout.hingeRightXIn = layout.spineXIn + layout.spineIn;
        layout.frontPanelXIn = layout.hingeRightXIn + layout.hingeGapIn;
        layout.canvasWidthIn = layout.frontPanelXIn + layout.panelWidthIn + layout.outerMarginIn;
        layout.pageCount = pages;
        return layout;
    }

    function computeCoverLayout(options) {
        options = options || {};
        var binding = options.bindingType || 'Paperback';
        var isHard = binding === 'Hardcover';
        var trim = parseTrim(options.trimKey || '6x9');
        var pages = Math.max(24, Math.min(828, parseInt(options.pageCount, 10) || 100));
        var interior = options.interiorType || 'Black & white';
        var paper = options.paperType || KDP_DEFAULT_PAPER;
        var perPage = isHard
            ? CASE_SPINE_PER_PAGE_IN
            : (interior === 'Premium color' ? 0.002347 : (interior === 'Standard color' ? 0.002347 : (paper.indexOf('Cream') >= 0 ? CASE_SPINE_PER_PAGE_IN : 0.002252)));
        var spineIn = Math.max(0.055, pages * perPage);
        var outerMargin = isHard ? CASE_WRAP_MARGIN_IN : BLEED_IN;
        var hingeGap = isHard ? CASE_HINGE_GAP_IN : 0;
        var backX = outerMargin;
        var hingeLeftX = backX + trim.w;
        var spineX = hingeLeftX + hingeGap;
        var hingeRightX = spineX + spineIn;
        var frontX = hingeRightX + hingeGap;
        return {
            bindingType: binding,
            pageCount: pages,
            trimW: trim.w,
            trimH: trim.h,
            spineIn: spineIn,
            outerMarginIn: outerMargin,
            hingeGapIn: hingeGap,
            canvasWidthIn: frontX + trim.w + outerMargin,
            canvasHeightIn: outerMargin * 2 + trim.h,
            backPanelXIn: backX,
            hingeLeftXIn: hingeLeftX,
            spineXIn: spineX,
            hingeRightXIn: hingeRightX,
            frontPanelXIn: frontX,
            panelTopYIn: outerMargin,
            panelWidthIn: trim.w,
            panelHeightIn: trim.h
        };
    }

    function layoutFromServerKdp(data) {
        if (!data || !data.kdp) return null;
        var k = data.kdp;
        var L = data.layout || {};
        var trimKey = '6x9';
        var trimLabel = (data.trimSize || '').toLowerCase();
        if (trimLabel.indexOf('5.5') >= 0) trimKey = '5.5x8.5';
        else if (trimLabel.indexOf('8.5') >= 0 && trimLabel.indexOf('11') >= 0) trimKey = '8.5x11';
        var pages = parseInt(data.pageCount, 10);
        if (!Number.isFinite(pages) || pages <= 0) pages = 100;
        var base = computeCoverLayout({
            bindingType: k.bindingType || 'Paperback',
            pageCount: pages,
            trimKey: trimKey,
            paperType: k.paperType,
            interiorType: k.interiorType
        });
        if (typeof k.spineInches === 'number' && k.spineInches > 0) {
            base.spineIn = k.spineInches;
            base.hingeRightXIn = base.spineXIn + base.spineIn;
            base.frontPanelXIn = base.hingeRightXIn + base.hingeGapIn;
            base.canvasWidthIn = base.frontPanelXIn + base.panelWidthIn + base.outerMarginIn;
        }
        if (typeof L.backPanelXInches === 'number') base.backPanelXIn = L.backPanelXInches;
        if (typeof L.spineXInches === 'number') base.spineXIn = L.spineXInches;
        if (typeof L.frontPanelXInches === 'number') base.frontPanelXIn = L.frontPanelXInches;
        if (typeof k.wrapWidthInches === 'number') base.canvasWidthIn = k.wrapWidthInches;
        if (typeof k.wrapHeightInches === 'number') base.canvasHeightIn = k.wrapHeightInches;
        return base;
    }

    function drawHingeZone(ctx, x, y, w, h, frontImg, side) {
        if (w <= 0) return;
        var colors = frontImg ? sampleEdgeGradient(ctx, frontImg, side === 'left' ? 'right' : 'left') : ['#4c4688', '#312e81'];
        var g = ctx.createLinearGradient(x, 0, x + w, 0);
        g.addColorStop(0, colors[1]);
        g.addColorStop(1, colors[0]);
        ctx.fillStyle = g;
        ctx.fillRect(x, y, w, h);
        ctx.fillStyle = 'rgba(0,0,0,0.08)';
        ctx.fillRect(x, y, w, h);
    }

    function sampleEdgeGradient(ctx2d, img, edge) {
        var sw = Math.max(4, Math.min(32, Math.floor(img.width * 0.06)));
        var sx = edge === 'right' ? img.width - sw : (edge === 'left' ? 0 : Math.floor((img.width - sw) / 2));
        var tmp = document.createElement('canvas');
        tmp.width = sw;
        tmp.height = img.height;
        var tctx = tmp.getContext('2d');
        tctx.drawImage(img, sx, 0, sw, img.height, 0, 0, sw, img.height);
        var data = tctx.getImageData(0, 0, sw, img.height).data;
        var r = 0, g = 0, b = 0, n = 0;
        for (var i = 0; i < data.length; i += 4) {
            r += data[i];
            g += data[i + 1];
            b += data[i + 2];
            n++;
        }
        if (!n) return ['#312e81', '#1e1b4b'];
        r = Math.round(r / n);
        g = Math.round(g / n);
        b = Math.round(b / n);
        var c1 = 'rgb(' + r + ',' + g + ',' + b + ')';
        var c2 = 'rgb(' + Math.max(0, r - 32) + ',' + Math.max(0, g - 32) + ',' + Math.max(0, b - 32) + ')';
        return [c1, c2];
    }

    function fillPanelFromFrontPalette(ctx, x, y, w, h, frontImg, edge) {
        var colors = frontImg && frontImg.width > 0
            ? sampleEdgeGradient(ctx, frontImg, edge)
            : ['#3730a3', '#1e1b4b'];
        var g = ctx.createLinearGradient(x, y, x + w, y + h);
        g.addColorStop(0, colors[0]);
        g.addColorStop(0.55, colors[1]);
        g.addColorStop(1, colors[0]);
        ctx.fillStyle = g;
        ctx.fillRect(x, y, w, h);
    }

    /**
     * Draw only a center vertical crop into the spine slot — never stretch a wide API spine/wrap asset.
     */
    function drawSpineCropCenter(ctx, img, x, y, w, h, layout) {
        if (!img || !img.width || w < 1 || h < 1) return false;
        var srcW = img.width;
        var srcH = img.height;
        var spineFrac = layout && layout.canvasWidthIn > 0
            ? Math.min(0.22, Math.max(0.02, layout.spineIn / layout.canvasWidthIn))
            : 0.06;
        var cropW = Math.max(2, Math.round(srcW * spineFrac));
        cropW = Math.min(cropW, Math.round(srcW * 0.2), srcW);
        var cropX = Math.max(0, Math.floor((srcW - cropW) / 2));
        var cropY = 0;
        var cropH = srcH;
        if (layout && layout.canvasHeightIn > 0 && layout.panelHeightIn > 0) {
            var topFrac = (layout.panelTopYIn != null ? layout.panelTopYIn : layout.outerMarginIn || 0) / layout.canvasHeightIn;
            var heightFrac = layout.panelHeightIn / layout.canvasHeightIn;
            cropY = Math.max(0, Math.round(topFrac * srcH));
            cropH = Math.max(1, Math.round(heightFrac * srcH));
            cropH = Math.min(cropH, srcH - cropY);
        }
        ctx.drawImage(img, cropX, cropY, cropW, cropH, x, y, w, h);
        return true;
    }

    function drawSpineTitle(ctx, x, y, w, h, title) {
        if (w < 10) return;
        ctx.save();
        ctx.beginPath();
        ctx.rect(x, y, w, h);
        ctx.clip();
        ctx.translate(x + w / 2, y + h / 2);
        ctx.rotate(-Math.PI / 2);
        ctx.fillStyle = 'rgba(248,250,252,0.95)';
        ctx.font = 'bold ' + Math.max(8, Math.min(w * 0.85, 13)) + 'px Inter, system-ui, sans-serif';
        ctx.shadowColor = 'rgba(0,0,0,0.45)';
        ctx.shadowBlur = 3;
        var t = (title || 'Title').toUpperCase();
        if (t.length > 48) t = t.slice(0, 45) + '…';
        var tw = ctx.measureText(t).width;
        ctx.fillText(t, -tw / 2, 4);
        ctx.restore();
    }

    function drawSpinePanel(ctx, x, y, w, h, title, frontImg, apiSpineImg, layout) {
        fillPanelFromFrontPalette(ctx, x, y, w, h, frontImg, 'right');
        if (apiSpineImg && apiSpineImg.width > 0 && w >= 3) {
            ctx.save();
            ctx.beginPath();
            ctx.rect(x, y, w, h);
            ctx.clip();
            ctx.globalAlpha = 0.82;
            drawSpineCropCenter(ctx, apiSpineImg, x, y, w, h, layout);
            ctx.restore();
        }
        drawSpineTitle(ctx, x, y, w, h, title);
    }

    function drawBackCropCenter(ctx, img, x, y, w, h, layout) {
        if (!img || !img.width) return false;
        var panelFrac = layout && layout.canvasWidthIn > 0
            ? Math.min(0.48, layout.panelWidthIn / layout.canvasWidthIn)
            : 0.4;
        var cropW = Math.max(2, Math.round(img.width * panelFrac));
        cropW = Math.min(cropW, Math.round(img.width * 0.5), img.width);
        var cropX = img.width > cropW ? Math.floor((img.width - cropW) * 0.12) : 0;
        ctx.drawImage(img, cropX, 0, cropW, img.height, x, y, w, h);
        return true;
    }

    function drawBackPanelFromFront(ctx, x, y, w, h, frontImg, meta, apiBackImg, layout) {
        fillPanelFromFrontPalette(ctx, x, y, w, h, frontImg, 'left');
        if (frontImg && frontImg.width > 0) {
            ctx.save();
            ctx.globalAlpha = 0.42;
            drawCoverContain(ctx, frontImg, x, y, w, h);
            ctx.restore();
        }
        if (apiBackImg && apiBackImg.width > 0) {
            ctx.save();
            ctx.beginPath();
            ctx.rect(x, y, w, h);
            ctx.clip();
            ctx.globalAlpha = 0.55;
            if (!drawBackCropCenter(ctx, apiBackImg, x, y, w, h, layout)) {
                drawCoverContain(ctx, apiBackImg, x, y, w, h);
            }
            ctx.restore();
        } else if (!frontImg || !frontImg.width) {
            drawBackPanel(ctx, x, y, w, h, meta);
            return;
        }
        var pad = Math.max(12, w * 0.06);
        var title = meta.title || 'Title';
        var author = meta.author || '';
        var blurb = (meta.synopsis || '').trim() || 'Your story continues on the back cover with the same premium design language as the front.';
        if (blurb.length > 520) blurb = blurb.slice(0, 517) + '…';
        ctx.fillStyle = '#f8fafc';
        ctx.font = 'bold ' + Math.max(14, w * 0.045) + 'px Inter, Segoe UI, system-ui, sans-serif';
        ctx.fillText(title.length > 42 ? title.slice(0, 39) + '…' : title, x + pad, y + pad + 18);
        ctx.fillStyle = '#c4b5fd';
        ctx.font = Math.max(11, w * 0.028) + 'px Inter, Segoe UI, system-ui, sans-serif';
        if (author) ctx.fillText(author, x + pad, y + pad + 38);
        ctx.fillStyle = '#e2e8f0';
        ctx.font = Math.max(10, w * 0.024) + 'px Georgia, Merriweather, serif';
        wrapText(ctx, blurb, x + pad, y + pad + 68, w - pad * 2, Math.max(13, w * 0.032));
    }

    function buildWrapCanvasFromParts(frontImg, spineImg, backImg, layout, meta, previewDpi) {
        var dpi = previewDpi || 120;
        function inPx(v) { return Math.max(1, Math.round(v * dpi)); }

        var W = inPx(layout.canvasWidthIn);
        var H = inPx(layout.canvasHeightIn);
        var backX = inPx(layout.backPanelXIn);
        var backW = inPx(layout.panelWidthIn);
        var backY = inPx(layout.panelTopYIn);
        var panelH = inPx(layout.panelHeightIn);
        var hingeW = inPx(layout.hingeGapIn);
        var spineX = inPx(layout.spineXIn);
        var spineW = inPx(layout.spineIn);
        var hingeRightX = inPx(layout.hingeRightXIn);
        var frontX = inPx(layout.frontPanelXIn);
        var frontW = inPx(layout.panelWidthIn);

        var c = document.createElement('canvas');
        c.width = W;
        c.height = H;
        var ctx = c.getContext('2d');
        ctx.fillStyle = '#1a1630';
        ctx.fillRect(0, 0, W, H);

        if (layout.outerMarginIn > 0) {
            ctx.fillStyle = '#2d2852';
            ctx.fillRect(0, 0, W, H);
        }

        drawBackPanelFromFront(ctx, backX, backY, backW, panelH, frontImg, meta, backImg, layout);
        if (hingeW > 0) {
            drawHingeZone(ctx, inPx(layout.hingeLeftXIn), backY, hingeW, panelH, frontImg, 'left');
        }
        drawSpinePanel(ctx, spineX, backY, spineW, panelH, meta.title, frontImg, spineImg, layout);
        if (hingeW > 0) {
            drawHingeZone(ctx, hingeRightX, backY, hingeW, panelH, frontImg, 'right');
        }
        ctx.save();
        ctx.beginPath();
        ctx.rect(frontX, backY, frontW, panelH);
        ctx.clip();
        drawCoverFill(ctx, frontImg, frontX, backY, frontW, panelH);
        ctx.restore();

        c._kdpParts = {
            backX: backX, backY: backY, backW: backW, spineX: spineX, spineW: spineW,
            frontX: frontX, frontW: frontW, panelH: panelH, H: H, dpi: dpi, layout: layout
        };
        return c;
    }

    function formatLayoutDebug(layout, pageCount, dpi) {
        if (!layout) return '';
        dpi = dpi || 150;
        var sw = layout.spineIn || 0;
        var swPx = Math.max(1, Math.round(sw * dpi));
        var tw = layout.canvasWidthIn || 0;
        var th = layout.canvasHeightIn || 0;
        var twPx = Math.max(1, Math.round(tw * dpi));
        var thPx = Math.max(1, Math.round(th * dpi));
        var ph = layout.panelHeightIn || 9;
        var phPx = Math.max(1, Math.round(ph * dpi));
        return 'pages=' + (pageCount || layout.pageCount || '?')
            + ' | spine=' + sw.toFixed(4) + ' in (' + swPx + ' px @ ' + dpi + ' DPI)'
            + ' | panel H=' + ph.toFixed(3) + ' in (' + phPx + ' px)'
            + ' | total=' + tw.toFixed(3) + '×' + th.toFixed(3) + ' in (' + twPx + '×' + thPx + ' px)';
    }

    function extractPanelsFromCanvas(canvas) {
        var p = canvas && canvas._kdpParts;
        if (!p) return null;
        var backY = p.backY || 0;
        function slice(sx, sy, sw, sh) {
            var c = document.createElement('canvas');
            c.width = Math.max(1, sw);
            c.height = Math.max(1, sh);
            c.getContext('2d').drawImage(canvas, sx, sy, sw, sh, 0, 0, sw, sh);
            return c.toDataURL('image/png');
        }
        return {
            back: slice(p.backX, backY, p.backW, p.panelH),
            spine: slice(p.spineX, backY, p.spineW, p.panelH),
            front: slice(p.frontX, backY, p.frontW, p.panelH)
        };
    }

    /**
     * Crop back / spine / front from a full wrap PNG using server KDP layout (inches).
     */
    function extractPanelsFromWrap(wrapUrl, layout) {
        if (!wrapUrl || !layout) return Promise.reject(new Error('wrap and layout required'));
        return loadImageElement(wrapUrl).then(function (img) {
            var layoutW = layout.canvasWidthIn;
            var layoutH = layout.canvasHeightIn;
            if (!layoutW || !layoutH) {
                var margin = layout.outerMarginIn || 0.125;
                var pw = layout.panelWidthIn || 6;
                var ph = layout.panelHeightIn || 9;
                layoutW = margin * 2 + pw * 2 + (layout.spineIn || 0.055);
                layoutH = margin * 2 + ph;
            }
            var refW = img.width;
            var refH = img.height;
            function cropDataUrl(xIn, yIn, wIn, hIn) {
                var x = Math.round((xIn / layoutW) * refW);
                var y = Math.round((yIn / layoutH) * refH);
                var w = Math.max(1, Math.round((wIn / layoutW) * refW));
                var h = Math.max(1, Math.round((hIn / layoutH) * refH));
                var c = document.createElement('canvas');
                c.width = w;
                c.height = h;
                c.getContext('2d').drawImage(img, x, y, w, h, 0, 0, w, h);
                return c.toDataURL('image/png');
            }
            var y = layout.panelTopYIn != null ? layout.panelTopYIn : (layout.outerMarginIn || 0.125);
            var pw = layout.panelWidthIn || 6;
            var ph = layout.panelHeightIn || 9;
            var backX = layout.backPanelXIn != null ? layout.backPanelXIn : (layout.outerMarginIn || 0.125);
            var spineX = layout.spineXIn != null ? layout.spineXIn : backX + pw;
            var frontX = layout.frontPanelXIn != null ? layout.frontPanelXIn : spineX + (layout.spineIn || 0.055);
            return {
                back: cropDataUrl(backX, y, pw, ph),
                spine: cropDataUrl(spineX, y, layout.spineIn, ph),
                front: cropDataUrl(frontX, y, pw, ph),
                wrap: wrapUrl
            };
        });
    }

    function applyWrapPartsGridLayout(layout, dpi) {
        var el = document.querySelector('.cov-wrap-parts');
        if (!el || !layout) return;
        var pw = layout.panelWidthIn || 6;
        var sw = Math.max(0.055, layout.spineIn || 0.055);
        var ph = layout.panelHeightIn || 9;
        el.style.gridTemplateColumns = pw + 'fr ' + sw + 'fr ' + pw + 'fr';
        el.style.setProperty('--cov-panel-aspect', pw + ' / ' + ph);
        el.style.setProperty('--cov-spine-aspect', sw + ' / ' + ph);
        var wrapFull = document.querySelector('.cov-wrap-full');
        if (wrapFull && layout.canvasWidthIn && layout.canvasHeightIn) {
            wrapFull.style.setProperty('--cov-wrap-aspect', layout.canvasWidthIn + ' / ' + layout.canvasHeightIn);
        }
        var debugEl = document.getElementById('covWrapKdpDebug');
        if (debugEl) {
            debugEl.textContent = formatLayoutDebug(layout, layout.pageCount, dpi || 150);
        }
    }

    function sampleEdgeColorRect(ctx, x, y, w, h) {
        try {
            var d = ctx.getImageData(x, y, Math.max(1, w), Math.max(1, h)).data;
            var r = 0, g = 0, b = 0, n = 0;
            for (var i = 0; i < d.length; i += 4) { r += d[i]; g += d[i + 1]; b += d[i + 2]; n++; }
            return 'rgb(' + Math.round(r / n) + ',' + Math.round(g / n) + ',' + Math.round(b / n) + ')';
        } catch (e) { return '#141414'; }
    }

    function drawSpineTextVertical(ctx, x, w, h, text, color) {
        ctx.save();
        ctx.translate(x + w / 2, h / 2);
        ctx.rotate(Math.PI / 2);
        ctx.fillStyle = color;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.font = '600 ' + Math.max(10, Math.floor(w * 0.5)) + 'px Georgia, serif';
        ctx.fillText(String(text), 0, 0);
        ctx.restore();
    }

    /**
     * True when image aspect ratio matches a full KDP wrap (back + spine + front).
     */
    function isLikelyFullWrapImage(img, fullWIn, fullHIn) {
        if (!img || !img.width || !img.height || fullHIn <= 0) return false;
        var wrapAr = fullWIn / fullHIn;
        var imgAr = img.width / img.height;
        return imgAr > 1.15 && Math.abs(imgAr - wrapAr) / wrapAr < 0.14;
    }

    /**
     * Solid panel filled from front cover edge colors — no image copy, no mirror (avoids overlap/duplicate text).
     */
    function drawSolidPanelFromFrontEdge(ctx, img, x, y, w, h, edge) {
        fillPanelFromFrontPalette(ctx, x, y, w, h, img, edge || 'left');
    }

    /**
     * Re-slice an existing wrap PNG into exact KDP panel widths for the real page count.
     * Keeps back art from the left and front art from the right; rebuilds spine in the middle.
     */
    function drawReslicedWrapToCanvas(ctx, wrapImg, backW, spineW, frontW, H) {
        var srcW = wrapImg.width;
        var srcH = wrapImg.height;
        var totalW = backW + spineW + frontW;
        if (totalW < 1 || srcW < 1) return;

        var srcBackW = Math.max(1, Math.round(srcW * (backW / totalW)));
        var srcFrontW = Math.max(1, Math.round(srcW * (frontW / totalW)));
        if (srcBackW + srcFrontW > srcW) srcFrontW = Math.max(1, srcW - srcBackW);

        ctx.drawImage(wrapImg, 0, 0, srcBackW, srcH, 0, 0, backW, H);

        var spineColors = sampleEdgeGradient(ctx, wrapImg, 'left');
        var spineG = ctx.createLinearGradient(backW, 0, backW + spineW, 0);
        spineG.addColorStop(0, spineColors[1]);
        spineG.addColorStop(0.5, spineColors[0]);
        spineG.addColorStop(1, spineColors[0]);
        ctx.fillStyle = spineG;
        ctx.fillRect(backW, 0, spineW, H);

        var seamX = srcW - srcFrontW;
        ctx.drawImage(wrapImg, seamX, 0, srcFrontW, srcH, backW + spineW, 0, frontW, H);
    }

    /**
     * KDP paperback layout: [back | spine | front] with bleed on outer edges.
     * Front-only source → solid back + solid spine (same palette) + front panel (clipped).
     * Full-wrap source → re-sliced to exact spine width for page count.
     */
    function composePrintWrapFromParts(frontUrl, spineUrl, backUrl, opts) {
        opts = opts || {};

        var pages = parseInt(opts.pageCount, 10);
        if (!Number.isFinite(pages) || pages <= 0) {
            return Promise.reject(new Error('composePrintWrapFromParts: real pageCount is required (got ' + opts.pageCount + ').'));
        }

        var MULT = { 'White paper': 0.002252, 'Cream paper': 0.0025, 'Color paper': 0.002347, 'Standard color': 0.002347 };
        var mult = MULT[opts.paperType] || 0.002252;
        var TRIM = { '6x9': { w: 6, h: 9 }, '5.5x8.5': { w: 5.5, h: 8.5 }, '5x8': { w: 5, h: 8 }, '7x10': { w: 7, h: 10 }, '8.5x11': { w: 8.5, h: 11 } };
        var trim = TRIM[String(opts.trimKey || '6x9').toLowerCase().replace(/\s/g, '')] || { w: 6, h: 9 };
        var bleed = 0.125;

        var spineIn = pages * mult;
        var fullW = trim.w * 2 + spineIn + bleed * 2;
        var fullH = trim.h + bleed * 2;

        var dpi = opts.dpi || 300;
        var px = function (inch) { return Math.round(inch * dpi); };

        var canvas = document.createElement('canvas');
        canvas.width = px(fullW);
        canvas.height = px(fullH);
        var ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        var backW = px(bleed + trim.w);
        var spineW = Math.max(1, px(spineIn));
        var frontW = Math.max(1, canvas.width - backW - spineW);
        var H = canvas.height;

        var rtl = opts.readingDirection === 'RightToLeft';
        var backX, spineX, frontX;
        if (rtl) {
            frontX = 0;
            spineX = frontW;
            backX = frontW + spineW;
        } else {
            backX = 0;
            spineX = backW;
            frontX = backW + spineW;
        }

        var useSpineArt = opts.useApiSpineArt === true && spineUrl;
        var useBackArt = opts.useApiBackArt === true && backUrl;

        return Promise.all([
            loadImageElement(frontUrl).catch(function () { return null; }),
            useBackArt ? loadImageElement(backUrl).catch(function () { return null; }) : Promise.resolve(null),
            useSpineArt ? loadImageElement(spineUrl).catch(function () { return null; }) : Promise.resolve(null)
        ]).then(function (imgs) {
            var sourceImg = imgs[0];
            var backImg = imgs[1];
            var spineImg = imgs[2];
            if (!sourceImg) throw new Error('composePrintWrapFromParts: cover image failed to load.');

            if (isLikelyFullWrapImage(sourceImg, fullW, fullH)) {
                drawReslicedWrapToCanvas(ctx, sourceImg, backW, spineW, frontW, H);
                if (pages >= 79 && opts.title && spineW >= 10) {
                    drawSpineTitle(ctx, spineX, 0, spineW, H, opts.title);
                }
            } else {
                if (useBackArt && backImg && backImg.width > 0) {
                    ctx.save();
                    ctx.beginPath();
                    ctx.rect(backX, 0, backW, H);
                    ctx.clip();
                    drawCoverFill(ctx, backImg, backX, 0, backW, H);
                    ctx.restore();
                } else {
                    drawSolidPanelFromFrontEdge(ctx, sourceImg, backX, 0, backW, H, 'left');
                }

                if (useSpineArt && spineImg && spineImg.width > 0) {
                    ctx.save();
                    ctx.beginPath();
                    ctx.rect(spineX, 0, spineW, H);
                    ctx.clip();
                    drawCoverFill(ctx, spineImg, spineX, 0, spineW, H);
                    ctx.restore();
                } else {
                    drawSolidPanelFromFrontEdge(ctx, sourceImg, spineX, 0, spineW, H, 'left');
                }
                if (pages >= 79 && opts.title && spineW >= 10) {
                    drawSpineTitle(ctx, spineX, 0, spineW, H, opts.title);
                }

                ctx.save();
                ctx.beginPath();
                ctx.rect(frontX, 0, frontW, H);
                ctx.clip();
                drawCoverFill(ctx, sourceImg, frontX, 0, frontW, H);
                ctx.restore();
            }

            return {
                dataUrl: canvas.toDataURL('image/png'),
                pageCount: pages,
                spineInches: spineIn,
                widthPx: canvas.width,
                heightPx: canvas.height,
                fullWidthIn: fullW,
                fullHeightIn: fullH,
                previewDpi: dpi,
                debugLabel: 'pages=' + pages + ' | spine=' + spineIn.toFixed(4) + ' in (' + spineW + ' px @ ' + dpi + ' DPI) | total=' + fullW.toFixed(3) + '×' + fullH.toFixed(3) + ' in (' + canvas.width + '×' + canvas.height + ' px)'
            };
        });
    }

    function persistComposedWrap(bookId, dataUrl, meta) {
        if (!bookId || !dataUrl) return Promise.resolve(null);
        return fetch('/Dashboard/SavePrintReadyComposedWrap', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify({
                bookId: bookId,
                wrapImageDataUrl: dataUrl,
                pageCount: meta && meta.pageCount,
                spineInches: meta && meta.spineInches
            })
        }).then(function (r) { return r.json(); });
    }

    global.CoverKdpExport = {
        openModal: openModal,
        openCalculatorModal: openCalculatorModal,
        EBOOK_COVER: EBOOK_COVER,
        printLayoutInches: printLayoutInches,
        estimateSpineInches: estimateSpineInches,
        computeKdpDimensionsMm: computeKdpDimensionsMm,
        composePrintWrapFromParts: composePrintWrapFromParts,
        persistComposedWrap: persistComposedWrap,
        extractPanelsFromWrap: extractPanelsFromWrap,
        extractPanelsFromCanvas: extractPanelsFromCanvas,
        applyWrapPartsGridLayout: applyWrapPartsGridLayout,
        formatLayoutDebug: formatLayoutDebug,
        trimToMmString: trimToMmString,
        computeCoverLayout: computeCoverLayout,
        layoutFromServerKdp: layoutFromServerKdp,
        CASE_WRAP_MARGIN_IN: CASE_WRAP_MARGIN_IN,
        CASE_HINGE_GAP_IN: CASE_HINGE_GAP_IN
    };
})(typeof window !== 'undefined' ? window : this);
