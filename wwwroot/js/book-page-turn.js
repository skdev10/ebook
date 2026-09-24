/**
 * Real open-book page turn for Formatter + AI Writer previews.
 * Flips the active leaf (recto forward / verso back) while the next spread renders underneath.
 */
(function (global) {
    'use strict';

    var TURN_MS = 520;
    var busy = false;

    function prefersReducedMotion() {
        try {
            return !!(global.matchMedia && global.matchMedia('(prefers-reduced-motion: reduce)').matches);
        } catch (e) {
            return false;
        }
    }

    function isPhone() {
        if (global.BookInteriorPreview && typeof global.BookInteriorPreview.isPhonePreview === 'function') {
            return !!global.BookInteriorPreview.isPhonePreview();
        }
        try {
            return !!(global.matchMedia && global.matchMedia('(max-width: 767px)').matches);
        } catch (e2) {
            return false;
        }
    }

    /**
     * @param {HTMLElement} viewport #preview-content
     * @param {number} dir 1 = next, -1 = previous
     * @param {function} applyContent swaps the spread DOM (called under the flipping leaf)
     * @returns {Promise<void>}
     */
    function animate(viewport, dir, applyContent) {
        if (typeof applyContent !== 'function') {
            return Promise.resolve();
        }
        if (!viewport || busy || prefersReducedMotion() || isPhone() || !dir) {
            applyContent();
            return Promise.resolve();
        }

        var spread = viewport.querySelector('.book-open-spread');
        if (!spread) {
            applyContent();
            return Promise.resolve();
        }

        var forward = dir > 0;
        var face = spread.querySelector(forward ? '.book-open-page--recto' : '.book-open-page--verso');
        if (!face) {
            applyContent();
            return Promise.resolve();
        }

        // Host must survive viewport.innerHTML replacement — pin the leaf to the shell.
        var shell = viewport.closest('.book-page-preview-shell')
            || document.getElementById('paginatedReaderShell')
            || viewport.parentElement;
        if (!shell) {
            applyContent();
            return Promise.resolve();
        }

        busy = true;
        shell.classList.add('is-page-turning');

        var faceRect = face.getBoundingClientRect();
        var shellRect = shell.getBoundingClientRect();
        var ghost = face.cloneNode(true);
        ghost.classList.add('book-turn-leaf');
        ghost.classList.add(forward ? 'book-turn-leaf--forward' : 'book-turn-leaf--back');
        ghost.setAttribute('aria-hidden', 'true');
        ghost.style.position = 'absolute';
        ghost.style.top = (faceRect.top - shellRect.top) + 'px';
        ghost.style.left = (faceRect.left - shellRect.left) + 'px';
        ghost.style.width = faceRect.width + 'px';
        ghost.style.height = faceRect.height + 'px';
        ghost.style.margin = '0';
        ghost.style.zIndex = '20';
        ghost.style.pointerEvents = 'none';
        ghost.style.transformOrigin = forward ? 'left center' : 'right center';
        ghost.style.backfaceVisibility = 'hidden';
        ghost.style.webkitBackfaceVisibility = 'hidden';

        var hostPos = global.getComputedStyle(shell).position;
        if (hostPos === 'static') shell.style.position = 'relative';
        shell.appendChild(ghost);

        // New spread under the flipping leaf (ghost lives on the shell, so it survives).
        try { applyContent(); } catch (err) { /* keep turn going */ }

        return new Promise(function (resolve) {
            var done = false;
            function finish() {
                if (done) return;
                done = true;
                if (ghost && ghost.parentNode) ghost.parentNode.removeChild(ghost);
                shell.classList.remove('is-page-turning');
                busy = false;
                resolve();
            }

            void ghost.offsetWidth;
            ghost.classList.add(forward ? 'is-flipping-forward' : 'is-flipping-back');

            var t = global.setTimeout(finish, TURN_MS + 40);
            ghost.addEventListener('animationend', function onEnd() {
                ghost.removeEventListener('animationend', onEnd);
                global.clearTimeout(t);
                finish();
            });
        });
    }

    function isBusy() { return busy; }

    global.BookPageTurn = {
        animate: animate,
        isBusy: isBusy,
        durationMs: TURN_MS
    };
})(typeof window !== 'undefined' ? window : this);
