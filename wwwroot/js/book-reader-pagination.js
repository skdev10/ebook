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

    /** Pull trailing heading cluster (one or more headings) so they stay with the next page. */
    function pullTrailingHeadingFromInnerHtml(innerHtml) {
        var html = String(innerHtml || '').trim();
        if (!html) return null;
        var tmp = document.createElement('div');
        tmp.innerHTML = html;
        var children = Array.prototype.slice.call(tmp.children || []);
        if (!children.length) return null;
        var taken = [];
        while (children.length && isHeadingElement(children[children.length - 1])) {
            taken.unshift(children.pop());
        }
        if (!taken.length) return null;
        var headingHtml = taken.map(function (n) { return n.outerHTML; }).join('');
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

    function isOpenerElement(el) {
        if (!el || el.nodeType !== 1) return false;
        var cls = String(el.className || '');
        return cls.indexOf('fmt-chapter-opener') >= 0
            || cls.indexOf('writer-chapter-opener') >= 0
            || cls.indexOf('chapter-opener') >= 0;
    }

    function isSinkElement(el) {
        if (!el || el.nodeType !== 1) return false;
        return String(el.className || '').indexOf('fmt-chapter-sink') >= 0;
    }

    function isFrontMatterHtml(html) {
        var s = String(html || '');
        return s.indexOf('writer-cover-page') >= 0
            || s.indexOf('front-matter-page') >= 0
            || s.indexOf('data-page-kind="cover"') >= 0
            || s.indexOf('data-page-kind="title"') >= 0
            || s.indexOf('data-page-kind="copyright"') >= 0
            || s.indexOf('data-page-kind="toc"') >= 0;
    }

    function stripEmptyPlaceholders(html) {
        var wrap = document.createElement('div');
        wrap.innerHTML = String(html || '');
        wrap.querySelectorAll('.preview-empty-state, p.text-slate-500, p.text-gray-500').forEach(function (el) {
            var t = (el.textContent || '').replace(/\s+/g, ' ').trim();
            if (!t || /^no content\.?$/i.test(t) || /no generated chapter yet/i.test(t)) el.remove();
        });
        return wrap.innerHTML;
    }

    function innerOfChapterBlock(html) {
        var wrap = document.createElement('div');
        wrap.innerHTML = String(html || '');
        var block = wrap.querySelector('.reader-chapter-block');
        var inner = block ? block.innerHTML : String(html || '');
        return stripEmptyPlaceholders(inner);
    }

    function wrapChapterBlock(inner, keepStart) {
        var attr = keepStart ? ' data-chapter-start="1"' : '';
        return '<div class="reader-chapter-block"' + attr + '>' + inner + '</div>';
    }

    /**
     * Flatten page inner HTML into keep-together units:
     * chapter opener + first paragraph, in-chapter heading + following paragraph, else one block.
     */
    function flattenKeepTogetherUnits(innerHtml) {
        var root = document.createElement('div');
        root.innerHTML = String(innerHtml || '');
        var units = [];

        function pushHeadingWithNext(nodes, i) {
            var n = nodes[i];
            var html = n.outerHTML;
            var take = 1;
            var nxt = nodes[i + 1];
            if (nxt && nxt.classList && nxt.classList.contains('reader-page-body') && nxt.firstElementChild) {
                html += nxt.firstElementChild.outerHTML;
                nxt.firstElementChild.remove();
            } else if (nxt && !isHeadingElement(nxt) && !isOpenerElement(nxt) && !isSinkElement(nxt)
                && !(nxt.classList && nxt.classList.contains('reader-page-body'))) {
                html += nxt.outerHTML;
                take = 2;
            }
            units.push(html);
            return take;
        }

        function walk(parent) {
            var nodes = Array.prototype.slice.call(parent.children || []);
            for (var i = 0; i < nodes.length; i++) {
                var n = nodes[i];
                if (isSinkElement(n)) continue;
                if (n.classList && n.classList.contains('reader-page-body')) {
                    walk(n);
                    continue;
                }
                if (isOpenerElement(n)) {
                    units.push(n.outerHTML);
                    continue;
                }
                if (isHeadingElement(n)) {
                    i += pushHeadingWithNext(nodes, i) - 1;
                    continue;
                }
                units.push(n.outerHTML);
            }
        }

        walk(root);
        return units;
    }

    /**
     * Pack leftover space on each page with the next page's keep-together units
     * (including the next chapter opener). Stops when nothing more fits.
     */
    function fillUnderfilledPages(pages, chapterIdxs, opts) {
        opts = opts || {};
        var fits = opts.fits;
        if (typeof fits !== 'function' || !Array.isArray(pages) || pages.length < 2) {
            return { pages: pages, chapterIdxs: chapterIdxs };
        }
        var outP = pages.slice();
        var outC = Array.isArray(chapterIdxs) ? chapterIdxs.slice() : pages.map(function (_, i) { return i; });
        while (outC.length < outP.length) outC.push(outC.length ? outC[outC.length - 1] : 0);

        var i = 0;
        while (i < outP.length - 1) {
            if (isFrontMatterHtml(outP[i]) || isFrontMatterHtml(outP[i + 1])) {
                i++;
                continue;
            }
            var keepStart = String(outP[i]).indexOf('data-chapter-start') >= 0;
            var units = flattenKeepTogetherUnits(innerOfChapterBlock(outP[i + 1]));
            if (!units.length) {
                i++;
                continue;
            }
            var trial = innerOfChapterBlock(outP[i]);
            var took = 0;
            for (var u = 0; u < units.length; u++) {
                var unit = units[u];
                if (/fmt-chapter-opener/.test(unit) && String(trial).replace(/<[^>]+>/g, ' ').trim()) {
                    unit = unit.replace('fmt-chapter-opener', 'fmt-chapter-opener is-inline-start');
                }
                var nextTrial = trial + unit;
                if (fits(wrapChapterBlock(nextTrial, keepStart))) {
                    trial = nextTrial;
                    units[u] = unit;
                    took++;
                } else {
                    break;
                }
            }
            if (!took) {
                i++;
                continue;
            }
            outP[i] = wrapChapterBlock(trial, keepStart);
            var remain = units.slice(took).join('');
            if (!remain.trim()) {
                outP.splice(i + 1, 1);
                outC.splice(i + 1, 1);
            } else {
                var nextWasStart = String(outP[i + 1]).indexOf('data-chapter-start') >= 0 && took === 0;
                outP[i + 1] = wrapChapterBlock(remain, nextWasStart);
            }
            continue;
        }
        return { pages: outP, chapterIdxs: outC };
    }

    global.BookReaderPagination = {
        isHeadingElement: isHeadingElement,
        isHeadingSegmentHtml: isHeadingSegmentHtml,
        isFrontMatterHtml: isFrontMatterHtml,
        pullTrailingHeadingFromInnerHtml: pullTrailingHeadingFromInnerHtml,
        stripTrailingHeadingsFromChapterBlockPages: stripTrailingHeadingsFromChapterBlockPages,
        splitAccBeforeFlush: splitAccBeforeFlush,
        flattenKeepTogetherUnits: flattenKeepTogetherUnits,
        fillUnderfilledPages: fillUnderfilledPages
    };
})(typeof window !== 'undefined' ? window : globalThis);
