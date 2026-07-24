/**
 * Soft save helpers for Writer / Formatting / Cover.
 * Keeps setDirty / markSaved for callers, but does NOT block navigation —
 * users can leave freely; autosave still runs in the background.
 */
(function (global) {
    'use strict';

    var dirty = false;
    var contextLabel = 'this page';
    var pendingSaveFn = null;
    var initialized = false;

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

    /** Never blocks — navigate freely. Optionally kick a silent save in the background. */
    function confirmLeave(/* targetHref */) {
        if (dirty && pendingSaveFn) {
            runPendingSave().then(function (ok) {
                if (ok) markSaved();
            });
        } else {
            markSaved();
        }
        return Promise.resolve(true);
    }

    function init() {
        if (initialized) return;
        initialized = true;
        // No beforeunload / click intercept — keep the flow soft for authors.
    }

    global.EbookUnsavedGuard = {
        isDirty: isDirty,
        setDirty: setDirty,
        markSaved: markSaved,
        setContext: setContext,
        setPendingSaveHandler: setPendingSaveHandler,
        confirmLeave: confirmLeave,
        init: init
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})(window);
