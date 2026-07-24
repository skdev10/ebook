/**
 * Shared module export helper — fetch file endpoints and trigger browser download.
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

    global.ModuleExport = {
        postExport: postExport,
        getExport: getExport,
        downloadBlob: downloadBlob
    };
})(typeof window !== 'undefined' ? window : globalThis);
