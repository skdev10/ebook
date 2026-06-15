/**
 * Central client-side cleanup for eBook editor draft resets.
 * Pairs with POST /Dashboard/ResetEditorDraft and /Dashboard/ResetFlowStep.
 */
(function (global) {
    'use strict';

    var CONFIRM_TITLE = 'Keep writing or reset your work?';
    var CONFIRM_HTML = 'Resetting clears drafts, formatting choices, and cover previews for this book. '
        + '<b>This work can be deleted and cannot be undone.</b><br><br>'
        + 'To continue your current book safely, use <b>Dashboard → Continue Editing</b> instead.';
    var FORMATTER_BOOK_KEY = 'ebook_formatter_book_id';
    var DASH_BOOK_KEY = 'ebook_dashboard_selected_book';

    function parseBookId(value) {
        var n = parseInt(value, 10);
        return Number.isFinite(n) && n > 0 ? n : 0;
    }

    /** Removes all known per-book workflow keys from localStorage and sessionStorage. */
    function clearAllClientState(bookId) {
        var bid = parseBookId(bookId);
        try {
            if (bid > 0) {
                localStorage.removeItem('formatter_draft_v2_' + bid);
                localStorage.removeItem('ebook_ai_reader_state_v2_' + bid);
                localStorage.removeItem('ebook_ai_reader_chapter_' + bid);
                localStorage.removeItem('ebook_reader_pos_' + bid);
                sessionStorage.removeItem('formatter_book_payload_v1_' + bid);
                sessionStorage.removeItem('coverDesign_selectedUrl_' + bid);
                sessionStorage.removeItem('cover_design_draft_' + bid);
                sessionStorage.removeItem('dbk_calc_v1_' + bid);
            }
            sessionStorage.removeItem('coverDesign_selectedUrl');
            sessionStorage.removeItem('coverDesign_hasGenerated');
            sessionStorage.removeItem('coverDesign_lastBookId');
            sessionStorage.removeItem('cover_design_books_cache_v1');
            sessionStorage.removeItem(FORMATTER_BOOK_KEY);
            sessionStorage.removeItem(DASH_BOOK_KEY);
            sessionStorage.removeItem('formatter_format_premium');
            localStorage.removeItem('printready_style_recent_v1');
            patchCoverBooksCache(bid);
        } catch (_) { /* storage may be blocked */ }
        if (global.EbookUnsavedGuard && typeof global.EbookUnsavedGuard.markSaved === 'function') {
            global.EbookUnsavedGuard.markSaved();
        }
    }

    function patchCoverBooksCache(bookId) {
        if (!bookId) return;
        try {
            var raw = sessionStorage.getItem('cover_design_books_cache_v1');
            if (!raw) return;
            var cache = JSON.parse(raw);
            if (!cache || !Array.isArray(cache.books)) return;
            cache.books = cache.books.map(function (b) {
                if (b && (b.bookId === bookId || b.BookId === bookId)) {
                    b.coverImagePath = '';
                    b.aiCoverLastPreview = '';
                }
                return b;
            });
            sessionStorage.setItem('cover_design_books_cache_v1', JSON.stringify(cache));
        } catch (_) { /* no-op */ }
    }

    function hardNavigate(url) {
        var target = url || '/Dashboard';
        try {
            if ('caches' in global) {
                caches.keys().then(function (keys) { keys.forEach(function (k) { caches.delete(k); }); });
            }
        } catch (_) { /* no-op */ }
        global.location.replace(target + (target.indexOf('?') >= 0 ? '&' : '?') + '_r=' + Date.now());
    }

    function postReset(payload) {
        return fetch('/Dashboard/ResetEditorDraft', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify(payload)
        }).then(function (res) {
            return res.json().then(function (j) {
                if (!res.ok || !j || !j.success) {
                    throw new Error((j && j.message) ? j.message : 'Reset failed.');
                }
                return j;
            });
        });
    }

    function postLegacyFlowReset(payload) {
        return fetch('/Dashboard/ResetFlowStep', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify(payload)
        }).then(function (res) {
            return res.json().then(function (j) {
                if (!res.ok || !j || j.success === false) {
                    throw new Error((j && j.message) ? j.message : 'Reset failed.');
                }
                return j;
            });
        });
    }

    function guardUnsavedThenConfirm(customHtml) {
        return Promise.resolve(true);
    }

    function confirmReset(customHtml) {
        return Promise.resolve(true);
    }

    /**
     * Full project reset then navigate (start over / reset project).
     * @param {number} bookId
     * @param {string} [scope] FullProject | FullProjectWithChapters
     * @param {string} [redirectUrl]
     */
    function resetProject(bookId, scope, redirectUrl) {
        var bid = parseBookId(bookId);
        if (!bid) return Promise.resolve(false);
        var resetScope = scope || 'fullproject';
        return guardUnsavedThenConfirm().then(function (ok) {
            if (!ok) return false;
            return postReset({ bookId: bid, scope: resetScope }).then(function (j) {
                clearAllClientState(bid);
                hardNavigate(redirectUrl || j.resumeUrl || '/Books/AIGenerateBook?bookId=' + bid + '&fresh=1');
                return true;
            });
        });
    }

    /**
     * New book: wipe current project draft (if any) then open a fresh Untitled book.
     * @param {number} [currentBookId]
     */
    function startNewProject(currentBookId) {
        hardNavigate('/Dashboard/StartNewBook?writer=1');
        return Promise.resolve(true);
    }

    /**
     * Scoped reset via ResetEditorDraft (FullProject / FullProjectWithChapters).
     */
    function resetWithScope(bookId, scope, redirectUrl, currentStep) {
        var bid = parseBookId(bookId);
        if (!bid) return Promise.resolve(false);
        return postReset({
            bookId: bid,
            scope: scope || 'fullproject',
            currentStep: currentStep || null
        }).then(function (j) {
            clearAllClientState(bid);
            hardNavigate(redirectUrl || j.resumeUrl || '/Dashboard');
            return true;
        }).catch(function () {
            clearAllClientState(bid);
            hardNavigate(redirectUrl || '/Dashboard');
            return true;
        });
    }

    /**
     * Workflow back — uses scoped reset when options.scope is set, else legacy step API.
     */
    function resetFlowBack(bookId, step, targetUrl, options) {
        var bid = parseBookId(bookId);
        var opts = options || {};
        if (opts.scope) {
            return resetWithScope(bid, opts.scope, targetUrl, step);
        }
        var body = { bookId: bid, step: step || '' };
        if (opts.wipeAllWork) body.wipeAllWork = true;
        else body.destructiveBack = true;
        return postLegacyFlowReset(body).then(function (j) {
            clearAllClientState(bid);
            hardNavigate(targetUrl || j.resumeUrl || '/Dashboard');
            return true;
        }).catch(function () {
            clearAllClientState(bid);
            hardNavigate(targetUrl || '/Dashboard');
            return true;
        });
    }

    function wireNewProjectLinks() {
        document.querySelectorAll('[data-editor-reset-new]').forEach(function (el) {
            el.addEventListener('click', function (e) {
                e.preventDefault();
                var bid = parseBookId(el.getAttribute('data-book-id') || el.getAttribute('data-editor-book-id') || '0');
                startNewProject(bid).catch(function (err) {
                    if (typeof Swal !== 'undefined') {
                        Swal.fire({ icon: 'error', title: 'Reset failed', text: err && err.message ? err.message : 'Try again.' });
                    } else {
                        alert(err && err.message ? err.message : 'Reset failed.');
                    }
                });
            });
        });

    }

    global.EditorDraftReset = {
        CONFIRM_TITLE: CONFIRM_TITLE,
        CONFIRM_HTML: CONFIRM_HTML,
        clearAllClientState: clearAllClientState,
        confirmReset: confirmReset,
        resetProject: resetProject,
        startNewProject: startNewProject,
        resetWithScope: resetWithScope,
        resetFlowBack: resetFlowBack,
        hardNavigate: hardNavigate
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', wireNewProjectLinks);
    } else {
        wireNewProjectLinks();
    }
})(window);
