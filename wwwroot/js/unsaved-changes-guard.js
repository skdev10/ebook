/**
 * Soft save helpers for Writer / Formatting / Cover.
 * Keeps setDirty / markSaved for callers, but does NOT block navigation —
 * users can leave freely; autosave flushes in the background on leave.
 *
 * Two phases:
 *  1) Start-to-finish flow (Writer → Format → Cover → Publish) — pending save flushes on leave.
 *  2) Mid-entry (open Cover/Format with no book) — local/server save still runs; never blocks.
 */
(function (global) {
    'use strict';

    var dirty = false;
    var contextLabel = 'this page';
    var pendingSaveFn = null;
    var initialized = false;
    var flushInFlight = false;

    function isDirty() {
        return dirty;
    }

    function setDirty() {
        dirty = true;
    }

    function markSaved() {
        dirty = false;
    }

    function setContext(label) {
        contextLabel = (label && String(label).trim()) || 'this page';
    }

    function setPendingSaveHandler(fn) {
        pendingSaveFn = typeof fn === 'function' ? fn : null;
    }

    function runPendingSave() {
        if (!pendingSaveFn) return Promise.resolve(true);
        try {
            var result = pendingSaveFn();
            if (result && typeof result.then === 'function') {
                return result.then(function (ok) { return !!ok; }).catch(function () { return false; });
            }
            return Promise.resolve(!!result);
        } catch (e) {
            return Promise.resolve(false);
        }
    }

    function flushIfDirty() {
        if (!dirty || !pendingSaveFn || flushInFlight) return;
        flushInFlight = true;
        runPendingSave().then(function (ok) {
            if (ok) markSaved();
        }).finally(function () {
            flushInFlight = false;
        });
    }

    /** Never blocks — navigate freely. Optionally kick a silent save in the background. */
    function confirmLeave(/* targetHref */) {
        flushIfDirty();
        return Promise.resolve(true);
    }

    function init() {
        if (initialized) return;
        initialized = true;
        // Flush on tab hide / page leave — no confirm dialog (soft modules).
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'hidden') flushIfDirty();
        });
        global.addEventListener('pagehide', flushIfDirty);
        // Soft beacon only — do not cancel navigation.
        global.addEventListener('beforeunload', function () {
            flushIfDirty();
        });
    }

    global.EbookUnsavedGuard = {
        isDirty: isDirty,
        setDirty: setDirty,
        markSaved: markSaved,
        setContext: setContext,
        setPendingSaveHandler: setPendingSaveHandler,
        confirmLeave: confirmLeave,
        flushIfDirty: flushIfDirty,
        init: init
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})(window);
