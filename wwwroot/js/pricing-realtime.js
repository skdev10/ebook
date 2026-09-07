(function () {
    "use strict";

    function boot() {
        var slider = document.getElementById("calcPages");
        var number = document.getElementById("calcPagesNumber");
        var label = document.getElementById("calcPagesLabel");
        var template = document.getElementById("calcTemplate");
        var fmtHidden = document.getElementById("calcFmt");
        var panel = document.getElementById("pricingPanel");
        var linesEl = document.getElementById("pricingPanelLines") || document.getElementById("calcLines");
        var totalEl = document.getElementById("calcTotal");
        var sticky = document.getElementById("calcTotalSticky");
        var note = document.getElementById("pricingPanelNote");
        var timer = 0;
        var seq = 0;

        if (!slider) return;

        function money(n) {
            if (window.PricingUI) return window.PricingUI.money(n);
            return "$" + Number(n || 0).toFixed(2);
        }

        function exportType() {
            var checked = document.querySelector('input[name="calcExport"]:checked');
            return (checked && checked.value) || "ebook";
        }

        function isCustomCover() {
            var custom = document.getElementById("calcCover");
            return !!(custom && custom.checked);
        }

        function templateId() {
            var n = parseInt(String((template && template.value) || "0"), 10) || 0;
            if (fmtHidden) fmtHidden.checked = n > 0;
            return n;
        }

        function paint(d) {
            if (!d) return;
            var lines = [
                { key: "writing", label: "AI Writing · " + (d.writingLabel || (d.pageCount + " pages")), amount: d.writingCost },
                { key: "cover", label: "Book Cover · " + (d.coverLabel || ""), amount: d.coverCost },
                { key: "fmt", label: "Formatting · " + (d.formattingLabel || ""), amount: d.formattingCost },
                { key: "export", label: "Export · " + (d.exportLabel || ""), amount: d.exportCost }
            ];
            if (window.PricingUI && linesEl) window.PricingUI.syncReceipt(linesEl, lines);
            else if (linesEl) {
                linesEl.innerHTML = lines.map(function (l) {
                    var free = Number(l.amount) <= 0 ? " free" : "";
                    return '<div class="line-item calc-line" data-key="' + l.key + '"><span data-lab></span><span data-amt class="amount' + free + '"></span></div>';
                }).join("");
                Array.prototype.forEach.call(linesEl.children, function (row, i) {
                    row.querySelector("[data-lab]").textContent = lines[i].label;
                    row.querySelector("[data-amt]").textContent = money(lines[i].amount);
                });
            }
            if (linesEl) {
                Array.prototype.forEach.call(linesEl.querySelectorAll("[data-amt]"), function (el, i) {
                    el.classList.toggle("free", Number(lines[i].amount) <= 0);
                });
            }
            if (window.PricingUI && totalEl) window.PricingUI.countTo(totalEl, d.total || 0, 220);
            else if (totalEl) totalEl.textContent = money(d.total);
            if (sticky) sticky.textContent = money(d.total);
            if (note) {
                note.textContent = d.requiresPayment
                    ? "Pay at export only. This estimate is calculated on the server."
                    : "Total $0.00 — export downloads directly, no Stripe.";
            }
        }

        function clampPages(raw) {
            var n = parseInt(String(raw), 10);
            if (!isFinite(n) || n < 1) n = 1;
            var max = parseInt((slider && slider.max) || "400", 10) || 400;
            if (n > max) n = max;
            return n;
        }

        function syncPages(raw, source) {
            var n = clampPages(raw);
            if (slider && source !== "slider") slider.value = String(n);
            if (number && source !== "number") number.value = String(n);
            if (label) label.textContent = String(n);
            return n;
        }

        function payload() {
            return {
                pageCount: clampPages(slider && slider.value),
                isCustomCover: isCustomCover(),
                templateId: templateId(),
                exportType: exportType()
            };
        }

        function calculate() {
            var my = ++seq;
            if (panel) panel.classList.add("is-loading");
            fetch("/api/pricing/calculate", {
                method: "POST",
                credentials: "same-origin",
                headers: { "Content-Type": "application/json", Accept: "application/json" },
                body: JSON.stringify(payload())
            }).then(function (r) { return r.json(); }).then(function (d) {
                if (my !== seq) return;
                if (panel) panel.classList.remove("is-loading");
                if (d && d.success !== false) paint(d);
            }).catch(function () {
                if (my !== seq) return;
                if (panel) panel.classList.remove("is-loading");
            });
        }

        function debounceCalc() {
            syncPages(slider.value, "slider");
            window.clearTimeout(timer);
            timer = window.setTimeout(calculate, 400);
        }

        slider.addEventListener("input", debounceCalc);
        slider.addEventListener("pointerdown", function () { slider.classList.add("is-drag"); });
        window.addEventListener("pointerup", function () { slider.classList.remove("is-drag"); });
        if (number) {
            number.addEventListener("input", function () {
                syncPages(number.value, "number");
                window.clearTimeout(timer);
                timer = window.setTimeout(calculate, 400);
            });
            number.addEventListener("change", function () {
                syncPages(number.value, "number");
                calculate();
            });
        }

        ["calcCover", "calcCoverAi", "calcEbook", "calcPb", "calcHc", "calcTemplate", "calcFmt"].forEach(function (id) {
            var el = document.getElementById(id);
            if (!el) return;
            el.addEventListener("change", calculate);
        });

        document.querySelectorAll(".pricing-hero").forEach(function (el) {
            var i = parseInt(el.getAttribute("data-hero") || "0", 10);
            if (window.PricingUI && window.PricingUI.prefersReduced()) { el.classList.add("is-in"); return; }
            window.setTimeout(function () { el.classList.add("is-in"); }, Math.min(i, 8) * 100);
        });

        syncPages(slider.value, "slider");
        calculate();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
    else boot();
})();
