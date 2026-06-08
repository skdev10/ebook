/**
 * Keep-with-next for in-chapter headings — shared by AI Writer preview, Book Formatter, and PDF CSS source.
 */
(function (global) {
    'use strict';

    function isHeadingElement(el) {
        if (!el || el.nodeType !== 1) return false;
        var tag = (el.tagName || '').toLowerCase();
        if (/^h[1-6]$/.test(tag)) return true;
        var cls = el.className || '';
        return typeof cls === 'string' && cls.indexOf('manuscript-heading') >= 0;
    }

    function isHeadingSegmentHtml(html) {
        var s = String(html || '').trim();
        if (!s) return false;
        var tmp = document.createElement('div');
        tmp.innerHTML = s;
        var el = tmp.firstElementChild;
        return el && tmp.children.length === 1 && isHeadingElement(el);
    }

    /** Pull trailing h1–h6 / .manuscript-heading from inner HTML (not wrapped in reader-chapter-block). */
    function pullTrailingHeadingFromInnerHtml(innerHtml) {
        var html = String(innerHtml || '').trim();
        if (!html) return null;
        var tmp = document.createElement('div');
        tmp.innerHTML = html;
        var children = Array.prototype.slice.call(tmp.children || []);
        if (!children.length) return null;
        var last = children[children.length - 1];
        if (!isHeadingElement(last)) return null;
        var headingHtml = last.outerHTML;
        children.pop();
        var remain = children.map(function (n) { return n.outerHTML; }).join('');
        if (!remain.trim() && children.length === 0) {
            return { bodyWithoutHeading: '', headingHtml: headingHtml };
        }
        if (!remain.trim()) return null;
        return { bodyWithoutHeading: remain, headingHtml: headingHtml };
    }

    /** After paginating reader-chapter-block pages, move orphan headings to the next page. */
    function stripTrailingHeadingsFromChapterBlockPages(pages) {
        if (!Array.isArray(pages) || pages.length < 2) return pages;
        var out = pages.slice();
        for (var i = 0; i < out.length - 1; i++) {
            var wrap = document.createElement('div');
            wrap.innerHTML = out[i];
            var block = wrap.querySelector('.reader-chapter-block');
            if (!block) continue;
            var inner = block.innerHTML;
            var pulled = pullTrailingHeadingFromInnerHtml(inner);
            if (!pulled) continue;
            block.innerHTML = pulled.bodyWithoutHeading;
            out[i] = block.outerHTML;

            var wrapNext = document.createElement('div');
            wrapNext.innerHTML = out[i + 1];
            var nextBlock = wrapNext.querySelector('.reader-chapter-block');
            if (nextBlock) {
                nextBlock.insertAdjacentHTML('afterbegin', pulled.headingHtml);
                out[i + 1] = nextBlock.outerHTML;
            } else {
                out[i + 1] = '<div class="reader-chapter-block">' + pulled.headingHtml + out[i + 1] + '</div>';
            }
        }
        return out.filter(function (p) {
            var w = document.createElement('div');
            w.innerHTML = p;
            var b = w.querySelector('.reader-chapter-block');
            return b && (b.textContent || '').trim().length > 0;
        });
    }

    /** When packing segments, avoid flushing a page that ends with a lone heading. */
    function splitAccBeforeFlush(acc, nextSeg) {
        var pulled = pullTrailingHeadingFromInnerHtml(acc);
        if (!pulled) return { flushBody: acc, carry: '' };
        return { flushBody: pulled.bodyWithoutHeading, carry: pulled.headingHtml };
    }

    global.BookReaderPagination = {
        isHeadingElement: isHeadingElement,
        isHeadingSegmentHtml: isHeadingSegmentHtml,
        pullTrailingHeadingFromInnerHtml: pullTrailingHeadingFromInnerHtml,
        stripTrailingHeadingsFromChapterBlockPages: stripTrailingHeadingsFromChapterBlockPages,
        splitAccBeforeFlush: splitAccBeforeFlush
    };
})(typeof window !== 'undefined' ? window : globalThis);
