// GSAP Animations for Admin Dashboard
// This file provides smooth animations for all admin pages

// Make functions globally available
window.initPageAnimations = function() {
    // Animate cards
    gsap.utils.toArray('.summary-card, .chart-card, .activity-card, .kpi-card, .book-card').forEach((card, i) => {
        gsap.from(card, {
            scrollTrigger: {
                trigger: card,
                start: 'top 85%',
                toggleActions: 'play none none none'
            },
            duration: 0.6,
            opacity: 0,
            y: 30,
            ease: 'power2.out',
            delay: i * 0.05
        });
    });

    // Animate table rows
    gsap.utils.toArray('.table-row, .activity-row').forEach((row, i) => {
        gsap.from(row, {
            scrollTrigger: {
                trigger: row,
                start: 'top 90%',
                toggleActions: 'play none none none'
            },
            duration: 0.4,
            opacity: 0,
            x: -20,
            ease: 'power2.out',
            delay: i * 0.03
        });
    });

    // Animate progress bars
    gsap.utils.toArray('.progress-bar-fill, .progress-fill').forEach((bar) => {
        const width = bar.style.width || '0%';
        bar.style.width = '0%';
        gsap.to(bar, {
            width: width,
            duration: 1.2,
            ease: 'power2.out',
            delay: 0.3
        });
    });

    // Animate buttons on hover
    gsap.utils.toArray('.btn, .btn-primary, .btn-secondary').forEach((btn) => {
        btn.addEventListener('mouseenter', function() {
            gsap.to(this, {
                duration: 0.3,
                scale: 1.05,
                ease: 'power2.out'
            });
        });
        
        btn.addEventListener('mouseleave', function() {
            gsap.to(this, {
                duration: 0.3,
                scale: 1,
                ease: 'power2.out'
            });
        });
    });
};

// Smooth page transition
window.animatePageTransition = function(callback) {
    gsap.to('#contentArea', {
        duration: 0.3,
        opacity: 0,
        x: -20,
        ease: 'power2.in',
        onComplete: () => {
            if (callback) callback();
            gsap.to('#contentArea', {
                duration: 0.4,
                opacity: 1,
                x: 0,
                ease: 'power2.out'
            });
        }
    });
}

// Animate chart entrance
window.animateChart = function(chartElement) {
    gsap.from(chartElement, {
        duration: 0.8,
        opacity: 0,
        scale: 0.9,
        ease: 'back.out(1.7)',
        delay: 0.2
    });
}

// Stagger animation for lists
window.staggerList = function(listSelector, delay = 0.05) {
    gsap.from(listSelector, {
        duration: 0.5,
        opacity: 0,
        y: 20,
        stagger: delay,
        ease: 'power2.out'
    });
};

// Initialize on page load (wait for GSAP to be available)
(function() {
    function tryInit() {
        if (typeof gsap !== 'undefined' && typeof ScrollTrigger !== 'undefined') {
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', function() {
                    window.initPageAnimations();
                });
            } else {
                window.initPageAnimations();
            }
        } else {
            // Retry after a short delay if GSAP isn't loaded yet
            setTimeout(tryInit, 100);
        }
    }
    tryInit();
})();

