(function (window) {
    'use strict';

    var SPINE_PX_PER_INCH = 40;
    var MIN_SPINE_PX = 20;
    var SPINE_TEXT_THRESHOLD = 0.25;

    function renderCoverPanels(dims) {
        if (!dims) return;

        var spinePx = Math.max(MIN_SPINE_PX, Math.round(dims.spineInches * SPINE_PX_PER_INCH));
        var spinePanel = document.getElementById('panelSpine');
        var spineText = document.getElementById('spineText');
        var labelSpine = document.getElementById('labelSpine');
        var labelSpineValue = document.getElementById('labelSpineValue');

        if (spinePanel) {
            spinePanel.style.width = spinePx + 'px';
        }
        if (labelSpine) {
            labelSpine.style.width = spinePx + 'px';
        }
        if (spineText) {
            if (dims.spineInches >= SPINE_TEXT_THRESHOLD) {
                spineText.classList.remove('spine-hidden');
            } else {
                spineText.classList.add('spine-hidden');
            }
        }
        if (labelSpineValue) {
            labelSpineValue.textContent = dims.spineInches.toFixed(3) + '" (' + dims.spineMm.toFixed(2) + 'mm)';
        }
    }

    function updatePanelText(title, author, description) {
        var frontTitle = document.getElementById('frontTitle');
        var frontAuthor = document.getElementById('frontAuthor');
        var spineText = document.getElementById('spineText');
        var backDesc = document.getElementById('backDescription');

        if (frontTitle) frontTitle.textContent = title || 'My Book';
        if (frontAuthor) frontAuthor.textContent = author || 'Author Name';
        if (spineText) spineText.textContent = (title || 'MY BOOK').toUpperCase();
        if (backDesc) backDesc.textContent = description || 'Your book description will appear here. Add a compelling back-cover blurb to attract readers.';
    }

    function applyPalette(bg, text, accent, spine) {
        var root = document.documentElement;
        root.style.setProperty('--cover-bg', bg);
        root.style.setProperty('--cover-text', text);
        root.style.setProperty('--cover-accent', accent);
        root.style.setProperty('--cover-spine', spine);
    }

    function setFontSize(px) {
        document.documentElement.style.setProperty('--cover-font-size', px + 'px');
    }

    function setLineHeight(val) {
        document.documentElement.style.setProperty('--cover-line-height', String(val));
    }

    window.CoverPreview = {
        renderCoverPanels: renderCoverPanels,
        updatePanelText: updatePanelText,
        applyPalette: applyPalette,
        setFontSize: setFontSize,
        setLineHeight: setLineHeight
    };

})(window);
