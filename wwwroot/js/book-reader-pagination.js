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

    function pageInnerHtml(pageHtml) {
        var wrap = document.createElement('div');
        wrap.innerHTML = String(pageHtml || '');
        var block = wrap.querySelector('.reader-chapter-block');
        return block ? block.innerHTML : String(pageHtml || '');
    }

    function pageHasChapterStart(pageHtml) {
        return String(pageHtml || '').indexOf('data-chapter-start') >= 0;
    }

    /** True when a page body is only heading/opener chrome (no body paragraphs). */
    function isHeadingOnlyInner(innerHtml) {
        var tmp = document.createElement('div');
        tmp.innerHTML = String(innerHtml || '');
        var kids = Array.prototype.slice.call(tmp.children || []);
        if (!kids.length) {
            var text = (tmp.textContent || '').replace(/\s+/g, ' ').trim();
            return !text;
        }
        return kids.every(function (el) {
            return isHeadingElement(el) || isOpenerElement(el) || isSinkElement(el);
        });
    }

    function isHeadingOnlyPage(pageHtml) {
        if (isFrontMatterHtml(pageHtml)) return false;
        return isHeadingOnlyInner(pageInnerHtml(pageHtml));
    }

    /**
     * Never leave a page that only contains a heading/opener.
     * Merge forward into the next page, or backward into the previous page.
     */
    function mergeHeadingOnlyChapterBlockPages(pages) {
        if (!Array.isArray(pages) || !pages.length) return pages;
        var out = pages.slice();
        var guard = 0;
        while (guard++ < out.length + 8) {
            var moved = false;
            for (var i = 0; i < out.length; i++) {
                if (!isHeadingOnlyPage(out[i])) continue;
                var inner = pageInnerHtml(out[i]);
                if (!(inner || '').replace(/\s+/g, '').length) {
                    out.splice(i, 1);
                    moved = true;
                    break;
                }
                var keepStart = pageHasChapterStart(out[i]);
                if (i < out.length - 1 && !isFrontMatterHtml(out[i + 1])) {
                    var wrapNext = document.createElement('div');
                    wrapNext.innerHTML = out[i + 1];
                    var nextBlock = wrapNext.querySelector('.reader-chapter-block');
                    if (nextBlock) {
                        nextBlock.insertAdjacentHTML('afterbegin', inner);
                        if (keepStart) nextBlock.setAttribute('data-chapter-start', '1');
                        out[i + 1] = nextBlock.outerHTML;
                    } else {
                        out[i + 1] = wrapChapterBlock(inner + out[i + 1], keepStart);
                    }
                    out.splice(i, 1);
                    moved = true;
                    break;
                }
                if (i > 0 && !isFrontMatterHtml(out[i - 1])) {
                    var wrapPrev = document.createElement('div');
                    wrapPrev.innerHTML = out[i - 1];
                    var prevBlock = wrapPrev.querySelector('.reader-chapter-block');
                    if (prevBlock) {
                        prevBlock.insertAdjacentHTML('beforeend', inner);
                        out[i - 1] = prevBlock.outerHTML;
                    } else {
                        out[i - 1] = wrapChapterBlock(pageInnerHtml(out[i - 1]) + inner, pageHasChapterStart(out[i - 1]));
                    }
                    out.splice(i, 1);
                    moved = true;
                    break;
                }
            }
            if (!moved) break;
        }
        return out.filter(function (p) {
            return (pageInnerHtml(p) || '').replace(/\s+/g, '').length > 0 || isFrontMatterHtml(p);
        });
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
            // Never flush an empty page that only donated its heading.
            if (!(pulled.bodyWithoutHeading || '').replace(/\s+/g, '').length) {
                continue;
            }
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
        out = out.filter(function (p) {
            var w = document.createElement('div');
            w.innerHTML = p;
            var b = w.querySelector('.reader-chapter-block');
            return b && (b.textContent || '').trim().length > 0;
        });
        return mergeHeadingOnlyChapterBlockPages(out);
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
                if (n.classList && n.classList.contains('manuscript-keep-next')) {
                    units.push(n.outerHTML);
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

    function countWordsIn(el) {
        var n = 0;
        var walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
        var node;
        while ((node = walker.nextNode())) {
            var s = node.nodeValue || '';
            for (var i = 0; i < s.length; i++) {
                if (/\s/.test(s.charAt(i))) continue;
                while (i < s.length && !/\s/.test(s.charAt(i))) i++;
                n++;
                i--;
            }
        }
        return n;
    }

    function findWordEnd(el, wordsOnLeft) {
        var seen = 0;
        var walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
        var node;
        while ((node = walker.nextNode())) {
            var s = node.nodeValue || '';
            for (var i = 0; i < s.length; i++) {
                if (/\s/.test(s.charAt(i))) continue;
                while (i < s.length && !/\s/.test(s.charAt(i))) i++;
                seen++;
                if (seen === wordsOnLeft) return { node: node, offset: i };
                i--;
            }
        }
        return null;
    }

    /**
     * Split one element at a word boundary. Inline ancestors stay open on both halves.
     * Text of left + text of right equals the original text.
     */
    function splitElementAtWord(element, wordsOnLeft) {
        var point = findWordEnd(element, wordsOnLeft);
        if (!point) return null;

        function splitAt(el, textNode, offset) {
            var right = el.cloneNode(false);
            var onRight = false;
            var children = Array.prototype.slice.call(el.childNodes);
            for (var i = 0; i < children.length; i++) {
                var child = children[i];
                if (!onRight) {
                    if (child === textNode) {
                        var full = child.nodeValue || '';
                        var leftText = full.slice(0, offset);
                        var rightText = full.slice(offset);
                        child.nodeValue = leftText;
                        if (rightText) right.appendChild(document.createTextNode(rightText));
                        onRight = true;
                    } else if (child.nodeType === 1 && child.contains(textNode)) {
                        var rightChild = splitAt(child, textNode, offset);
                        if (rightChild && (rightChild.childNodes.length || (rightChild.textContent || '').length))
                            right.appendChild(rightChild);
                        onRight = true;
                    }
                } else {
                    right.appendChild(child);
                }
            }
            return right;
        }

        return splitAt(element, point.node, point.offset);
    }

    function sliceWords(sourceEl, from, to) {
        var host = document.createElement('div');
        host.appendChild(sourceEl.cloneNode(true));
        var el = host.firstElementChild;
        var total = countWordsIn(el);
        if (to < total) splitElementAtWord(el, to);
        if (from > 0) {
            var rest = splitElementAtWord(el, from);
            if (rest) el = rest;
        }
        return el ? el.outerHTML : '';
    }

    /**
     * Paginate one block without flattening inline tags to textContent.
     * fits(html) is true when that HTML fits the current page.
     */
    function paginateBlockPreservingInline(blockHtml, fits) {
        var html = String(blockHtml || '').trim();
        if (!html) return [];
        var tmp = document.createElement('div');
        tmp.innerHTML = html;
        var el = tmp.firstElementChild;
        if (!el || typeof fits !== 'function') return [html];
        var tag = (el.tagName || '').toLowerCase();
        if (/^h[1-6]$/.test(tag)) return [html];
        if (el.querySelector && el.querySelector('img,table,ul,ol,figure,svg,pre,video')) return [html];
        if (fits(html)) return [html];
        var total = countWordsIn(el);
        if (total < 2) return [html];
        var parts = [];
        var start = 0;
        var guard = 0;
        while (start < total && guard++ < total + 2) {
            var lo = start + 1;
            var hi = total;
            var best = start;
            while (lo <= hi) {
                var mid = (lo + hi) >> 1;
                if (fits(sliceWords(el, start, mid))) {
                    best = mid;
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }
            if (best <= start) best = Math.min(total, start + 1);
            parts.push(sliceWords(el, start, best));
            start = best;
        }
        return parts.length ? parts : [html];
    }

    global.BookReaderPagination = {
        isHeadingElement: isHeadingElement,
        isHeadingSegmentHtml: isHeadingSegmentHtml,
        isFrontMatterHtml: isFrontMatterHtml,
        isHeadingOnlyPage: isHeadingOnlyPage,
        pullTrailingHeadingFromInnerHtml: pullTrailingHeadingFromInnerHtml,
        stripTrailingHeadingsFromChapterBlockPages: stripTrailingHeadingsFromChapterBlockPages,
        mergeHeadingOnlyChapterBlockPages: mergeHeadingOnlyChapterBlockPages,
        splitAccBeforeFlush: splitAccBeforeFlush,
        flattenKeepTogetherUnits: flattenKeepTogetherUnits,
        fillUnderfilledPages: fillUnderfilledPages,
        splitElementAtWord: splitElementAtWord,
        sliceWords: sliceWords,
        paginateBlockPreservingInline: paginateBlockPreservingInline
    };
})(typeof window !== 'undefined' ? window : globalThis);
