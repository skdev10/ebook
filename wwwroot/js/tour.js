(function () {
    "use strict";

    function startReplay() {
        fetch("/Dashboard/ReplayTour", {
            method: "POST",
            credentials: "same-origin",
            headers: { Accept: "application/json" }
        })
            .then(function (r) { return r.ok ? r.json() : { success: false }; })
            .then(function () {
                if (typeof window.startGuidedTourForRoute === "function") {
                    window.startGuidedTourForRoute("onboarding", { silent: false, persistCompletion: true });
                    return;
                }
                window.location.href = "/Dashboard?replayTour=1";
            })
            .catch(function () {
                window.location.href = "/Dashboard?replayTour=1";
            });
    }

    window.EbookTour = { replay: startReplay };

    document.addEventListener("DOMContentLoaded", function () {
        var btn = document.getElementById("replayTourBtn");
        if (btn) btn.addEventListener("click", startReplay);

        var params = new URLSearchParams(window.location.search);
        if (params.get("replayTour") === "1" && typeof window.startGuidedTourForRoute === "function") {
            setTimeout(function () {
                window.startGuidedTourForRoute("onboarding", { silent: false, persistCompletion: true });
            }, 500);
        }
    });
})();
