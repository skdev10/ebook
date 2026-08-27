/**
 * Shared module export helper — fetch file endpoints and trigger browser download.
 * Also exposes ModuleExport.run for header quick-actions (draft DOCX/PDF + book PrintPdf/EPUB).
 */
(function (global) {
    'use strict';

    function downloadBlob(blob, filename) {
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename || 'download';
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 1500);
    }

    function filenameFromDisposition(header, fallback) {
        if (!header) return fallback;
        var m = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(header);
        if (!m) return fallback;
        try { return decodeURIComponent(m[1].replace(/"/g, '').trim()); }
        catch (_) { return m[1].replace(/"/g, '').trim() || fallback; }
    }

    /**
     * POST JSON to an export endpoint; downloads the returned file.
     * @returns {Promise<{ok:boolean, message?:string}>}
     */
    async function postExport(url, body, fallbackName) {
        var res = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'Accept': '*/*'
            },
            body: JSON.stringify(body || {})
        });
        var ct = (res.headers.get('content-type') || '').toLowerCase();
        if (!res.ok) {
            var msg = 'Export failed.';
            if (ct.indexOf('json') >= 0) {
                try {
                    var j = await res.json();
                    if (j && j.checkoutUrl) {
                        window.location = j.checkoutUrl;
                        return { ok: false, message: j.message || 'Payment required' };
                    }
                    if (j && j.message) msg = j.message;
                } catch (_) { /* ignore */ }
            } else {
                try {
                    var t = await res.text();
                    if (t) msg = t.slice(0, 200);
                } catch (_) { /* ignore */ }
            }
            return { ok: false, message: msg };
        }
        var blob = await res.blob();
        if (!blob || blob.size < 16) {
            return { ok: false, message: 'Empty file returned.' };
        }
        var name = filenameFromDisposition(res.headers.get('content-disposition'), fallbackName || 'export.bin');
        downloadBlob(blob, name);
        return { ok: true };
    }

    /** GET binary download (cover assets). */
    async function getExport(url, fallbackName) {
        var res = await fetch(url, { method: 'GET', credentials: 'same-origin' });
        if (!res.ok) {
            var msg = 'Download failed.';
            var ctGet = (res.headers.get('content-type') || '').toLowerCase();
            if (ctGet.indexOf('json') >= 0) {
                try {
                    var j = await res.json();
                    if (j && j.checkoutUrl) {
                        window.location = j.checkoutUrl;
                        return { ok: false, message: j.message || 'Payment required' };
                    }
                    if (j && j.message) msg = j.message;
                } catch (_) { /* ignore */ }
                return { ok: false, message: msg };
            }
            try {
                var t = await res.text();
                if (t) msg = t.slice(0, 200);
            } catch (_) { /* ignore */ }
            return { ok: false, message: msg };
        }
        var blob = await res.blob();
        if (!blob || blob.size < 16) return { ok: false, message: 'Empty file returned.' };
        var name = filenameFromDisposition(res.headers.get('content-disposition'), fallbackName || 'download.bin');
        downloadBlob(blob, name);
        return { ok: true };
    }

    /**
     * Header quick-actions entry point.
     * @param {{ bookId:number, bookExport?:string, draftFormat?:string, status?:function }} opts
     */
    async function run(opts) {
        var o = opts || {};
        var bid = parseInt(o.bookId, 10) || 0;
        var status = typeof o.status === 'function' ? o.status : function () {};
        if (bid <= 0) {
            status('Open or create a book first.', false);
            return { ok: false, message: 'No book selected.' };
        }

        // Flush Formatting/Writer prefs so export matches on-screen edits.
        try {
            if (typeof global.fmtFlushFormattingForExport === 'function') {
                await global.fmtFlushFormattingForExport();
            } else if (global.EbookUnsavedGuard && typeof global.EbookUnsavedGuard.flushIfDirty === 'function') {
                global.EbookUnsavedGuard.flushIfDirty();
            }
        } catch (_) { /* non-blocking */ }

        var body = { bookId: bid };
        try {
            if (typeof global.getFormatterStateFromUi === 'function') {
                var st = global.getFormatterStateFromUi();
                if (st) {
                    body.interiorStyle = st.interiorStyle || st.style;
                    body.textSize = st.textSize;
                    body.lineSpacing = st.lineSpacing;
                    body.bookFormat = st.format;
                    body.previewAccent = st.previewAccent;
                    body.pageBackgroundColor = st.pageBackgroundColor;
                }
            }
        } catch (_) { /* optional */ }

        var bookFmt = (o.bookExport || '').trim();
        var draftFmt = (o.draftFormat || '').trim();
        status('Preparing download…', null);

        if (bookFmt) {
            var ext = /epub/i.test(bookFmt) ? '.epub' : '-print.pdf';
            var r1 = await postExport(
                '/Books/Export/' + bid + '?format=' + encodeURIComponent(bookFmt),
                body,
                'book-' + bid + ext);
            status(r1.ok ? 'Download started.' : (r1.message || 'Export failed.'), r1.ok);
            return r1;
        }

        if (draftFmt) {
            var r2 = await postExport(
                '/Books/ExportDraft/' + bid + '?format=' + encodeURIComponent(draftFmt),
                body,
                'draft-' + bid + (/pdf/i.test(draftFmt) ? '.pdf' : '.docx'));
            status(r2.ok ? 'Download started.' : (r2.message || 'Export failed.'), r2.ok);
            return r2;
        }

        status('Unknown export type.', false);
        return { ok: false, message: 'Unknown export type.' };
    }

    global.ModuleExport = {
        postExport: postExport,
        getExport: getExport,
        downloadBlob: downloadBlob,
        run: run
    };
})(typeof window !== 'undefined' ? window : globalThis);
