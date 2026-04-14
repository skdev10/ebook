/**
 * Cover export: EPUB, print PNGs (back / spine / front / wrap), ZIP + manifest, PDF hook.
 * Depends: SweetAlert2, cover-download-modal.js (CoverDownloadModal), JSZip, optional html2canvas.
 */
(function (global) {
    'use strict';

    var EBOOK_COVER = { w: 1600, h: 2560 }; // KDP: min 1000px shortest side; 2560px longest recommended
    var BLEED_IN = 0.125;
    var PRINT_DPI = 300;

    var TRIM_SIZES = {
        '5x8': { w: 5, h: 8 },
        '5.25x8': { w: 5.25, h: 8 },
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

    /** Spine estimate (inches) — aligns with common KDP white/cream approximations; verify in KDP calculator. */
    function estimateSpineInches(pageCount, paperType, interiorBw) {
        var p = Math.max(24, Math.min(828, parseInt(pageCount, 10) || 200));
        var mult = 0.002252;
        if (paperType === 'cream') mult = 0.0025;
        if (paperType === 'color' || interiorBw === false) mult = 0.002347;
        var sp = p * mult;
        return Math.max(0.055, sp);
    }

    function parseTrim(trimKey) {
        var t = TRIM_SIZES[trimKey] || TRIM_SIZES['6x9'];
        return { w: t.w, h: t.h };
    }

    function printLayoutInches(trimKey, pageCount, paperType, interiorBw, savedSpine) {
        var trim = parseTrim(trimKey);
        var spine = savedSpine > 0 ? savedSpine : estimateSpineInches(pageCount, paperType, interiorBw !== false);
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
                reject(new Error('No image'));
                return;
            }
            var img = new Image();
            img.onload = function () { resolve(img); };
            img.onerror = function () { reject(new Error('Image failed to load')); };
            if (src.indexOf('data:') !== 0 && src.indexOf('blob:') !== 0) {
                try {
                    if (src.indexOf('/') === 0 || src.indexOf(window.location.origin) === 0) {
                        img.crossOrigin = 'anonymous';
                    }
                } catch (e) { /* ignore */ }
            }
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

    /** KDP-style dimension model (mm) — aligned with Amazon print cover calculator logic. */
    function computeKdpDimensionsMm(binding, interior, paper, trimStr, pages) {
        var raw = String(trimStr || '').split(/[x×]/i);
        var trimW = parseFloat((raw[0] || '152.4').trim()) || 152.4;
        var trimH = parseFloat((raw[1] || '228.6').trim()) || 228.6;
        var p = Math.max(24, Math.min(828, parseInt(pages, 10) || 100));

        var thickness = 0.0572;
        if (interior === 'Black & white' && paper === 'Cream paper') thickness = 0.0635;
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
            paper: paper,
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

            ['_binding', '_interior', '_paper', '_direction', '_units', '_trim', '_pages'].forEach(function (suf) {
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
            lastComposite: null
        };

        var meta = { title: title, author: author, synopsis: synopsis };
        var base = sanitizeFilename(title);

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
            if (!calc || !state.frontImg) {
                var cf = root.querySelector('#dbkCompositeThumb');
                if (cf) cf.src = '';
                state.lastComposite = null;
                return;
            }
            try {
                var canvas = buildPrintCanvasFromCalc(state.frontImg, calc, meta);
                state.lastComposite = canvas;
                var url = canvas.toDataURL('image/png');
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
                    return Promise.reject(new Error('No cover'));
                }

                resolveFrontImage().catch(function () {
                    var im = root.querySelector('#dbkImgFront');
                    if (im) im.src = '';
                    refreshCoverUi(root);
                });

                function requireCover() {
                    if (!state.frontImg) {
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
                    if (!calc || !state.frontImg) return null;
                    return buildPrintCanvasFromCalc(state.frontImg, calc, meta);
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
                    var calc = requireCalcForExport();
                    if (!calc) return;
                    var cv = buildPrintCanvasFromCalc(state.frontImg, calc, meta);
                    if (!cv) return;
                    cv.toBlob(function (blob) {
                        if (blob) triggerDownload(blob, base + '-print-full-wrap.png');
                    }, 'image/png');
                });

                function downloadSlice(which) {
                    if (!requireCover()) return;
                    var calc = requireCalcForExport();
                    if (!calc) return;
                    var cv = buildPrintCanvasFromCalc(state.frontImg, calc, meta);
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
                    var cv = buildPrintCanvasFromCalc(state.frontImg, calc, meta);
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


    global.CoverKdpExport = {
        openModal: openModal,
        openCalculatorModal: openCalculatorModal,
        EBOOK_COVER: EBOOK_COVER,
        printLayoutInches: printLayoutInches,
        estimateSpineInches: estimateSpineInches,
        computeKdpDimensionsMm: computeKdpDimensionsMm
    };
})(typeof window !== 'undefined' ? window : this);
