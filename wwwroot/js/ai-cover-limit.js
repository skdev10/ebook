(function () {
    "use strict";

    var wrap = document.getElementById("aiCoverQuotaWrap");
    var badge = document.getElementById("aiCoverQuotaBadge");
    var upgrade = document.getElementById("aiCoverUpgradeCard");
    var uploadBtn = document.getElementById("aiCoverUploadOwnBtn");
    var generateBtn = document.getElementById("btnGenerate");
    var regenerateBtn = document.getElementById("btnRegenerate");

    if (!wrap || !badge) return;

    function toneClass(remaining) {
        if (remaining <= 0) return "is-gray";
        if (remaining === 1) return "is-red";
        if (remaining <= 3) return "is-orange";
        return "is-green";
    }

    function apply(data) {
        if (!data) return;
        var used = Number(data.used || 0);
        var limit = Number(data.limit || 5);
        var remaining = data.remaining != null ? Number(data.remaining) : Math.max(0, limit - used);
        var blocked = !!data.limitReached || remaining <= 0;

        wrap.hidden = false;
        badge.className = "ai-cover-quota__badge " + toneClass(remaining);
        badge.textContent = blocked
            ? "0 of " + limit + " free uses remaining"
            : remaining + " of " + limit + " free uses remaining";

        window.__aiCoverLimitReached = blocked;
        if (upgrade) upgrade.hidden = !blocked;
        [generateBtn, regenerateBtn].forEach(function (btn) {
            if (!btn) return;
            btn.classList.toggle("is-quota-locked", blocked);
            if (blocked) {
                btn.disabled = true;
                btn.setAttribute("aria-disabled", "true");
                btn.title = "You've used all " + limit + " free AI cover generations.";
            }
        });
    }

    function toastRemaining(data) {
        var remaining = Number(data && data.remaining != null ? data.remaining : 0);
        var limit = Number(data && data.limit ? data.limit : 5);
        var msg = remaining <= 0
            ? "Cover generated! You've used all " + limit + " free uses."
            : "Cover generated! " + remaining + " of " + limit + " free uses remaining.";
        if (typeof window.toast === "function") window.toast("success", msg);
        else if (window.Swal) window.Swal.fire({ toast: true, icon: "success", title: msg, timer: 2800, showConfirmButton: false, position: "top-end" });
    }

    function onGenerated(data) {
        apply(data);
        toastRemaining(data);
    }

    function load() {
        fetch("/Dashboard/CoverQuota", { credentials: "same-origin", headers: { Accept: "application/json" } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) { if (d && d.success !== false) apply(d); })
            .catch(function () { /* keep generate usable if quota endpoint is down */ });
    }

    if (uploadBtn) {
        uploadBtn.addEventListener("click", function () {
            var drop = document.getElementById("covRefImageBlock") || document.getElementById("covRefImageInput");
            if (drop && drop.scrollIntoView) drop.scrollIntoView({ behavior: "smooth", block: "center" });
            var input = document.getElementById("covRefImageInput");
            if (input) input.click();
        });
    }

    window.AiCoverLimit = { apply: apply, onGenerated: onGenerated, load: load };
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", load);
    else load();
})();
