(function (window, document) {
    "use strict";

    function prefersReduced() {
        return !!(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);
    }

    function money(n) {
        var v = Number(n);
        if (!isFinite(v)) v = 0;
        return "$" + v.toFixed(2);
    }

    function countTo(el, to, duration) {
        if (!el) return;
        duration = duration || 220;
        var target = Number(to) || 0;
        if (prefersReduced()) {
            el.textContent = money(target);
            el.setAttribute("data-value", String(target));
            return;
        }
        var from = parseFloat(el.getAttribute("data-value") || "0");
        if (!isFinite(from)) from = 0;
        var start = performance.now();
        function frame(now) {
            var t = Math.min(1, (now - start) / duration);
            var eased = 1 - Math.pow(1 - t, 2);
            var cur = from + (target - from) * eased;
            if (t >= 1) cur = target;
            el.textContent = money(cur);
            if (t < 1) {
                window.requestAnimationFrame(frame);
            } else {
                el.setAttribute("data-value", String(target));
            }
        }
        window.requestAnimationFrame(frame);
    }

    function syncReceipt(container, lines) {
        if (!container) return;
        lines = lines || [];
        var existing = {};
        Array.prototype.forEach.call(container.children, function (child) {
            var key = child.getAttribute("data-key");
            if (key) existing[key] = child;
        });
        var keep = {};
        lines.forEach(function (line) {
            keep[line.key] = true;
            var row = existing[line.key];
            if (row) {
                var amt = row.querySelector("[data-amt]");
                if (amt) amt.textContent = money(line.amount);
                var lab = row.querySelector("[data-lab]");
                if (lab) lab.textContent = line.label;
                return;
            }
            row = document.createElement("div");
            row.className = "calc-line" + (prefersReduced() ? "" : " is-enter");
            row.setAttribute("data-key", line.key);
            row.innerHTML = "<span data-lab></span><span data-amt class=\"pricing-money\"></span>";
            row.querySelector("[data-lab]").textContent = line.label;
            row.querySelector("[data-amt]").textContent = money(line.amount);
            container.appendChild(row);
            if (!prefersReduced()) {
                window.requestAnimationFrame(function () {
                    window.requestAnimationFrame(function () {
                        row.classList.remove("is-enter");
                    });
                });
            }
        });
        Array.prototype.forEach.call(Array.prototype.slice.call(container.children), function (child) {
            var key = child.getAttribute("data-key");
            if (key && !keep[key]) {
                if (prefersReduced()) {
                    child.remove();
                    return;
                }
                child.classList.add("is-exit");
                window.setTimeout(function () { child.remove(); }, 120);
            }
        });
    }

    function staggerIn(nodes, stepMs, maxItems) {
        stepMs = stepMs || 40;
        maxItems = maxItems == null ? 8 : maxItems;
        var list = Array.prototype.slice.call(nodes);
        if (prefersReduced()) {
            list.forEach(function (el) { el.classList.add("is-in"); });
            return;
        }
        list.forEach(function (el, i) {
            var delay = Math.min(i, Math.max(0, maxItems - 1)) * stepMs;
            window.setTimeout(function () { el.classList.add("is-in"); }, delay);
        });
    }

    window.PricingUI = {
        prefersReduced: prefersReduced,
        money: money,
        countTo: countTo,
        syncReceipt: syncReceipt,
        staggerIn: staggerIn
    };
})(window, document);
