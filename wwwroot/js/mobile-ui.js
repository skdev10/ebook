/**
 * Mobile-only helpers. CSS and behaviour are gated to max-width 767px.
 * window.MOBILE_MQ is the single breakpoint; layout JS must use .matches, not innerWidth.
 */
(function () {
    "use strict";

    var MOBILE_MQ = window.matchMedia("(max-width: 767px)");
    window.MOBILE_MQ = MOBILE_MQ;

    var SKIP = ".cover-preview-root, .cover-preview-container, .paginated-reader-shell, .fmt-print-preview, .fmt-spread-host, .book-page-preview-shell";
    var FOCUSABLE = "a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]):not([type='hidden']), select:not([disabled]), [tabindex]:not([tabindex='-1'])";
    var INERT_SEL = [".main-content", ".m-topbar", ".m-tabbar"];
    var DRAWER_KEY = "mDrawer";

    var mobileBound = false;
    var historyPushed = false;
    var ignorePop = false;
    var pendingNavHref = null;
    var resizeTimer = null;

    function isMobile() {
        return MOBILE_MQ.matches;
    }

    function sidebarEl() {
        return document.getElementById("sidebar");
    }

    function menuToggleEl() {
        return document.getElementById("menuToggle");
    }

    function backdropEl() {
        return document.getElementById("sidebarBackdrop");
    }

    function isDrawerOpen() {
        var sb = sidebarEl();
        return !!(sb && sb.classList.contains("active"));
    }

    function scrollLock() {
        return window.ScrollLock;
    }

    function setInert(el, on) {
        if (!el) return;
        if ("inert" in el) {
            el.inert = on;
        }
        if (on) el.setAttribute("aria-hidden", "true");
        else el.removeAttribute("aria-hidden");
    }

    function setPageInert(on) {
        INERT_SEL.forEach(function (sel) {
            document.querySelectorAll(sel).forEach(function (el) {
                setInert(el, on);
            });
        });
    }

    function focusables(root) {
        if (!root) return [];
        return Array.prototype.filter.call(root.querySelectorAll(FOCUSABLE), function (el) {
            return el.offsetParent !== null || el === document.activeElement;
        });
    }

    function firstFocusable(root) {
        var list = focusables(root);
        return list.length ? list[0] : root;
    }

    function applyDrawerChrome(open) {
        var sb = sidebarEl();
        var mt = menuToggleEl();
        var bd = backdropEl();
        if (!sb) return;

        if (open) {
            sb.classList.add("active");
            sb.setAttribute("role", "dialog");
            sb.setAttribute("aria-modal", "true");
            if (!sb.hasAttribute("tabindex")) sb.setAttribute("tabindex", "-1");
            if (bd) {
                bd.classList.add("show");
                bd.setAttribute("aria-hidden", "false");
            }
            if (mt) {
                mt.setAttribute("aria-expanded", "true");
                mt.style.opacity = "0";
                mt.style.pointerEvents = "none";
            }
            setPageInert(true);
            var lock = scrollLock();
            if (lock && lock.lock) lock.lock();
            var target = firstFocusable(sb);
            if (target && typeof target.focus === "function") {
                try { target.focus(); } catch (e) { /* ignore */ }
            }
        } else {
            sb.classList.remove("active");
            sb.removeAttribute("aria-modal");
            if (sb.getAttribute("role") === "dialog") sb.removeAttribute("role");
            if (bd) {
                bd.classList.remove("show");
                bd.setAttribute("aria-hidden", "true");
            }
            if (mt) {
                mt.setAttribute("aria-expanded", "false");
                mt.style.opacity = "1";
                mt.style.pointerEvents = "auto";
            }
            setPageInert(false);
            var lock2 = scrollLock();
            if (lock2 && lock2.unlock) lock2.unlock();
            if (mt && typeof mt.focus === "function") {
                try { mt.focus(); } catch (e2) { /* ignore */ }
            }
        }
    }

    function consumeDrawerHistory() {
        if (!historyPushed) return;
        historyPushed = false;
        ignorePop = true;
        history.back();
    }

    function openDrawer() {
        if (!isMobile() || isDrawerOpen()) return;
        applyDrawerChrome(true);
        var st = history.state;
        if (!st || !st[DRAWER_KEY]) {
            history.pushState({ mDrawer: true }, "");
            historyPushed = true;
        } else {
            historyPushed = true;
        }
    }

    function closeDrawer(opts) {
        var fromPop = opts && opts.fromPopstate;
        if (!isDrawerOpen()) {
            if (historyPushed && !fromPop) consumeDrawerHistory();
            return;
        }
        applyDrawerChrome(false);
        if (historyPushed && !fromPop) consumeDrawerHistory();
        else historyPushed = false;
    }

    function toggleDrawer() {
        if (!isMobile()) return;
        if (isDrawerOpen()) closeDrawer();
        else openDrawer();
    }

    /**
     * Header cells fill `labels` by column index. A header colSpan of N
     * writes N entries so a later body cell still lines up.
     * Body cells with colSpan > 1 are skipped and `i` advances by that span.
     */
    function labelTables() {
        if (!isMobile()) {
            document.querySelectorAll(".main-content table.has-m-labels").forEach(function (t) {
                t.classList.remove("has-m-labels");
            });
            return;
        }
        document.querySelectorAll(".main-content table").forEach(function (table) {
            if (table.closest(SKIP) || table.closest("td, th")) return;
            var headRow = table.tHead && table.tHead.rows.length
                ? table.tHead.rows[table.tHead.rows.length - 1]
                : table.querySelector("tr");
            if (!headRow || !headRow.querySelector("th")) return;
            var labels = [];
            Array.prototype.forEach.call(headRow.cells, function (cell) {
                var n = cell.colSpan || 1;
                var text = (cell.textContent || "").replace(/\s+/g, " ").trim();
                for (var h = 0; h < n; h++) labels.push(text);
            });
            Array.prototype.forEach.call(table.rows, function (tr) {
                if (table.tHead && tr.parentElement === table.tHead) return;
                var i = 0;
                Array.prototype.forEach.call(tr.cells, function (td) {
                    var span = td.colSpan || 1;
                    if (td.tagName !== "TD") {
                        i += span;
                        return;
                    }
                    if (span > 1) {
                        i += span;
                        return;
                    }
                    if (!td.hasAttribute("data-label")) {
                        var label = labels[i] || "";
                        if (label) td.setAttribute("data-label", label);
                    }
                    i += 1;
                });
            });
            table.classList.add("has-m-labels");
        });
    }

    function onSidebarClick(e) {
        if (!isMobile() || !isDrawerOpen()) return;
        var a = e.target.closest("a[href]");
        if (!a) return;
        var href = a.getAttribute("href");
        if (!href || href === "#") return;
        e.preventDefault();
        pendingNavHref = a.href;
        applyDrawerChrome(false);
        if (historyPushed) {
            consumeDrawerHistory();
            return;
        }
        var go = pendingNavHref;
        pendingNavHref = null;
        window.location.href = go;
    }

    function onKeydown(e) {
        if (!isMobile() || !isDrawerOpen()) return;
        var sb = sidebarEl();
        if (e.key === "Escape") {
            closeDrawer();
            return;
        }
        if (e.key !== "Tab" || !sb) return;
        var list = focusables(sb);
        if (!list.length) {
            e.preventDefault();
            if (sb.focus) sb.focus();
            return;
        }
        var first = list[0];
        var last = list[list.length - 1];
        if (e.shiftKey) {
            if (document.activeElement === first || !sb.contains(document.activeElement)) {
                e.preventDefault();
                last.focus();
            }
        } else if (document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    }

    function onFocusIn(e) {
        if (!isMobile() || !isDrawerOpen()) return;
        var sb = sidebarEl();
        if (!sb || sb.contains(e.target)) return;
        var target = firstFocusable(sb);
        if (target && typeof target.focus === "function") {
            try { target.focus(); } catch (err) { /* ignore */ }
        }
    }

    function onDocClick(e) {
        if (!isMobile() || !isDrawerOpen()) return;
        var sb = sidebarEl();
        var mt = menuToggleEl();
        var bd = backdropEl();
        var clickToggle = mt && (mt === e.target || mt.contains(e.target));
        var clickBackdrop = bd && (bd === e.target || bd.contains(e.target));
        if (clickBackdrop) return;
        if (sb && !sb.contains(e.target) && !clickToggle) closeDrawer();
    }

    function onPopState() {
        if (ignorePop) {
            ignorePop = false;
            if (pendingNavHref) {
                var go = pendingNavHref;
                pendingNavHref = null;
                window.location.href = go;
            }
            return;
        }
        if (isDrawerOpen()) closeDrawer({ fromPopstate: true });
    }

    function onPageHide() {
        var lock = scrollLock();
        if (lock && lock.unlock) lock.unlock();
    }

    function bindMobileListeners() {
        if (mobileBound) return;
        var sb = sidebarEl();
        if (sb) sb.addEventListener("click", onSidebarClick);
        document.addEventListener("keydown", onKeydown);
        document.addEventListener("focusin", onFocusIn);
        document.addEventListener("click", onDocClick);
        mobileBound = true;
    }

    function unbindMobileListeners() {
        if (!mobileBound) return;
        var sb = sidebarEl();
        if (sb) sb.removeEventListener("click", onSidebarClick);
        document.removeEventListener("keydown", onKeydown);
        document.removeEventListener("focusin", onFocusIn);
        document.removeEventListener("click", onDocClick);
        mobileBound = false;
    }

    function enterMobile() {
        bindMobileListeners();
        labelTables();
        var mt = menuToggleEl();
        if (mt) {
            mt.setAttribute("aria-expanded", isDrawerOpen() ? "true" : "false");
            mt.setAttribute("aria-controls", "sidebar");
        }
    }

    function leaveMobile() {
        closeDrawer();
        var lock = scrollLock();
        if (lock && lock.unlock) lock.unlock();
        document.querySelectorAll(".main-content table.has-m-labels").forEach(function (t) {
            t.classList.remove("has-m-labels");
        });
        unbindMobileListeners();
        setPageInert(false);
        var mt = menuToggleEl();
        if (mt) {
            mt.setAttribute("aria-expanded", "false");
            mt.style.opacity = "1";
            mt.style.pointerEvents = "auto";
        }
        var sb = sidebarEl();
        var bd = backdropEl();
        if (sb) {
            sb.classList.remove("active");
            sb.removeAttribute("aria-modal");
            if (sb.getAttribute("role") === "dialog") sb.removeAttribute("role");
        }
        if (bd) {
            bd.classList.remove("show");
            bd.setAttribute("aria-hidden", "true");
        }
    }

    function onBreakpointChange() {
        if (MOBILE_MQ.matches) enterMobile();
        else leaveMobile();
    }

    function onResize() {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            if (MOBILE_MQ.matches) {
                if (!mobileBound) enterMobile();
                else labelTables();
            } else if (mobileBound) {
                leaveMobile();
            }
        }, 150);
    }

    function boot() {
        window.addEventListener("pagehide", onPageHide);
        window.addEventListener("popstate", onPopState);
        window.addEventListener("resize", onResize);
        if (MOBILE_MQ.addEventListener) MOBILE_MQ.addEventListener("change", onBreakpointChange);
        else if (MOBILE_MQ.addListener) MOBILE_MQ.addListener(onBreakpointChange);

        if (!MOBILE_MQ.matches) return;
        enterMobile();
    }

    window.MobileUi = {
        toggleDrawer: toggleDrawer,
        openDrawer: openDrawer,
        closeDrawer: closeDrawer,
        enterMobile: enterMobile,
        leaveMobile: leaveMobile,
        labelTables: labelTables,
        isMobile: isMobile
    };

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
    else boot();
})();
