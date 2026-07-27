/**
 * Formatting workspace — two-page book spread preview.
 * Paginate chapter HTML (text + images), apply KDP trim/margins/bleed, TOC jump.
 */
(function (global) {
    'use strict';

    var DPI = 96; // CSS px per inch for screen preview scaling

    function kdpSpecs() {
        return global.__kdpSpecs || {
            bleedIn: 0.125,
            margins: {
                recommendedOuterNoBleedIn: 0.25,
                recommendedOuterWithBleedIn: 0.375,
                gutterTiers: [
                    { maxPageCount: 150, insideIn: 0.375 },
                    { maxPageCount: 300, insideIn: 0.5 },
                    { maxPageCount: 500, insideIn: 0.625 },
                    { maxPageCount: 700, insideIn: 0.75 },
                    { maxPageCount: 828, insideIn: 0.875 }
                ],
                fallbackInsideIn: 0.875
            }
        };
    }

    function interiorMarginsSpec() {
        var s = kdpSpecs();
        return s.margins || s.interiorMargins || {};
    }

    function gutterInsideIn(pageCount) {
        var meta = state.kdpMeta;
        var im = interiorMarginsSpec();
        var tiers = (meta && meta.gutterTiers) || im.gutterTiers || null;
        if (tiers && tiers.length) {
            for (var i = 0; i < tiers.length; i++) {
                var t = tiers[i];
                var max = t.maxPageCount != null ? t.maxPageCount : t.MaxPageCount;
                var inside = t.insideIn != null ? t.insideIn : t.InsideIn;
                if (pageCount <= max) return inside;
            }
            return im.fallbackInsideIn != null ? im.fallbackInsideIn : (im.FallbackInsideIn != null ? im.FallbackInsideIn : 0.875);
        }
        if (pageCount <= 150) return 0.375;
        if (pageCount <= 300) return 0.5;
        if (pageCount <= 500) return 0.625;
        if (pageCount <= 700) return 0.75;
        return 0.875;
    }
    var state = {
        bookTitle: '',
        chapters: [],
        pages: [],
        screens: [],
        toc: [],
        pageList: [],
        spreadIndex: 0,
        screenIndex: 0,
        previewMode: 'print', // print | ebook
        binding: 'Paperback',
        trimW: 6,
        trimH: 9,
        margins: { top: 0.625, bottom: 0.875, inside: 0.8125, outside: 0.625 },
        bleed: false,
        bleedIn: (global.__kdpSpecs && global.__kdpSpecs.bleedIn) ?? 0.125,
        useKdp: true,
        fontPt: 11,
        lineHeight: 1.5,
        rightHandStarts: true,
        kdpMeta: null
    };

    var els = {};

    function $(id) { return document.getElementById(id); }

    function esc(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    function inchToPx(inches) { return inches * DPI; }

    function parseChaptersJson() {
        var node = $('fmt-chapters-json');
        if (!node) return [];
        try {
            var data = JSON.parse(node.textContent || '[]');
            return Array.isArray(data) ? data : [];
        } catch (_) {
            return [];
        }
    }

    function parseKdpMeta() {
        var node = $('fmt-kdp-meta-json');
        if (!node) return null;
        try { return JSON.parse(node.textContent || 'null'); } catch (_) { return null; }
    }

    /** Split chapter HTML into block-level segments (paragraphs, headings, figures). */
    function htmlToBlocks(html) {
        var wrap = document.createElement('div');
        wrap.innerHTML = String(html || '').trim() || '<p></p>';
        var blocks = [];
        Array.prototype.forEach.call(wrap.childNodes, function (node) {
            if (node.nodeType === 3) {
                var t = (node.textContent || '').trim();
                if (t) blocks.push('<p class="fmt-spread-p">' + esc(t) + '</p>');
                return;
            }
            if (node.nodeType !== 1) return;
            var el = node;
            var tag = (el.tagName || '').toLowerCase();
            if (tag === 'img') {
                blocks.push('<p class="fmt-spread-figure">' + el.outerHTML + '</p>');
                return;
            }
            // Promote bare text wrappers
            if (tag === 'div' && !el.querySelector('img') && el.children.length === 0) {
                var tx = (el.textContent || '').trim();
                if (tx) blocks.push('<p class="fmt-spread-p">' + esc(tx) + '</p>');
                return;
            }
            if (/^h[1-6]$/.test(tag)) {
                el.classList.add('fmt-spread-heading');
                blocks.push(el.outerHTML);
                return;
            }
            if (tag === 'p' || tag === 'figure' || tag === 'blockquote' || tag === 'ul' || tag === 'ol' || tag === 'hr') {
                if (el.querySelector('img')) el.classList.add('fmt-spread-figure');
                else if (tag === 'p') el.classList.add('fmt-spread-p');
                blocks.push(el.outerHTML);
                return;
            }
            // Nested: flatten children if possible
            if (el.children.length) {
                Array.prototype.forEach.call(el.children, function (child) {
                    blocks = blocks.concat(htmlToBlocks(child.outerHTML));
                });
            } else {
                var plain = (el.textContent || '').trim();
                if (plain) blocks.push('<p class="fmt-spread-p">' + esc(plain) + '</p>');
            }
        });
        return blocks.filter(Boolean);
    }

    function createMeasureBox(contentW, contentH) {
        var box = document.createElement('div');
        box.className = 'fmt-spread-measure';
        box.style.cssText = [
            'position:absolute', 'left:-99999px', 'top:0', 'visibility:hidden',
            'width:' + contentW + 'px', 'height:' + contentH + 'px',
            'overflow:hidden', 'box-sizing:border-box',
            'font-family:Georgia,"Times New Roman",serif',
            'font-size:' + state.fontPt + 'pt',
            'line-height:' + state.lineHeight,
            'text-align:justify', 'hyphens:auto'
        ].join(';');
        document.body.appendChild(box);
        return box;
    }

    function applyImageConstraints(root) {
        root.querySelectorAll('img').forEach(function (img) {
            img.classList.add('fmt-spread-img');
            img.style.maxWidth = '100%';
            img.style.height = 'auto';
            img.style.display = 'block';
            img.style.margin = '0.6em auto';
            // Cap tall images so they leave room for text on the same page when possible
            img.style.maxHeight = '70%';
            img.setAttribute('loading', 'eager');
            img.setAttribute('decoding', 'async');
        });
    }

    function packBlocksIntoPages(chapters) {
        var trimW = state.trimW;
        var trimH = state.trimH;
        var m = state.margins;
        var contentW = inchToPx(trimW - m.inside - m.outside);
        var contentH = inchToPx(trimH - m.top - m.bottom);
        contentW = Math.max(80, contentW);
        contentH = Math.max(120, contentH);

        var measure = createMeasureBox(contentW, contentH);
        var pages = [];
        var pageNo = 1;

        function pushBlank(reason) {
            pages.push({
                pageNo: pageNo++,
                html: '',
                chapterNo: 0,
                chapterTitle: '',
                isBlank: true,
                isChapterStart: false,
                blankReason: reason || 'blank'
            });
        }

        function ensureRectoStart() {
            if (!state.rightHandStarts) return;
            // Odd page numbers are recto (right-hand).
            if ((pageNo % 2) === 0) pushBlank('recto-pad');
        }

        chapters.forEach(function (ch, chIdx) {
            var title = (ch.title || ch.Title || ('Chapter ' + (ch.chapterNo || ch.ChapterNo || chIdx + 1))).trim();
            var chapterNo = ch.chapterNo || ch.ChapterNo || (chIdx + 1);
            var body = ch.contentHtml || ch.ContentHtml || ch.body || ch.Body || '';
            var matter = (ch.matter || ch.Matter || 'body').toLowerCase();
            var blocks = htmlToBlocks(body);
            if (!blocks.length) return;

            if (matter === 'body' || matter === 'back') ensureRectoStart();

            var opener = '<div class="fmt-spread-chapter-opener" data-chapter="' + esc(chapterNo) + '">' +
                '<div class="fmt-spread-ch-title">' + esc(title) + '</div></div>';

            var acc = [];
            var isFirstPageOfChapter = true;

            function flush() {
                if (!acc.length && !isFirstPageOfChapter) return;
                var html = (isFirstPageOfChapter ? opener : '') + acc.join('');
                if (!html.trim() && !isFirstPageOfChapter) return;
                pages.push({
                    pageNo: pageNo++,
                    html: html,
                    chapterNo: chapterNo,
                    chapterTitle: title,
                    isBlank: false,
                    isChapterStart: isFirstPageOfChapter,
                    matter: matter
                });
                acc = [];
                isFirstPageOfChapter = false;
            }

            blocks.forEach(function (blockHtml) {
                measure.innerHTML = (isFirstPageOfChapter ? opener : '') + acc.join('') + blockHtml;
                applyImageConstraints(measure);
                // Force image layout
                measure.querySelectorAll('img').forEach(function (img) {
                    if (!img.complete) { /* height may be 0 until load; use min height guess */ }
                });

                if (measure.scrollHeight > contentH + 1 && acc.length > 0) {
                    flush();
                    measure.innerHTML = (isFirstPageOfChapter ? opener : '') + blockHtml;
                    applyImageConstraints(measure);
                    // Single block taller than page — still place it (image may dominate page)
                    acc = [blockHtml];
                    if (measure.scrollHeight > contentH + 1) {
                        flush();
                    }
                } else {
                    acc.push(blockHtml);
                }
            });
            flush();
        });

        measure.remove();

        // Prefer ending on verso so last spread is complete (optional blank recto omitted)
        return pages;
    }

    function rebuildTocAndPageList(pages) {
        if (state.previewMode === 'ebook') {
            var tocE = [];
            var pageListE = [];
            var seenE = {};
            state.screens.forEach(function (s) {
                pageListE.push({
                    pageNumber: s.screenNo,
                    chapterNo: s.chapterNo,
                    label: s.isChapterStart ? s.chapterTitle : ('… ' + s.chapterTitle)
                });
                if (s.isChapterStart && s.chapterNo && !seenE[s.chapterNo]) {
                    seenE[s.chapterNo] = true;
                    tocE.push({
                        chapterNo: s.chapterNo,
                        title: s.chapterTitle,
                        startPage: s.screenNo,
                        matter: 'body'
                    });
                }
            });
            state.toc = tocE;
            state.pageList = pageListE;
            state.pages = [];
            renderSidebar();
            return;
        }
        var toc = [];
        var pageList = [];
        var seen = {};
        (pages || []).forEach(function (p) {
            pageList.push({
                pageNumber: p.pageNo,
                chapterNo: p.chapterNo,
                label: p.isBlank ? '(blank)' : (p.isChapterStart ? p.chapterTitle : ('… ' + (p.chapterTitle || '')))
            });
            if (p.isChapterStart && p.chapterNo && !seen[p.chapterNo]) {
                seen[p.chapterNo] = true;
                toc.push({
                    chapterNo: p.chapterNo,
                    title: p.chapterTitle,
                    startPage: p.pageNo,
                    matter: p.matter || 'body'
                });
            }
        });
        state.toc = toc;
        state.pageList = pageList;
        state.pages = pages || [];
        renderSidebar();
    }

    function renderSidebar() {
        var tocList = $('fmtTocList');
        var tocEmpty = $('fmtTocEmpty');
        var pagesList = $('fmtPagesList');
        var pagesEmpty = $('fmtPagesEmpty');

        if (tocList) {
            if (!state.toc.length) {
                tocList.hidden = true;
                if (tocEmpty) {
                    tocEmpty.hidden = false;
                    tocEmpty.textContent = 'Upload a manuscript to build the table of contents.';
                }
            } else {
                if (tocEmpty) tocEmpty.hidden = true;
                tocList.hidden = false;
                tocList.innerHTML = state.toc.map(function (item) {
                    var badge = item.matter && item.matter !== 'body'
                        ? '<span class="fmt-matter ' + esc(item.matter) + '">' + esc(item.matter) + '</span>'
                        : '';
                    return '<li data-page="' + item.startPage + '" data-chapter="' + item.chapterNo + '" tabindex="0" role="button">' +
                        '<span class="fmt-title">' + badge + esc(item.title) + '</span>' +
                        '<span class="fmt-page">' + item.startPage + '</span></li>';
                }).join('');
            }
        }

        if (pagesList) {
            if (!state.pageList.length) {
                pagesList.hidden = true;
                if (pagesEmpty) {
                    pagesEmpty.hidden = false;
                    pagesEmpty.textContent = 'Page list appears after import.';
                }
            } else {
                if (pagesEmpty) pagesEmpty.hidden = true;
                pagesList.hidden = false;
                pagesList.innerHTML = state.pageList.map(function (p) {
                    return '<li data-page="' + p.pageNumber + '" tabindex="0" role="button">' +
                        '<span class="fmt-title">' + esc(p.label) + '</span>' +
                        '<span class="fmt-page">p. ' + p.pageNumber + '</span></li>';
                }).join('');
            }
        }
    }

    function totalSpreads() {
        var n = state.pages.length;
        if (n === 0) return 1;
        // Spreads: [blank|1], [2|3], [4|5]… page 1 alone on right
        // Index 0: left empty, right = page 1 (pageNo 1)
        // Index 1: left page 2, right page 3
        return Math.ceil((n + 1) / 2);
    }

    function pagesForSpread(spreadIdx) {
        // spread 0 → left null, right pages[0]
        // spread 1 → left pages[1], right pages[2]
        if (spreadIdx <= 0) {
            return { left: null, right: state.pages[0] || null };
        }
        var leftIdx = spreadIdx * 2 - 1;
        var rightIdx = spreadIdx * 2;
        return {
            left: state.pages[leftIdx] || null,
            right: state.pages[rightIdx] || null
        };
    }

    function pageCssPadding(isVerso) {
        var m = state.margins;
        var b = state.bleed ? state.bleedIn : 0;
        var top = inchToPx(m.top + b);
        var bottom = inchToPx(m.bottom + b);
        var inside = inchToPx(m.inside + b);
        var outside = inchToPx(m.outside + b);
        // Verso (left): outside on left, inside on right (gutter toward spine)
        if (isVerso) {
            return top + 'px ' + inside + 'px ' + bottom + 'px ' + outside + 'px';
        }
        return top + 'px ' + outside + 'px ' + bottom + 'px ' + inside + 'px';
    }

    function renderPageFace(target, page, isVerso) {
        if (!target) return;
        target.classList.toggle('is-empty-face', !page);
        target.classList.toggle('has-bleed', !!state.bleed);
        target.classList.toggle('is-verso', !!isVerso);
        target.classList.toggle('is-recto', !isVerso);
        target.style.setProperty('--fmt-page-pad', pageCssPadding(isVerso));
        target.style.setProperty('--fmt-margin-top', inchToPx(state.margins.top) + 'px');
        target.style.setProperty('--fmt-margin-bottom', inchToPx(state.margins.bottom) + 'px');
        target.style.setProperty('--fmt-margin-inside', inchToPx(state.margins.inside) + 'px');
        target.style.setProperty('--fmt-margin-outside', inchToPx(state.margins.outside) + 'px');

        var bleedBand = state.bleed ? inchToPx(state.bleedIn) : 0;
        target.style.setProperty('--fmt-bleed', bleedBand + 'px');

        var guides =
            '<div class="fmt-page-guides" aria-hidden="true">' +
            '<div class="fmt-guide-bleed"></div>' +
            '<div class="fmt-guide-trim"></div>' +
            '<div class="fmt-guide-safe"></div>' +
            '</div>';

        if (!page) {
            target.innerHTML = guides + '<div class="fmt-spread-face-inner fmt-spread-blank-face"></div>';
            return;
        }

        var head = page.isBlank ? '' : (isVerso
            ? esc(state.bookTitle)
            : esc(page.chapterTitle || state.bookTitle));
        if (head.length > 48) head = head.slice(0, 45) + '…';

        var body = page.isBlank
            ? '<div class="fmt-spread-blank-label"> </div>'
            : '<div class="fmt-spread-body">' + page.html + '</div>';

        target.innerHTML =
            guides +
            '<div class="fmt-spread-face-inner' + (page.isBlank ? ' is-blank' : '') + '">' +
            '<div class="fmt-spread-running-head' + (page.isBlank || page.isChapterStart ? ' is-muted' : '') + '">' +
            (page.isChapterStart || page.isBlank ? '' : head) +
            '</div>' +
            body +
            '<div class="fmt-spread-folio">' + (page.isBlank ? '' : page.pageNo) + '</div>' +
            '</div>';

        applyImageConstraints(target);
    }

    function spineGapPx() {
        // Visible spine/gutter: inside margin + slight bump for thicker books
        var pages = Math.max(state.pages.length || 1, 1);
        var thicknessBoost = Math.min(28, Math.round(pages / 40));
        return Math.max(10, Math.round(inchToPx(state.margins.inside) * 0.9) + thicknessBoost);
    }

    function updateSpreadScale() {
        var host = els.host;
        var book = els.book;
        if (!host || !book) return;

        var bleed = state.bleed ? state.bleedIn : 0;
        var pageW = inchToPx(state.trimW + (bleed * 2));
        var pageH = inchToPx(state.trimH + (bleed * 2));
        var gap = spineGapPx();
        var naturalW = pageW * 2 + gap;
        var naturalH = pageH;

        book.style.setProperty('--fmt-face-w', pageW + 'px');
        book.style.setProperty('--fmt-face-h', pageH + 'px');
        book.style.setProperty('--fmt-spine-gap', gap + 'px');
        book.style.width = naturalW + 'px';
        book.style.height = naturalH + 'px';

        var spine = $('fmtSpineGap');
        if (spine) {
            spine.style.width = gap + 'px';
            spine.title = 'Spine / gutter ≈ ' + state.margins.inside.toFixed(3) + ' in (inside)';
        }

        var availW = Math.max(120, host.clientWidth - 16);
        var availH = Math.max(160, host.clientHeight - 16);
        var scale = Math.min(availW / naturalW, availH / naturalH, 1);
        book.style.transform = 'scale(' + scale + ')';
        book.style.transformOrigin = 'top center';

        var wrap = els.scaleWrap;
        if (wrap) {
            wrap.style.height = Math.ceil(naturalH * scale) + 'px';
            wrap.style.width = '100%';
        }
    }

    function renderSpread() {
        if (state.previewMode === 'ebook') {
            renderEbookScreen();
            return;
        }
        var pair = pagesForSpread(state.spreadIndex);
        renderPageFace(els.left, pair.left, true);
        renderPageFace(els.right, pair.right, false);
        updateSpreadScale();

        var max = Math.max(0, totalSpreads() - 1);
        if (els.prev) els.prev.disabled = state.spreadIndex <= 0;
        if (els.next) els.next.disabled = state.spreadIndex >= max;

        var label = $('fmtSpreadLabel');
        if (label) {
            var leftNo = pair.left ? pair.left.pageNo : '—';
            var rightNo = pair.right ? pair.right.pageNo : '—';
            label.textContent = 'Pages ' + leftNo + '–' + rightNo +
                ' · Spread ' + (state.spreadIndex + 1) + ' of ' + Math.max(1, totalSpreads());
        }

        var meta = $('fmtPreviewMeta');
        if (meta) {
            meta.textContent = state.pages.length
                ? (state.chapters.length + ' chapters · ' + state.pages.length + ' pages · ' +
                    state.trimW + '×' + state.trimH + ' in · gutter ' + state.margins.inside.toFixed(3) + '″')
                : 'No manuscript yet';
        }

        document.querySelectorAll('#fmtTocList li, #fmtPagesList li').forEach(function (li) {
            var p = parseInt(li.getAttribute('data-page') || '0', 10);
            var active = false;
            if (pair.left && pair.left.pageNo === p) active = true;
            if (pair.right && pair.right.pageNo === p) active = true;
            li.classList.toggle('is-active', active);
        });
    }

    function packEbookScreens(chapters) {
        var screenH = 520;
        var screenW = 340;
        var measure = createMeasureBox(screenW - 40, screenH - 40);
        measure.style.fontSize = state.fontPt + 'pt';
        measure.style.lineHeight = String(state.lineHeight);
        measure.style.padding = '0';
        measure.style.textAlign = 'left';

        var screens = [];
        var screenNo = 1;
        chapters.forEach(function (ch, chIdx) {
            var title = (ch.title || ('Chapter ' + (chIdx + 1))).trim();
            var chapterNo = ch.chapterNo || (chIdx + 1);
            var blocks = htmlToBlocks(ch.contentHtml || '');
            var opener = '<h2 class="fmt-ebook-ch-title">' + esc(title) + '</h2>';
            var acc = [];
            var first = true;
            function flush() {
                if (!acc.length && !first) return;
                var html = (first ? opener : '') + acc.join('');
                if (!html.trim()) return;
                screens.push({
                    screenNo: screenNo++,
                    chapterNo: chapterNo,
                    chapterTitle: title,
                    html: html,
                    isChapterStart: first
                });
                acc = [];
                first = false;
            }
            blocks.forEach(function (blockHtml) {
                measure.innerHTML = (first ? opener : '') + acc.join('') + blockHtml;
                applyImageConstraints(measure);
                if (measure.scrollHeight > screenH - 48 && acc.length) {
                    flush();
                    measure.innerHTML = (first ? opener : '') + blockHtml;
                    applyImageConstraints(measure);
                    acc = [blockHtml];
                    if (measure.scrollHeight > screenH - 48) flush();
                } else {
                    acc.push(blockHtml);
                }
            });
            flush();
        });
        measure.remove();
        return screens;
    }

    function renderEbookScreen() {
        var screenEl = $('fmtEbookScreen');
        if (!screenEl) return;
        var sc = state.screens[state.screenIndex];
        screenEl.style.fontSize = state.fontPt + 'pt';
        screenEl.style.lineHeight = String(state.lineHeight);
        if (!sc) {
            screenEl.innerHTML = '<p class="fmt-empty">Upload a manuscript to preview this ebook.</p>';
        } else {
            screenEl.innerHTML = sc.html;
            applyImageConstraints(screenEl);
        }
        var max = Math.max(0, state.screens.length - 1);
        if (els.prev) els.prev.disabled = state.screenIndex <= 0;
        if (els.next) els.next.disabled = state.screenIndex >= max;
        var label = $('fmtSpreadLabel');
        if (label) {
            label.textContent = state.screens.length
                ? ('Screen ' + (state.screenIndex + 1) + ' of ' + state.screens.length +
                    (sc ? (' · ' + sc.chapterTitle) : ''))
                : 'Screen';
        }
        var meta = $('fmtPreviewMeta');
        if (meta) {
            meta.textContent = state.screens.length
                ? (state.chapters.length + ' chapters · ' + state.screens.length + ' screens · reflowable')
                : 'No manuscript yet';
        }
        document.querySelectorAll('#fmtTocList li').forEach(function (li) {
            var ch = parseInt(li.getAttribute('data-chapter') || '0', 10);
            var page = parseInt(li.getAttribute('data-page') || '0', 10);
            var active = false;
            if (sc && ch && sc.chapterNo === ch) active = true;
            if (sc && !ch && page && sc.screenNo === page) active = true;
            // TOC uses data-page as start page in print; for ebook we also set data-chapter
            if (sc && li.getAttribute('data-chapter') == String(sc.chapterNo) && sc.isChapterStart && state.screenIndex === findEbookChapterStart(sc.chapterNo))
                active = true;
            if (sc && parseInt(li.getAttribute('data-chapter') || '0', 10) === sc.chapterNo)
                active = true;
            li.classList.toggle('is-active', active);
        });
        document.querySelectorAll('#fmtPagesList li').forEach(function (li) {
            var p = parseInt(li.getAttribute('data-page') || '0', 10);
            li.classList.toggle('is-active', !!(sc && sc.screenNo === p));
        });
    }

    function findEbookChapterStart(chapterNo) {
        for (var i = 0; i < state.screens.length; i++) {
            if (state.screens[i].chapterNo === chapterNo && state.screens[i].isChapterStart)
                return i;
        }
        return 0;
    }

    function goToPage(pageNo) {
        var n = parseInt(pageNo, 10);
        if (!Number.isFinite(n) || n < 1) return;
        if (state.previewMode === 'ebook') {
            // Prefer chapter jump via data-chapter on TOC; pageNo may be screenNo
            var idx = Math.max(0, Math.min(n - 1, state.screens.length - 1));
            // If TOC passed chapter start page from print structure, map by chapter title match
            state.screenIndex = idx;
            renderEbookScreen();
            return;
        }
        var spread = n === 1 ? 0 : Math.floor(n / 2);
        state.spreadIndex = Math.max(0, Math.min(spread, totalSpreads() - 1));
        renderSpread();
    }

    function goToChapter(chapterNo) {
        var ch = parseInt(chapterNo, 10);
        if (!Number.isFinite(ch) || ch < 1) return;
        if (state.previewMode === 'ebook') {
            state.screenIndex = findEbookChapterStart(ch);
            renderEbookScreen();
            return;
        }
        var page = state.toc.find(function (t) { return t.chapterNo === ch; });
        if (page) goToPage(page.startPage);
    }

    function applyPreviewModeUi() {
        var isPrint = state.previewMode === 'print';
        var root = $('fmtWorkspace');
        if (root) root.setAttribute('data-preview-mode', state.previewMode);
        var host = $('fmtSpreadHost');
        var ebook = $('fmtEbookHost');
        if (host) {
            host.classList.toggle('is-hidden', !isPrint);
            host.setAttribute('aria-hidden', isPrint ? 'false' : 'true');
        }
        if (ebook) {
            ebook.classList.toggle('is-hidden', isPrint);
            ebook.setAttribute('aria-hidden', isPrint ? 'true' : 'false');
        }
        var printSettings = $('fmtPrintSettings');
        if (printSettings) printSettings.style.display = isPrint ? '' : 'none';
        var trimWrap = $('fmtTrimLabelWrap');
        if (trimWrap) trimWrap.style.display = isPrint ? '' : 'none';
        var badge = $('fmtPreviewModeBadge');
        if (badge) {
            badge.textContent = isPrint
                ? ('Print preview (' + (state.binding || 'Paperback') + ')')
                : 'Ebook preview (Kindle-style)';
        }
        var pagesSection = $('fmtPagesScroll');
        if (pagesSection) pagesSection.style.display = isPrint ? '' : 'none';
        var pagesSplit = $('fmtPagesHeading') || document.querySelector('.fmt-section-split');
        if (pagesSplit) pagesSplit.style.display = isPrint ? '' : 'none';
    }

    function setPreviewMode(mode, binding) {
        state.previewMode = (mode === 'ebook') ? 'ebook' : 'print';
        if (binding) state.binding = binding;
        applyPreviewModeUi();
        repaginate();
    }

    function recommendedMargins(pageCount, bleed) {
        var meta = state.kdpMeta;
        var im = interiorMarginsSpec();
        var inside = gutterInsideIn(pageCount);

        var safeBleed = (meta && meta.safeFromTrimWithBleedIn != null)
            ? meta.safeFromTrimWithBleedIn
            : (im.recommendedOuterWithBleedIn ?? 0.375);
        var comfort = (meta && meta.comfortOuterNoBleedIn != null)
            ? meta.comfortOuterNoBleedIn
            : (im.recommendedOuterNoBleedIn ?? 0.25);

        if (meta && meta.defaults) {
            if (bleed) {
                var safe = Math.max(safeBleed, comfort);
                return { top: safe, bottom: safe, inside: inside, outside: safe };
            }
            return {
                top: Math.max(0.5, meta.defaults.top || 0.625),
                bottom: Math.max(0.5, meta.defaults.bottom || 0.875),
                inside: Math.max(inside, (meta.defaults.inside || 0.8125) * 0.9),
                outside: Math.max(0.5, meta.defaults.outside || 0.625)
            };
        }

        if (bleed) {
            var safeOuter = Math.max(safeBleed, comfort);
            return { top: safeOuter, bottom: safeOuter, inside: inside, outside: safeOuter };
        }
        return { top: 0.625, bottom: 0.875, inside: inside, outside: Math.max(0.5, comfort) };
    }

    function readSettingsFromUi() {
        var trimSel = $('fmtTrimSize');
        if (trimSel) {
            var opt = trimSel.options[trimSel.selectedIndex];
            var w = parseFloat(opt.getAttribute('data-w') || '6');
            var h = parseFloat(opt.getAttribute('data-h') || '9');
            state.trimW = w;
            state.trimH = h;
        }

        var bleedBleed = $('fmtBleedOn');
        state.bleed = !!(bleedBleed && bleedBleed.checked);

        var useKdp = $('fmtUseKdpMargins');
        state.useKdp = !!(useKdp && useKdp.checked);

        var fontPt = $('fmtFontPt');
        if (fontPt) state.fontPt = parseFloat(fontPt.value) || 11;
        var lsBtn = document.querySelector('#fmtLineSpacing button.active');
        if (lsBtn) {
            var ls = parseFloat(lsBtn.getAttribute('data-ls') || '');
            if (ls > 0) state.lineHeight = ls;
        }

        if (state.useKdp) {
            var est = Math.max(state.pages.length || 1, 120);
            state.margins = recommendedMargins(est, state.bleed);
            setMarginInputs(state.margins, true);
        } else {
            state.margins = {
                top: parseFloat(($('fmtMarginTop') || {}).value) || 0.625,
                bottom: parseFloat(($('fmtMarginBottom') || {}).value) || 0.875,
                inside: parseFloat(($('fmtMarginInside') || {}).value) || 0.625,
                outside: parseFloat(($('fmtMarginOutside') || {}).value) || 0.5
            };
            setMarginInputs(state.margins, false);
        }
    }

    function setMarginInputs(m, readOnly) {
        ['Top', 'Bottom', 'Inside', 'Outside'].forEach(function (name) {
            var el = $('fmtMargin' + name);
            if (!el) return;
            var key = name.toLowerCase();
            el.value = Number(m[key]).toFixed(4);
            el.readOnly = !!readOnly;
            el.classList.toggle('is-readonly', !!readOnly);
        });
    }

    function repaginate() {
        readSettingsFromUi();
        if (state.previewMode === 'ebook') {
            var lsBtnE = document.querySelector('#fmtLineSpacing button.active');
            if (lsBtnE) {
                var lsE = parseFloat(lsBtnE.getAttribute('data-ls') || '');
                if (lsE > 0) state.lineHeight = lsE;
                else state.lineHeight = 1.45 + Math.max(0, (state.fontPt - 11) * 0.02);
            } else {
                state.lineHeight = 1.45 + Math.max(0, (state.fontPt - 11) * 0.02);
            }
            state.screens = packEbookScreens(state.chapters);
            rebuildTocAndPageList([]);
            state.screenIndex = Math.min(state.screenIndex, Math.max(0, state.screens.length - 1));
            renderEbookScreen();
            return;
        }
        var pages = packBlocksIntoPages(state.chapters);
        rebuildTocAndPageList(pages);
        if (state.useKdp) {
            state.margins = recommendedMargins(Math.max(pages.length, 1), state.bleed);
            setMarginInputs(state.margins, true);
            pages = packBlocksIntoPages(state.chapters);
            rebuildTocAndPageList(pages);
        }
        state.spreadIndex = Math.min(state.spreadIndex, Math.max(0, totalSpreads() - 1));
        renderSpread();
    }

    function bindSidebarClicks() {
        function onNav(e) {
            var li = e.target.closest('li[data-page], li[data-chapter]');
            if (!li) return;
            var ch = li.getAttribute('data-chapter');
            if (state.previewMode === 'ebook' && ch) {
                goToChapter(ch);
                return;
            }
            goToPage(li.getAttribute('data-page'));
        }
        var toc = $('fmtTocList');
        var pages = $('fmtPagesList');
        if (toc) toc.addEventListener('click', onNav);
        if (pages) pages.addEventListener('click', onNav);
    }

    function init(options) {
        options = options || {};
        els.host = $('fmtSpreadHost');
        els.scaleWrap = $('fmtSpreadScaleWrap');
        els.book = $('fmtSpreadBook');
        els.left = $('fmtSpreadLeft');
        els.right = $('fmtSpreadRight');
        els.prev = $('fmtSpreadPrev');
        els.next = $('fmtSpreadNext');

        state.bookTitle = options.bookTitle || (document.querySelector('.fmt-header h1') || {}).textContent || '';
        state.kdpMeta = parseKdpMeta();
        if (state.kdpMeta && state.kdpMeta.bleedIn) state.bleedIn = state.kdpMeta.bleedIn;
        else state.bleedIn = kdpSpecs().bleedIn ?? 0.125;

        var root = $('fmtWorkspace');
        var modeOpt = options.previewMode || (root && root.getAttribute('data-preview-mode')) || 'print';
        var bindOpt = options.binding || (root && root.getAttribute('data-binding')) || 'Paperback';
        state.previewMode = (modeOpt === 'ebook') ? 'ebook' : 'print';
        state.binding = bindOpt;

        state.chapters = (options.chapters && options.chapters.length)
            ? options.chapters
            : parseChaptersJson();

        state.chapters = state.chapters.map(function (c, i) {
            return {
                chapterNo: c.chapterNo || c.ChapterNo || (i + 1),
                title: c.title || c.Title || ('Chapter ' + (i + 1)),
                contentHtml: c.contentHtml || c.ContentHtml || c.body || '',
                matter: c.matter || c.Matter || 'body'
            };
        });

        applyPreviewModeUi();
        bindSidebarClicks();

        if (els.prev) els.prev.addEventListener('click', function () {
            if (state.previewMode === 'ebook') {
                if (state.screenIndex > 0) { state.screenIndex--; renderEbookScreen(); }
                return;
            }
            if (state.spreadIndex > 0) { state.spreadIndex--; renderSpread(); }
        });
        if (els.next) els.next.addEventListener('click', function () {
            if (state.previewMode === 'ebook') {
                if (state.screenIndex < state.screens.length - 1) { state.screenIndex++; renderEbookScreen(); }
                return;
            }
            if (state.spreadIndex < totalSpreads() - 1) { state.spreadIndex++; renderSpread(); }
        });

        document.addEventListener('keydown', function (e) {
            if (e.target && /input|textarea|select/i.test(e.target.tagName)) return;
            if (e.key === 'ArrowLeft') { e.preventDefault(); if (els.prev) els.prev.click(); }
            if (e.key === 'ArrowRight') { e.preventDefault(); if (els.next) els.next.click(); }
        });

        ['fmtTrimSize', 'fmtBleedOn', 'fmtBleedOff', 'fmtUseKdpMargins',
            'fmtMarginTop', 'fmtMarginBottom', 'fmtMarginInside', 'fmtMarginOutside', 'fmtFontPt']
            .forEach(function (id) {
                var el = $(id);
                if (!el) return;
                el.addEventListener('change', function () { repaginate(); });
                if (el.tagName === 'INPUT' && el.type === 'number') {
                    el.addEventListener('input', debounce(repaginate, 350));
                }
            });

        var fontPresets = $('fmtFontPresets');
        if (fontPresets) {
            fontPresets.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-size]');
                if (!btn) return;
                fontPresets.querySelectorAll('button').forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                var map = { Small: '10', Medium: '11', Large: '12' };
                var pt = $('fmtFontPt');
                if (pt) pt.value = map[btn.getAttribute('data-size')] || '11';
                repaginate();
            });
        }

        var bookType = $('fmtBookType');
        if (bookType) {
            bookType.addEventListener('change', function () {
                var val = bookType.value || 'Ebook';
                var mode = (val === 'Ebook') ? 'ebook' : 'print';
                var label = $('fmtBindingLabel');
                if (label) label.textContent = val;
                if (root) {
                    root.setAttribute('data-binding', val);
                    root.setAttribute('data-preview-mode', mode);
                }
                setPreviewMode(mode, val);

                var bookId = parseInt((root && root.getAttribute('data-book-id')) || '0', 10);
                if (bookId > 0) {
                    fetch('/BookDesign/SaveBookFormatting', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                        body: JSON.stringify({ bookId: bookId, format: val })
                    }).catch(function () { /* non-blocking */ });
                }
            });
        }

        window.addEventListener('resize', debounce(function () {
            if (state.previewMode === 'print') updateSpreadScale();
        }, 100));

        var imgs = [];
        state.chapters.forEach(function (ch) {
            var tmp = document.createElement('div');
            tmp.innerHTML = ch.contentHtml || '';
            Array.prototype.forEach.call(tmp.querySelectorAll('img'), function (img) {
                imgs.push(img.src);
            });
        });
        preloadImages(imgs).then(function () { repaginate(); });

        global.FormattingBookSpread = {
            repaginate: repaginate,
            goToPage: goToPage,
            goToChapter: goToChapter,
            setPreviewMode: setPreviewMode,
            setChapters: function (chs) {
                state.chapters = (chs || []).map(function (c, i) {
                    return {
                        chapterNo: c.chapterNo || c.ChapterNo || (i + 1),
                        title: c.title || c.Title || ('Chapter ' + (i + 1)),
                        contentHtml: c.contentHtml || c.ContentHtml || c.body || c.text || '',
                        matter: c.matter || c.Matter || 'body'
                    };
                });
                state.spreadIndex = 0;
                state.screenIndex = 0;
                repaginate();
            },
            getState: function () { return state; }
        };
    }

    function preloadImages(urls) {
        var unique = Array.from(new Set((urls || []).filter(Boolean)));
        if (!unique.length) return Promise.resolve();
        return Promise.all(unique.map(function (src) {
            return new Promise(function (resolve) {
                var img = new Image();
                img.onload = img.onerror = function () { resolve(); };
                img.src = src;
            });
        }));
    }

    function debounce(fn, ms) {
        var t;
        return function () {
            var args = arguments;
            clearTimeout(t);
            t = setTimeout(function () { fn.apply(null, args); }, ms);
        };
    }

    global.FormattingBookSpreadInit = init;
    global.FormattingBookSpreadApplyLineSpacing = function (ls) {
        var v = parseFloat(ls);
        if (!(v > 0)) return;
        state.lineHeight = v;
        if (typeof global.FormattingBookSpread !== 'undefined' && global.FormattingBookSpread.repaginate) {
            global.FormattingBookSpread.repaginate();
        } else {
            // fallback: trigger control sync if available via change on font pt
            var pt = $('fmtFontPt');
            if (pt) pt.dispatchEvent(new Event('change', { bubbles: true }));
        }
    };
})(typeof window !== 'undefined' ? window : globalThis);
