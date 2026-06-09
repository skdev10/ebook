(function () {
    'use strict';

    var configEl = document.getElementById('mobile-block-config');
    if (!configEl) return;

    var alertOpen = false;

    function getMaxWidth() {
        var maxWidth = parseInt(configEl.getAttribute('data-max-width') || '768', 10);
        return (!isFinite(maxWidth) || maxWidth <= 0) ? 768 : maxWidth;
    }

    function isNarrowViewport() {
        var maxWidth = getMaxWidth();
        if (typeof window.matchMedia === 'function') {
            return window.matchMedia('(max-width: ' + maxWidth + 'px)').matches;
        }
        return window.innerWidth <= maxWidth;
    }

    function isLikelyMobileUa() {
        var ua = navigator.userAgent || '';
        return /Android|iPhone|iPod|iPad|Windows Phone|IEMobile|Mobile|BlackBerry|BB10|Opera Mini|webOS|Kindle|Silk|Tablet/i.test(ua)
            || (/Macintosh/i.test(ua) && /Mobile\//i.test(ua));
    }

    function isMobileContext() {
        return isNarrowViewport() || isLikelyMobileUa();
    }

    function escapeHtml(text) {
        return String(text || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function showMobileAlert() {
        if (typeof Swal === 'undefined') return;
        if (alertOpen && Swal.isVisible()) return;

        var title = configEl.getAttribute('data-title') || 'Desktop Only';
        var message = configEl.getAttribute('data-message') || '';
        var subMessage = configEl.getAttribute('data-submessage') || '';

        alertOpen = true;
        document.body.classList.add('mobile-block-active');

        Swal.fire({
            icon: 'info',
            title: title,
            html: '<p class="mobile-block-swal-message">' + escapeHtml(message) + '</p>'
                + '<p class="mobile-block-swal-submessage">' + escapeHtml(subMessage) + '</p>',
            confirmButtonText: 'OK',
            confirmButtonColor: '#4f46e5',
            allowOutsideClick: false,
            allowEscapeKey: false,
            heightAuto: true,
            customClass: {
                popup: 'mobile-block-swal-popup',
                title: 'mobile-block-swal-title',
                htmlContainer: 'mobile-block-swal-html'
            }
        }).then(function () {
            alertOpen = false;
            if (isMobileContext()) {
                showMobileAlert();
            }
        });
    }

    function hideMobileAlert() {
        alertOpen = false;
        document.body.classList.remove('mobile-block-active');
        if (typeof Swal !== 'undefined' && Swal.isVisible()) {
            Swal.close();
        }
    }

    function evaluate() {
        if (isMobileContext()) {
            showMobileAlert();
        } else {
            hideMobileAlert();
        }
    }

    function init() {
        evaluate();
        window.addEventListener('resize', evaluate);
        window.addEventListener('orientationchange', function () {
            setTimeout(evaluate, 150);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
