/**
 * AI Writer full-book read mode: cover + title + copyright + TOC + paginated chapters (6×9).
 * Pairs with book-page-preview.css and InteriorLayoutTokens (via GetFullBookContent interiorCss).
 */
(function (global) {
    'use strict';

    var FM_COUNT = 3;
    var tocWired = false;

    var interiorWrapClassMap = {
        Minimalist: 'interior-minimalist',
        Novel: 'interior-novel',
        Modern: 'interior-modern',
        Classic: 'interior-classic',
        ElegantTrade: 'interior-elegant-trade',
        Traditional: 'interior-traditional',
        Contemporary: 'interior-contemporary',
        FineBook: 'interior-fine-book',
        Clean: 'interior-clean',
        POD: 'interior-pod',
        ElegantTradePOD: 'interior-elegant-trade-pod'
    };

    var fmtStageClassMap = {
        Minimalist: 'fmt-style-clean-minimalist',
        Novel: 'fmt-style-traditional',
        Modern: 'fmt-style-modern',
        Classic: 'fmt-style-classic',
        ElegantTrade: 'fmt-style-elegant-trade',
        Traditional: 'fmt-style-traditional',
        Contemporary: 'fmt-style-modern',
        FineBook: 'fmt-style-classic',
        Clean: 'fmt-style-clean-minimalist',
        POD: 'fmt-style-traditional',
        ElegantTradePOD: 'fmt-style-elegant-trade'
    };

    var allWrapClasses = Object.keys(interiorWrapClassMap).reduce(function (acc, k) {
        var c = interiorWrapClassMap[k];
        if (acc.indexOf(c) < 0) acc.push(c);
        return acc;
    }, []);

    var allStageClasses = Object.keys(fmtStageClassMap).reduce(function (acc, k) {
        var c = fmtStageClassMap[k];
        if (acc.indexOf(c) < 0) acc.push(c);
        return acc;
    }, []);

    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function resolveCoverUrl(path) {
        if (!path) return '';
        var p = String(path).trim();
        if (!p) return '';
        if (/^https?:\/\//i.test(p)) return p;
        if (p.charAt(0) === '/') return p;
        return '/' + p.replace(/^\/+/, '');
    }

    function extractHeadingsFromHtml(html) {
        var out = [];
        if (!html) return out;
        try {
            var d = document.createElement('div');
            d.innerHTML = html;
            d.querySelectorAll('h1,h2,h3,h4,h5,h6,.manuscript-h1,.manuscript-h2,.manuscript-h3').forEach(function (h) {
                var text = (h.textContent || '').replace(/\s+/g, ' ').trim();
                if (text) out.push(text);
            });
        } catch (e) { /* ignore */ }
        return out;
    }

    function getPreviewStyleHeading(title, chapterNo, narrativeOrd) {
        var chNo = chapterNo != null ? parseInt(String(chapterNo), 10) : NaN;
        if (!Number.isFinite(chNo)) chNo = narrativeOrd;
        var t = (title || '').trim();
        if (chNo <= 0) return t || 'Front matter';
        if (!t || /^\s*Chapter\s*0\s*:?\s*$/i.test(t)) return 'Chapter ' + narrativeOrd;
        if (/^\s*Chapter\s+\d+/i.test(t)) return t;
        return 'Chapter ' + narrativeOrd + ': ' + t;
    }

    function buildCoverPage(coverUrl, title) {
        var alt = escapeHtml((title || '').trim() || 'Book cover');
        return '<div class="writer-cover-page front-matter-page" data-page-kind="cover">' +
            '<img class="writer-cover-img" src="' + escapeHtml(coverUrl) + '" alt="' + alt + '" loading="eager" decoding="async" />' +
            '</div>';
    }

    function buildCoverFallbackPage(ctx) {
        var title = escapeHtml((ctx && ctx.bookTitle) || 'Untitled');
        var author = escapeHtml((ctx && ctx.authorName) || '');
        return '<div class="writer-cover-page writer-cover-fallback front-matter-page" data-page-kind="cover">' +
            '<div class="writer-cover-fallback-inner"><h1 class="writer-cover-fallback-title">' + title + '</h1>' +
            (author ? '<p class="writer-cover-fallback-author">' + author + '</p>' : '') +
            '</div></div>';
    }

    function buildTitlePage(ctx) {
        ctx = ctx || {};
        var title = escapeHtml((ctx.bookTitle || '').trim() || 'Untitled');
        var author = escapeHtml((ctx.authorName || '').trim());
        var genre = escapeHtml((ctx.genre || '').trim());
        var subtitle = escapeHtml((ctx.subtitle || '').trim());
        return '<div class="front-matter-page title-page" data-page-kind="title"><h1>' + title + '</h1>' +
            (subtitle ? '<p class="subtitle">' + subtitle + '</p>' : '') +
            (author ? '<p class="title-page-author">' + author + '</p>' : '') +
            (genre ? '<p class="title-page-genre">' + genre + '</p>' : '') +
            '</div>';
    }

    function buildCopyrightPage(ctx) {
        ctx = ctx || {};
        var title = escapeHtml((ctx.bookTitle || '').trim() || 'Untitled');
        var author = escapeHtml((ctx.authorName || '').trim() || 'Author');
        var year = new Date().getFullYear();
        return '<div class="front-matter-page copyright-page" data-page-kind="copyright"><div class="copyright-block">' +
            '<p class="cr-meta"><strong>' + title + '</strong></p>' +
            (ctx.authorName ? '<p class="cr-meta">' + escapeHtml(ctx.authorName) + '</p>' : '') +
            '<p class="cr-legal">Copyright &copy; ' + year + ' ' + author + '. All rights reserved.</p>' +
            '</div></div>';
    }

    function buildTocPage(chaptersMeta, chapterStartPages, pageOffset) {
        var items = [];
        var narrative = 0;
        for (var i = 0; i < chaptersMeta.length; i++) {
            var ch = chaptersMeta[i];
            if (!ch || ch.loading) continue;
            narrative++;
            var title = (ch.title || '').trim() || ('Chapter ' + narrative);
            var chNo = ch.chapterNo != null ? ch.chapterNo : (i + 1);
            var line = getPreviewStyleHeading(title, chNo, narrative);
            var subs = extractHeadingsFromHtml(ch.html || '');
            var startInChapters = chapterStartPages[i];
            var pageNo = (startInChapters != null ? startInChapters : 0) + pageOffset + 1;
            var subHtml = '';
            if (subs.length) {
                subHtml = '<ul class="toc-subheadings">' + subs.map(function (h) {
                    return '<li class="toc-subheading-item"><span class="toc-sub-text">' + escapeHtml(h) + '</span></li>';
                }).join('') + '</ul>';
            }
            items.push(
                '<li class="toc-item"><div class="toc-chapter-line">' +
                '<span class="toc-entry-text"><a href="#" class="toc-link" data-goto-page="' + pageNo + '">' + escapeHtml(line) + '</a></span>' +
                '<span class="toc-leader" aria-hidden="true"></span>' +
                '<span class="toc-page-ref">' + pageNo + '</span></div>' + subHtml + '</li>'
            );
        }
        if (!items.length) items.push('<li class="toc-item toc-item-empty">No chapters yet.</li>');
        return '<div class="front-matter-page toc-page" data-page-kind="toc"><div class="toc-block">' +
            '<h1 class="toc-title">Contents</h1><nav class="toc-nav" aria-label="Table of contents">' +
            '<ol class="toc-list">' + items.join('') + '</ol></nav></div></div>';
    }

    function fmtPxFromPt(pt) {
        return (pt * 96 / 72).toFixed(2) + 'px';
    }

    function applyInteriorFormatting(formatting) {
        formatting = formatting || global._aiWriterFormatting || {};
        var interior = formatting.interiorStyle || 'Novel';
        var size = formatting.textSize || 'Medium';
        var lineSp = String(formatting.lineSpacing || '1.6');
        var fontSize;
        if (interior === 'Classic') {
            if (size === 'Small') fontSize = '14px';
            else if (size === 'Large') fontSize = '19px';
            else fontSize = '17px';
        } else {
            if (size === 'Small') fontSize = fmtPxFromPt(10);
            else if (size === 'Large') fontSize = fmtPxFromPt(12);
            else fontSize = fmtPxFromPt(11);
        }
        var textAlign = (interior === 'Classic' || interior === 'ElegantTrade' || interior === 'Novel') ? 'justify' : '';
        var shell = document.getElementById('paginatedReaderShell');
        var scrollHost = document.getElementById('chapterPreviewScrollHost');
        var bookResult = document.getElementById('bookResult');

        if (bookResult) bookResult.setAttribute('data-interior-mode', 'web');

        function removeClasses(el, list) {
            if (!el) return;
            list.forEach(function (c) { el.classList.remove(c); });
        }

        var wrapCls = interiorWrapClassMap[interior] || interiorWrapClassMap.Novel;
        var stageCls = fmtStageClassMap[interior] || fmtStageClassMap.Novel;

        if (shell) {
            removeClasses(shell, allWrapClasses.concat(allStageClasses));
            shell.classList.add(wrapCls, stageCls);
            shell.style.setProperty('--fmt-font-size', fontSize);
            shell.style.setProperty('--fmt-line-height', lineSp);
            shell.style.setProperty('--ilt-body-lh', lineSp);
            shell.style.setProperty('--ilt-body-px', fontSize);
        }
        if (scrollHost) {
            removeClasses(scrollHost, allWrapClasses);
            scrollHost.classList.add(wrapCls);
        }

        global.document.documentElement.style.setProperty('--fmt-font-size', fontSize);
        global.document.documentElement.style.setProperty('--fmt-line-height', lineSp);
        global.document.documentElement.style.setProperty('--ilt-body-lh', lineSp);
        global.document.documentElement.style.setProperty('--ilt-body-px', fontSize);

        var viewport = document.getElementById('preview-content');
        if (viewport) {
            viewport.style.textAlign = textAlign || '';
        }

        if (formatting.pageBackgroundColor) {
            if (shell) shell.style.setProperty('--export-page-bg', formatting.pageBackgroundColor);
        }
    }

    function ensureInteriorCss(css) {
        if (!css) return;
        var id = 'ai-writer-interior-tokens';
        var el = document.getElementById(id);
        if (!el) {
            el = document.createElement('style');
            el.id = id;
            document.head.appendChild(el);
        }
        var scoped = String(css)
            .replace(/#book-formatter-root/g, '#bookResult')
            .replace(/#fmt-book-result/g, '#bookResult');
        if (el.textContent !== scoped) el.textContent = scoped;
        appendAiWriterInteriorBridge();
    }

    function appendAiWriterInteriorBridge() {
        var id = 'ai-writer-interior-bridge';
        var el = document.getElementById(id);
        if (!el) {
            el = document.createElement('style');
            el.id = id;
            document.head.appendChild(el);
        }
        el.textContent =
            '#bookResult[data-interior-mode="web"] .paginated-reader-shell.book-page-preview-shell {' +
            'background: var(--ilt-page-bg, var(--book-preview-page-bg, #fffef8)) !important;' +
            'border: 1px solid rgba(68, 48, 36, 0.28) !important; border-radius: 3px !important;' +
            'box-shadow: var(--book-preview-page-shadow) !important; z-index: 3 !important; }' +
            '#bookResult[data-interior-mode="web"] #preview-content.book-page-preview-content {' +
            'background: var(--ilt-page-bg, var(--book-preview-page-bg, #fffef8));' +
            'color: var(--ilt-body-color, #1c1917); font-family: var(--ilt-body-font, Georgia, \'Times New Roman\', serif);' +
            'font-size: var(--ilt-body-px, inherit); line-height: var(--ilt-body-lh, inherit); }' +
            '#bookResult #preview-content .reader-chapter-block .reader-page-body,' +
            '#bookResult #preview-content .reader-chapter-block .reader-page-body p,' +
            '#bookResult #preview-content .reader-chapter-block .reader-page-body .manuscript-p {' +
            'max-width: var(--ilt-text-max, 100%); margin-inline: auto; width: 100%; box-sizing: border-box; }' +
            '#bookResult #preview-content .reader-chapter-block .reader-page-title,' +
            '#bookResult #preview-content .reader-chapter-block .manuscript-chapter-heading.reader-page-title {' +
            'font-family: var(--heading-font, Georgia, serif); color: var(--heading-color, inherit);' +
            'text-align: center; margin: 0 0 0.65rem; font-weight: 600; }' +
            '#bookResult #chapterPreviewScrollHost.book-preview-stage {' +
            'background: #edf0f4 !important; align-items: center !important; }';
    }

    /** Match formatter DOM: reader-chapter-block > reader-page-title + reader-page-body */
    function normalizeChapterPageHtml(html) {
        if (!html || html.indexOf('reader-page-body') >= 0) return html;
        if (html.indexOf('front-matter-page') >= 0 || html.indexOf('writer-cover-page') >= 0) return html;
        try {
            var d = document.createElement('div');
            d.innerHTML = html;
            var block = d.querySelector('.reader-chapter-block');
            if (!block) return html;
            var heading = block.querySelector('.manuscript-chapter-heading, h1, h2, h3, h4, h5, h6');
            var titleHtml = '';
            if (heading) {
                heading.classList.add('reader-page-title');
                if (!heading.classList.contains('manuscript-chapter-heading')) {
                    heading.classList.add('manuscript-chapter-heading');
                }
                titleHtml = heading.outerHTML;
                heading.remove();
            }
            var bodyInner = block.innerHTML.trim();
            block.innerHTML = titleHtml + '<section class="reader-page-body">' + (bodyInner || '<p class="text-slate-500">No content.</p>') + '</section>';
            return d.innerHTML;
        } catch (e) {
            return html;
        }
    }

    function waitForShellReady(cb, tries) {
        tries = tries || 0;
        if (typeof global.syncAiWriterPreviewLayout === 'function') global.syncAiWriterPreviewLayout();
        var shell = document.getElementById('paginatedReaderShell');
        var viewport = document.getElementById('preview-content');
        var ready = shell && viewport && shell.clientWidth >= 48 && shell.clientHeight >= 48;
        if (ready || tries >= 16) {
            cb(shell, viewport);
            return;
        }
        global.requestAnimationFrame(function () { waitForShellReady(cb, tries + 1); });
    }

    function syncContextFromDom(ctx) {
        ctx = Object.assign({}, ctx || global._aiBookPreviewContext || {});
        var titleEl = document.getElementById('BookTitle');
        var bookTitle = (titleEl && titleEl.value) ? String(titleEl.value).trim() : '';
        if (bookTitle) ctx.bookTitle = bookTitle;
        return ctx;
    }

    function paginateAllChapters(meta, shell, viewport) {
        var chapterPages = [];
        var chapterIdxs = [];
        var splitFn = global.splitHtmlIntoReaderPages;
        if (typeof splitFn !== 'function') return { pages: chapterPages, idxs: chapterIdxs };

        for (var i = 0; i < meta.length; i++) {
            var ch = meta[i];
            if (!ch || ch.loading) continue;
            var html = String(ch.html || '').trim();
            if (!html) continue;
            var fullHtml = '<div class="reader-chapter-block">' + html + '</div>';
            var key = typeof global.getReaderLayoutCacheKey === 'function'
                ? global.getReaderLayoutCacheKey(shell, viewport)
                : (shell.clientWidth + 'x' + shell.clientHeight);
            var pages;
            if (ch._pagesCache && ch._pagesCacheKey === key && ch._pagesCache.length) {
                pages = ch._pagesCache;
            } else {
                pages = splitFn(fullHtml, shell, viewport);
                if (!pages.length) pages = [fullHtml];
                ch._pagesCache = pages;
                ch._pagesCacheKey = key;
            }
            for (var p = 0; p < pages.length; p++) {
                chapterPages.push(normalizeChapterPageHtml(pages[p]));
                chapterIdxs.push(i);
            }
        }
        return { pages: chapterPages, idxs: chapterIdxs };
    }

    function findFirstPageForChapter(chIdx) {
        var idxs = global._writerPageChapterIdx || [];
        for (var i = 0; i < idxs.length; i++) {
            if (idxs[i] === chIdx) return i;
        }
        return -1;
    }

    function findLastPageForChapter(chIdx) {
        var idxs = global._writerPageChapterIdx || [];
        for (var i = idxs.length - 1; i >= 0; i--) {
            if (idxs[i] === chIdx) return i;
        }
        return -1;
    }

    function syncLegacyChapterIndex(pageIdx) {
        var idxs = global._writerPageChapterIdx || [];
        var chIdx = idxs[pageIdx];
        if (chIdx == null || chIdx < 0) {
            global._previewChapterIndex = 0;
            global._previewPageIndex = 0;
            global._previewChapterPages = [global._writerBookPages[pageIdx] || ''];
            return;
        }
        global._previewChapterIndex = chIdx;
        var chStart = pageIdx;
        while (chStart > 0 && idxs[chStart - 1] === chIdx) chStart--;
        global._previewPageIndex = pageIdx - chStart;
        var pages = [];
        for (var i = chStart; i < idxs.length && idxs[i] === chIdx; i++) {
            pages.push(global._writerBookPages[i]);
        }
        global._previewChapterPages = pages.length ? pages : [global._writerBookPages[pageIdx] || ''];
    }

    function pageLabel(pageIdx) {
        var pages = global._writerBookPages || [];
        var html = pages[pageIdx] || '';
        if (html.indexOf('writer-cover-page') >= 0 || html.indexOf('data-page-kind="cover"') >= 0) return 'Cover';
        if (html.indexOf('title-page') >= 0 || html.indexOf('data-page-kind="title"') >= 0) return 'Title page';
        if (html.indexOf('copyright-page') >= 0 || html.indexOf('data-page-kind="copyright"') >= 0) return 'Copyright';
        if (html.indexOf('toc-page') >= 0 || html.indexOf('data-page-kind="toc"') >= 0) return 'Contents';
        var idxs = global._writerPageChapterIdx || [];
        var chIdx = idxs[pageIdx];
        if (chIdx >= 0) {
            var meta = global._previewChaptersMeta || [];
            var ch = meta[chIdx] || {};
            var chNum = ch.chapterNo != null ? parseInt(ch.chapterNo, 10) : (chIdx + 1);
            if (typeof global.formatChapterDisplayName === 'function') {
                var t = global.formatChapterDisplayName(chNum, ch.title);
                if (t) return t;
            }
            return (ch.title || '').trim() || ('Chapter ' + (chIdx + 1));
        }
        return 'Page ' + (pageIdx + 1);
    }

    function getSpreadLeftIndex(idx) {
        var i = Math.max(0, idx | 0);
        return (i % 2 === 0) ? i : (i - 1);
    }

    function resolveWriterRunningHead(pageIdx, pages) {
        var html = (pages && pages[pageIdx]) || '';
        if (!html) return '';
        if (html.indexOf('writer-cover-page') >= 0 || html.indexOf('data-chapter-start') >= 0) return '';
        if (pageIdx < (global._writerFrontMatterCount || 0)) return '';
        var ctx = global._aiBookPreviewContext || {};
        var bookTitle = String(ctx.bookTitle || '').trim();
        if (!bookTitle) {
            var bt = document.getElementById('BookTitle') || document.getElementById('preview-book-title');
            if (bt) bookTitle = String(bt.value || bt.textContent || '').trim();
        }
        var idxs = global._writerPageChapterIdx || [];
        var ci = idxs.length > pageIdx ? idxs[pageIdx] : -1;
        var chapterTitle = '';
        var meta = global._previewChaptersMeta || [];
        if (ci >= 0 && meta[ci]) chapterTitle = String(meta[ci].chapterTitle || meta[ci].title || '').trim();
        var pageNo = pageIdx + 1;
        var isRecto = (pageNo % 2) === 1;
        var text = isRecto ? (chapterTitle || bookTitle) : (bookTitle || chapterTitle);
        if (!text || /^(untitled|your chapter)/i.test(text)) return '';
        return text.length > 52 ? text.substring(0, 49) + '\u2026' : text;
    }

    function escapeWriterHead(s) {
        return String(s || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function buildWriterSpreadFace(pageIdx, pages, side) {
        var sideClass = side === 'recto' ? 'book-open-page--recto' : 'book-open-page--verso';
        if (pageIdx < 0 || pageIdx >= pages.length) {
            return '<div class="book-open-page ' + sideClass + '" aria-hidden="true">' +
                '<div class="book-open-running-head is-empty">&nbsp;</div>' +
                '<div class="book-open-page-inner book-open-blank"></div>' +
                '<div class="book-open-folio">&nbsp;</div></div>';
        }
        var html = pages[pageIdx] || '';
        var body = String(html || '').replace(/<div[^>]*class="[^"]*writer-page-folio[^"]*"[^>]*>[\s\S]*?<\/div>/gi, '');
        var isCover = body.indexOf('writer-cover-page') >= 0;
        var pageNum = pageIdx + 1;
        var head = isCover ? '' : resolveWriterRunningHead(pageIdx, pages);
        var headClass = head ? 'book-open-running-head' : 'book-open-running-head is-muted';
        var folio = isCover
            ? '<div class="book-open-folio">&nbsp;</div>'
            : ('<div class="book-open-folio writer-page-folio" aria-label="Page ' + pageNum + '">' + pageNum + '</div>');
        return '<div class="book-open-page ' + sideClass + '">' +
            '<div class="' + headClass + '">' + (head ? escapeWriterHead(head) : '&nbsp;') + '</div>' +
            '<div class="book-open-page-inner">' + body + '</div>' +
            folio +
            '</div>';
    }

    function renderWriterPage(pageIdx) {
        var viewport = document.getElementById('preview-content');
        var shell = document.getElementById('paginatedReaderShell');
        var pages = global._writerBookPages || [];
        if (!pages.length) return;
        pageIdx = Math.max(0, Math.min(pageIdx, pages.length - 1));
        pageIdx = getSpreadLeftIndex(pageIdx);
        global._writerPageIndex = pageIdx;
        var rightIdx = pageIdx + 1;
        if (viewport) {
            if (shell) shell.classList.add('is-open-spread');
            viewport.innerHTML =
                '<div class="book-open-spread" role="group" aria-label="Open book spread" data-kdp-bleed="0">' +
                    buildWriterSpreadFace(pageIdx, pages, 'verso') +
                    '<div class="book-open-spine" aria-hidden="true"></div>' +
                    buildWriterSpreadFace(rightIdx, pages, 'recto') +
                '</div>';
            viewport.scrollTop = 0;
            var leftBody = pages[pageIdx] || '';
            var isCover = leftBody.indexOf('writer-cover-page') >= 0;
            viewport.classList.toggle('writer-cover-active', isCover);
            viewport.classList.toggle('writer-front-matter-active', !isCover && pageIdx < (global._writerFrontMatterCount || 0));
        }
        var previewHeader = document.getElementById('preview-chapter-header');
        if (previewHeader) previewHeader.classList.add('hidden');
        syncLegacyChapterIndex(pageIdx);
        if (typeof global.updateChapterPreviewNavUI === 'function') global.updateChapterPreviewNavUI();
        if (typeof global.saveReaderBookState === 'function') global.saveReaderBookState();
        if (typeof global.syncAiWriterPreviewLayout === 'function') {
            global.requestAnimationFrame(function () { global.syncAiWriterPreviewLayout(); });
        }
    }

    function resolveStartPageIndex(opts, meta) {
        opts = opts || {};
        if (opts.useFlatPage === true && typeof opts.startPageIndex === 'number' && Number.isFinite(opts.startPageIndex)) {
            return Math.max(0, opts.startPageIndex);
        }
        if (typeof opts.startPageIndex === 'number' && Number.isFinite(opts.startPageIndex) && opts.startChapterIndex == null) {
            return Math.max(0, opts.startPageIndex);
        }
        if (typeof opts.startChapterIndex === 'number' && Number.isFinite(opts.startChapterIndex)) {
            var chIdx = Math.max(0, Math.min(opts.startChapterIndex, meta.length - 1));
            var pg = typeof opts.startPageIndex === 'number' ? Math.max(0, opts.startPageIndex) : 0;
            if (typeof opts.initialPage === 'number') pg = opts.initialPage >= 0 ? opts.initialPage : -1;
            var first = findFirstPageForChapter(chIdx);
            if (first < 0) return 0;
            if (pg === -1) {
                var last = findLastPageForChapter(chIdx);
                return last >= 0 ? last : first;
            }
            return first + pg;
        }
        try {
            var bidEl = document.getElementById('BookId');
            var bid = bidEl ? bidEl.value : '0';
            if (typeof global.loadReaderBookState === 'function') {
                var st = global.loadReaderBookState(bid);
                if (st && typeof st.flatPage === 'number' && st.flatPage >= 0) return st.flatPage;
            }
        } catch (e) { /* ignore */ }
        return 0;
    }

    function rebuildFullBookPagesCore(meta, ctx, formatting, coverUrl, opts, shell, viewport) {
        meta = meta || [];
        ctx = syncContextFromDom(ctx);
        formatting = formatting || global._aiWriterFormatting || {};
        opts = opts || {};

        if (!shell || !viewport) return false;

        var hasContent = meta.some(function (m) { return m && !m.loading && String(m.html || '').trim(); });
        if (!hasContent) {
            global._writerFullBookMode = false;
            global._writerBookPages = null;
            return false;
        }

        if (typeof global.invalidateReaderPageCaches === 'function') global.invalidateReaderPageCaches();

        applyInteriorFormatting(formatting);
        if (formatting.interiorCss) ensureInteriorCss(formatting.interiorCss);
        else appendAiWriterInteriorBridge();

        var paginated = paginateAllChapters(meta, shell, viewport);
        var chapterPages = paginated.pages;
        var chapterIdxs = paginated.idxs;
        if (!chapterPages.length) return false;

        var chapterStartPages = {};
        for (var j = 0; j < chapterIdxs.length; j++) {
            var ci = chapterIdxs[j];
            if (chapterStartPages[ci] === undefined) chapterStartPages[ci] = j;
        }

        var resolvedCover = resolveCoverUrl(coverUrl || global._aiWriterCoverUrl || ctx.coverImagePath || '');
        var COVER_COUNT = 1;
        var fmOffset = COVER_COUNT + FM_COUNT;
        var toc = buildTocPage(meta, chapterStartPages, fmOffset);
        var fmPages = [buildTitlePage(ctx), buildCopyrightPage(ctx), toc];
        if (resolvedCover) {
            fmPages.unshift(buildCoverPage(resolvedCover, ctx.bookTitle));
        } else {
            fmPages.unshift(buildCoverFallbackPage(ctx));
        }

        var allPages = fmPages.concat(chapterPages);
        var allIdxs = [];
        for (var k = 0; k < fmPages.length; k++) allIdxs.push(-1);
        allIdxs = allIdxs.concat(chapterIdxs);

        global._writerFullBookMode = true;
        global._writerBookPages = allPages;
        global._writerPageChapterIdx = allIdxs;
        global._writerFrontMatterCount = fmPages.length;
        global._aiBookPreviewContext = ctx;

        var start = resolveStartPageIndex(opts, meta);
        start = Math.max(0, Math.min(start, allPages.length - 1));
        renderWriterPage(start);
        wireTocOnce();
        return true;
    }

    function rebuildFullBookPages(meta, ctx, formatting, coverUrl, opts) {
        meta = meta || [];
        ctx = ctx || global._aiBookPreviewContext || {};
        formatting = formatting || global._aiWriterFormatting || {};
        opts = opts || {};

        var shell = document.getElementById('paginatedReaderShell');
        var viewport = document.getElementById('preview-content');
        if (!shell || !viewport) return false;

        var hasContent = meta.some(function (m) { return m && !m.loading && String(m.html || '').trim(); });
        if (!hasContent) {
            global._writerFullBookMode = false;
            global._writerBookPages = null;
            return false;
        }

        if (shell.clientWidth >= 48 && shell.clientHeight >= 48) {
            return rebuildFullBookPagesCore(meta, ctx, formatting, coverUrl, opts, shell, viewport);
        }

        waitForShellReady(function (sh, vp) {
            rebuildFullBookPagesCore(meta, ctx, formatting, coverUrl, opts, sh, vp);
        });
        return false;
    }

    function rebuildIfPossible(meta, opts) {
        meta = meta || global._previewChaptersMeta || [];
        var ctx = global._aiBookPreviewContext || {};
        var fmt = global._aiWriterFormatting || {};
        var cover = global._aiWriterCoverUrl || ctx.coverImagePath || '';
        return rebuildFullBookPages(meta, ctx, fmt, cover, opts);
    }

    function navNext() {
        var pi = global._writerPageIndex || 0;
        var pages = global._writerBookPages || [];
        var next = getSpreadLeftIndex(pi) + 2;
        if (next < pages.length) {
            renderWriterPage(next);
            global.requestAnimationFrame(function () {
                if (typeof global.scrollAiWriterChapterToTop === 'function') global.scrollAiWriterChapterToTop();
            });
        }
    }

    function navPrev() {
        var pi = global._writerPageIndex || 0;
        var prev = getSpreadLeftIndex(pi) - 2;
        if (prev >= 0) {
            renderWriterPage(prev);
            global.requestAnimationFrame(function () {
                if (typeof global.scrollAiWriterChapterToTop === 'function') global.scrollAiWriterChapterToTop();
            });
        }
    }

    function goToPage(pageIdx) {
        renderWriterPage(pageIdx);
        global.requestAnimationFrame(function () {
            if (typeof global.scrollAiWriterChapterToTop === 'function') global.scrollAiWriterChapterToTop();
        });
    }

    function wireTocOnce() {
        if (tocWired) return;
        var root = document.getElementById('paginatedReaderShell') || document.getElementById('preview-content');
        if (!root) return;
        tocWired = true;
        root.addEventListener('click', function (e) {
            var anchor = e.target && e.target.closest ? e.target.closest('.toc-link[data-goto-page]') : null;
            if (!anchor) return;
            e.preventDefault();
            var p = parseInt(anchor.getAttribute('data-goto-page') || '', 10);
            if (!Number.isFinite(p) || p < 1) return;
            goToPage(p - 1);
        });
    }

    function goToChapter(chIdx, opts) {
        opts = opts || {};
        var first = findFirstPageForChapter(chIdx);
        if (first < 0) return false;
        if (opts.initialPage === -1) {
            var last = findLastPageForChapter(chIdx);
            renderWriterPage(last >= 0 ? last : first);
        } else {
            var pg = typeof opts.initialPage === 'number' ? Math.max(0, opts.initialPage) : 0;
            renderWriterPage(first + pg);
        }
        return true;
    }

    global.AiWriterReadMode = {
        rebuildFullBookPages: rebuildFullBookPages,
        rebuildIfPossible: rebuildIfPossible,
        renderWriterPage: renderWriterPage,
        navNext: navNext,
        navPrev: navPrev,
        goToPage: goToPage,
        goToChapter: goToChapter,
        findFirstPageForChapter: findFirstPageForChapter,
        findLastPageForChapter: findLastPageForChapter,
        pageLabel: pageLabel,
        applyInteriorFormatting: applyInteriorFormatting,
        syncContextFromDom: syncContextFromDom,
        normalizeChapterPageHtml: normalizeChapterPageHtml
    };
})(window);
