(function () {
    "use strict";

    function boot() {
        var page = document.querySelector(".pricing-page");
        var toggle = document.getElementById("pricingSheetToggle");
        var overlay = document.getElementById("pricingSheetOverlay");
        if (!page || !toggle) return;

        function openSheet() {
            page.classList.add("is-sheet-open");
            toggle.setAttribute("aria-expanded", "true");
            if (overlay) overlay.hidden = false;
        }
        function closeSheet() {
            page.classList.remove("is-sheet-open");
            toggle.setAttribute("aria-expanded", "false");
            if (overlay) overlay.hidden = true;
        }
        function isMobile() {
            return window.matchMedia && window.matchMedia("(max-width: 959px)").matches;
        }

        toggle.addEventListener("click", function () {
            if (!isMobile()) return;
            if (page.classList.contains("is-sheet-open")) closeSheet();
            else openSheet();
        });
        if (overlay) overlay.addEventListener("click", closeSheet);
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape") closeSheet();
        });
        window.addEventListener("resize", function () {
            if (!isMobile()) closeSheet();
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
    else boot();
})();
