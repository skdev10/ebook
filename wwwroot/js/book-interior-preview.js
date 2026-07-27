/**
 * Web preview parity with print PDF: fit-to-width 6×9 layout, loading shell, page preload hooks.
 * Pairs with book-page-preview.css and InteriorLayoutTokens.
 */
(function (global) {
    'use strict';

    var DEFAULT_RATIO = 6 / 9;
    var DEFAULT_WIDTH_FACTOR = 0.995;
    var LOADING_OVERLAY_CLASS = 'book-preview-loading-overlay';

    function resolveRoot(rootOrSelector) {
        if (!rootOrSelector) return document.querySelector('[data-book-interior-root]');
        if (typeof rootOrSelector === 'string') return document.querySelector(rootOrSelector);
        return rootOrSelector;
    }

    function resolveEl(ref, fallbackId) {
        if (ref) return typeof ref === 'string' ? document.getElementById(ref) : ref;
        return fallbackId ? document.getElementById(fallbackId) : null;
    }

    function setRunningHeads(root, bookTitle) {
        var title = (bookTitle || '').trim();
        if (!title) return;
        root.querySelectorAll('section.chapter[id^="ch-"]').forEach(function (sec) {
            sec.setAttribute('data-running-head', title);
        });
    }

    function wireTocLinks(root) {
        root.querySelectorAll('.toc-link[data-goto-page]').forEach(function (anchor) {
            anchor.addEventListener('click', function (e) {
                e.preventDefault();
                var page = parseInt(anchor.getAttribute('data-goto-page') || '', 10);
                if (!Number.isFinite(page) || page < 1) return;
                if (typeof global.FmtBookStageReader !== 'undefined' && global.FmtBookStageReader.goToPage) {
                    global.FmtBookStageReader.goToPage(page - 1);
                    if (typeof global.updatePageIndicator === 'function') global.updatePageIndicator();
                    if (typeof global.persistReaderPageBookmark === 'function') global.persistReaderPageBookmark();
                    return;
                }
            });
        });
        root.querySelectorAll('.toc-link[href^="#"]').forEach(function (anchor) {
            if (anchor.getAttribute('data-goto-page')) return;
            anchor.addEventListener('click', function (e) {
                e.preventDefault();
                var id = (anchor.getAttribute('href') || '').slice(1);
                if (!id) return;
                var host = root.closest('.book-interior-scroll-host') || root;
                var target = root.querySelector('#' + CSS.escape(id)) || document.getElementById(id);
                if (!target) return;
                if (host && typeof host.scrollTo === 'function' && host.scrollHeight > host.clientHeight + 2) {
                    var top = target.getBoundingClientRect().top - host.getBoundingClientRect().top + host.scrollTop - 12;
                    host.scrollTo({ top: top, behavior: 'smooth' });
                } else {
                    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            });
        });
    }

    /**
     * Size a 6×9 preview shell: fit to host width (default), lock aspect ratio, optional height cap.
     */
    function fitShellToHost(shell, host, options) {
        if (!shell || !host) return false;
        var opts = options || {};
        var ratio = opts.aspectRatio > 0 ? opts.aspectRatio : DEFAULT_RATIO;
        var widthFactor = opts.widthFactor > 0 ? opts.widthFactor : DEFAULT_WIDTH_FACTOR;
        var reserve = opts.reserveBottomPx > 0 ? opts.reserveBottomPx : 0;
        var preferHeight = opts.preferHeight === true;

        var availW = Math.max(0, host.clientWidth);
        var availH = Math.max(0, host.clientHeight - reserve);
        if (availW < 48 || availH < 48) return false;

        var maxHeightCap = opts.maxHeightPx > 0 ? opts.maxHeightPx : availH;
        var cappedH = Math.min(availH, maxHeightCap);

        var shellW;
        var shellH;

        if (preferHeight) {
            shellH = Math.floor(cappedH);
            shellW = Math.floor(shellH * ratio);
            var targetW = Math.floor(availW * widthFactor);
            if (shellW < targetW) {
                shellW = targetW;
                shellH = Math.floor(shellW / ratio);
                if (shellH > cappedH) {
                    shellH = Math.floor(cappedH);
                    shellW = Math.floor(shellH * ratio);
                }
            } else if (shellW > availW) {
                shellW = Math.floor(availW * widthFactor);
                shellH = Math.floor(shellW / ratio);
            }
        } else {
            shellW = Math.floor(availW * widthFactor);
            if (opts.maxWidthPx > 0) shellW = Math.min(shellW, opts.maxWidthPx);
            shellH = Math.floor(shellW / ratio);
            if (shellH > cappedH) {
                shellH = Math.floor(cappedH);
                shellW = Math.floor(shellH * ratio);
            }
        }

        shellW = Math.max(48, shellW);
        shellH = Math.max(72, shellH);

        var prevKey = shell.dataset.layoutKey || '';
        var nextKey = shellW + 'x' + shellH;

        shell.style.width = shellW + 'px';
        shell.style.height = shellH + 'px';
        shell.style.maxWidth = '100%';
        shell.style.maxHeight = shellH + 'px';
        shell.style.flexShrink = '0';
        // Keep CSS aspect-ratio in sync with the ratio used for sizing (single page 6/9, spread ~12/9).
        shell.style.aspectRatio = String(ratio);

        if (opts.measureHost) {
            opts.measureHost.style.width = shellW + 'px';
            opts.measureHost.style.boxSizing = 'border-box';
            if (opts.syncMeasureTypography && opts.viewport) {
                var vpStyles = global.getComputedStyle(opts.viewport);
                opts.measureHost.style.padding = vpStyles.padding;
                opts.measureHost.style.fontSize = vpStyles.fontSize;
                opts.measureHost.style.lineHeight = vpStyles.lineHeight;
            }
        }

        shell.dataset.layoutKey = nextKey;
        return prevKey !== nextKey;
    }

    /**
     * Unified preview layout sync for AI Writer + Book Formatting.
     * @param {{ host?: Element|string, hostId?: string, shell?: Element|string, shellId?: string, measureHost?: Element, viewport?: Element, navDock?: Element|string, minHostHeightPx?: number, widthFactor?: number, onLayoutChange?: function }} config
     */
    function syncPreviewLayout(config) {
        var cfg = config || {};
        var scrollHost = resolveEl(cfg.host, cfg.hostId);
        var shell = resolveEl(cfg.shell, cfg.shellId || 'paginatedReaderShell');
        if (!scrollHost || !shell) return false;

        if (scrollHost.clientHeight < 120 && cfg.minHostHeightPx > 0) {
            scrollHost.style.minHeight = Math.floor(cfg.minHostHeightPx) + 'px';
        }

        var reserve = cfg.reserveBottomPx > 0 ? cfg.reserveBottomPx : 0;
        var navDock = resolveEl(cfg.navDock, null);
        if (navDock && !navDock.classList.contains('hidden')) {
            reserve += navDock.offsetHeight + 8;
        }
        var chromeReserve = cfg.chromeReservePx > 0 ? cfg.chromeReservePx : 0;
        reserve += chromeReserve;

        var measureHost = cfg.measureHost || document.getElementById('fmt-preview-measure-host') || document.getElementById('previewMeasureHost');
        var viewport = cfg.viewport || document.getElementById('preview-content');

        var changed = fitShellToHost(shell, scrollHost, {
            aspectRatio: cfg.aspectRatio || DEFAULT_RATIO,
            widthFactor: cfg.widthFactor || DEFAULT_WIDTH_FACTOR,
            maxWidthPx: cfg.maxWidthPx || 0,
            maxHeightPx: cfg.maxHeightPx || 0,
            reserveBottomPx: reserve,
            preferHeight: cfg.preferHeight === true,
            measureHost: measureHost,
            viewport: viewport,
            syncMeasureTypography: !!cfg.syncMeasureTypography
        });

        if (typeof cfg.onLayoutChange === 'function') {
            cfg.onLayoutChange(changed, shell, scrollHost);
        }
        return changed;
    }

    function ensureLoadingOverlay(host) {
        if (!host) return null;
        var existing = host.querySelector('.' + LOADING_OVERLAY_CLASS);
        if (existing) return existing;
        var overlay = document.createElement('div');
        overlay.className = LOADING_OVERLAY_CLASS;
        overlay.setAttribute('role', 'status');
        overlay.setAttribute('aria-live', 'polite');
        overlay.innerHTML =
            '<div class="book-preview-loading-skeleton" aria-hidden="true"></div>' +
            '<div class="book-preview-loading-spinner" aria-hidden="true"></div>' +
            '<span class="book-preview-loading-label">Loading preview…</span>';
        host.appendChild(overlay);
        return overlay;
    }

    function showLoadingOverlay(hostOrId, label) {
        var host = resolveEl(hostOrId, typeof hostOrId === 'string' ? hostOrId : null);
        if (!host) return;
        var overlay = ensureLoadingOverlay(host);
        if (label) {
            var lbl = overlay.querySelector('.book-preview-loading-label');
            if (lbl) lbl.textContent = label;
        }
        overlay.classList.remove('hidden');
    }

    function hideLoadingOverlay(hostOrId) {
        var host = resolveEl(hostOrId, typeof hostOrId === 'string' ? hostOrId : null);
        if (!host) return;
        var overlay = host.querySelector('.' + LOADING_OVERLAY_CLASS);
        if (overlay) overlay.classList.add('hidden');
    }

    /**
     * Pre-render adjacent page HTML into a hidden layer for instant page turns.
     * @param {{ pages: string[], currentIndex: number, fill: function(number, Element), hiddenTarget?: Element }} opts
     */
    function preloadAdjacentPage(opts) {
        if (!opts || !Array.isArray(opts.pages) || typeof opts.fill !== 'function') return;
        var nextIdx = (opts.currentIndex || 0) + 1;
        if (nextIdx >= opts.pages.length) return;
        var run = function () {
            if (opts.hiddenTarget) {
                opts.fill(nextIdx, opts.hiddenTarget);
            }
        };
        if (typeof global.requestIdleCallback === 'function') {
            global.requestIdleCallback(run, { timeout: 400 });
        } else {
            global.setTimeout(run, 0);
        }
    }

    function init(rootOrSelector, options) {
        var root = resolveRoot(rootOrSelector);
        if (!root) return;
        var opts = options || {};
        var mode = opts.mode === 'print' ? 'print' : 'web';
        root.setAttribute('data-interior-mode', mode);
        root.setAttribute('data-book-interior-root', '1');
        if (mode === 'web') {
            setRunningHeads(root, opts.bookTitle || root.getAttribute('data-book-title') || '');
            wireTocLinks(root);
            syncPreviewLayout({
                host: opts.host || root,
                shell: opts.shell || root.querySelector('.paginated-reader-shell, .book-page-preview-shell'),
                measureHost: opts.measureHost,
                viewport: opts.viewport,
                widthFactor: (opts.fitOptions && opts.fitOptions.widthFactor) || DEFAULT_WIDTH_FACTOR,
                preferHeight: false,
                onLayoutChange: opts.onLayoutChange
            });
        }
    }

    global.BookInteriorPreview = {
        init: init,
        wireTocLinks: wireTocLinks,
        setRunningHeads: setRunningHeads,
        fitShellToHost: fitShellToHost,
        syncPreviewLayout: syncPreviewLayout,
        showLoadingOverlay: showLoadingOverlay,
        hideLoadingOverlay: hideLoadingOverlay,
        preloadAdjacentPage: preloadAdjacentPage
    };
})(window);
