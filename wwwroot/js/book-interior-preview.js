/**

 * Web preview parity with print PDF: running heads, TOC scroll links, fit-to-viewport layout.

 * Pairs with InteriorLayoutTokens CSS and BookPreviewPrintHtmlBuilder HTML shape.

 */

(function (global) {

    'use strict';



    function resolveRoot(rootOrSelector) {

        if (!rootOrSelector) return document.querySelector('[data-book-interior-root]');

        if (typeof rootOrSelector === 'string') return document.querySelector(rootOrSelector);

        return rootOrSelector;

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

     * Size a 6×9 (or custom ratio) preview shell to fill its host without internal scroll.

     * @param {Element} shell

     * @param {Element} host

     * @param {{ aspectRatio?: number, widthFactor?: number, maxWidthPx?: number, reserveBottomPx?: number, measureHost?: Element, preferHeight?: boolean }} [options]

     * @returns {boolean} true when dimensions changed

     */

    function fitShellToHost(shell, host, options) {

        if (!shell || !host) return false;

        var opts = options || {};

        var ratio = opts.aspectRatio > 0 ? opts.aspectRatio : (6 / 9);

        var widthFactor = opts.widthFactor > 0 ? opts.widthFactor : 1;

        var reserve = opts.reserveBottomPx > 0 ? opts.reserveBottomPx : 0;



        var availW = Math.max(0, host.clientWidth);

        var availH = Math.max(0, host.clientHeight - reserve);

        if (availW < 48 || availH < 48) return false;



        var shellW = Math.floor(availW * widthFactor);

        if (opts.maxWidthPx > 0) shellW = Math.min(shellW, opts.maxWidthPx);



        var shellH;

        if (opts.preferHeight !== false) {

            shellH = Math.floor(availH);

            shellW = Math.floor(shellH * ratio);

            if (shellW > availW) {

                shellW = Math.floor(availW);

                shellH = Math.floor(shellW / ratio);

            }

        } else {

            shellH = Math.floor(shellW / ratio);

            if (shellH > availH) {

                shellH = Math.floor(availH);

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

        shell.style.aspectRatio = '6 / 9';



        if (opts.measureHost) {

            opts.measureHost.style.width = shellW + 'px';

            opts.measureHost.style.boxSizing = 'border-box';

        }



        shell.dataset.layoutKey = nextKey;

        return prevKey !== nextKey;

    }



    /**

     * Initialize web preview shell (call after preview HTML is injected).

     * @param {Element|string} [rootOrSelector]

     * @param {{ bookTitle?: string, mode?: 'web'|'print', shell?: Element, host?: Element, onLayoutChange?: function }} [options]

     */

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

            var shell = opts.shell || root.querySelector('.paginated-reader-shell, .book-page-preview-shell');

            var host = opts.host || root;

            if (shell && host) {

                fitShellToHost(shell, host, opts.fitOptions || {});

                if (typeof opts.onLayoutChange === 'function' && shell.dataset.layoutKey) {

                    opts.onLayoutChange(shell.dataset.layoutKey);

                }

            }

        }

    }



    global.BookInteriorPreview = {

        init: init,

        wireTocLinks: wireTocLinks,

        setRunningHeads: setRunningHeads,

        fitShellToHost: fitShellToHost

    };

})(window);


