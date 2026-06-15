/**
 * Central unsaved-changes guard for AI Writer, Formatter, and Cover workflow pages.
 * - beforeunload: native browser prompt only when edits are not confirmed saved on server.
 * - In-app links: SweetAlert Stay / Save first / Leave without saving.
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

    function beforeunloadHandler(event) {
        if (!dirty) return;
        event.preventDefault();
        event.returnValue = '';
    }

    function sameOriginNavigableHref(href) {
        if (!href || href === '#' || href.indexOf('javascript:') === 0) return false;
        try {
            var u = new URL(href, global.location.href);
            if (u.origin !== global.location.origin) return false;
            if (u.pathname === global.location.pathname && u.search === global.location.search) return false;
            return true;
        } catch (e) {
            return false;
        }
    }

    function runPendingSave() {
        if (!pendingSaveFn) return Promise.resolve(false);
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

    function confirmLeave(targetHref) {
        if (!dirty) return Promise.resolve(true);

        if (typeof Swal === 'undefined') {
            var ok = global.confirm('Leave this page? Your current draft won\'t be saved.');
            if (ok) markSaved();
            return Promise.resolve(ok);
        }

        var buttons = {
            showCancelButton: true,
            confirmButtonText: 'Leave',
            cancelButtonText: 'Stay',
            confirmButtonColor: '#7c3aed',
            cancelButtonColor: '#64748b',
            allowOutsideClick: false
        };

        if (pendingSaveFn) {
            buttons.showDenyButton = true;
            buttons.denyButtonText = 'Save first';
            buttons.denyButtonColor = '#16a34a';
        }

        return Swal.fire(Object.assign({
            title: 'Leave this page?',
            html: 'Unsaved changes on this step may be <b>lost</b> if you leave without saving.<br><br>'
                + 'To stay safe, use <b>Dashboard → Continue Editing</b> for your book.',
            icon: 'warning'
        }, buttons)).then(function (result) {
            if (result.isDenied) {
                return runPendingSave().then(function (saved) {
                    if (saved) {
                        markSaved();
                        return true;
                    }
                    Swal.fire({
                        icon: 'error',
                        title: 'Could not save',
                        text: 'Please try again or stay on the page to keep editing.',
                        confirmButtonColor: '#7c3aed'
                    });
                    return false;
                });
            }
            if (result.isConfirmed) {
                markSaved();
                return true;
            }
            return false;
        });
    }

    function onDocumentClick(e) {
        if (!dirty) return;
        var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (!a) return;
        if (a.target === '_blank' || a.hasAttribute('download')) return;
        if (a.classList.contains('sidebar-locked-feature')) return;
        if (a.getAttribute('data-unsaved-ignore') === 'true') return;
        if (a.hasAttribute('data-flow-back')) return;

        var href = a.getAttribute('href') || '';
        if (!sameOriginNavigableHref(href)) return;

        e.preventDefault();
        e.stopPropagation();
        confirmLeave(href).then(function (ok) {
            if (ok) global.location.href = href;
        });
    }

    function init() {
        if (initialized) return;
        initialized = true;
        global.addEventListener('beforeunload', beforeunloadHandler);
        document.addEventListener('click', onDocumentClick, true);
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
