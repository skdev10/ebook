(function () {
    'use strict';

    var lastDims = null;
    var LINE_LABELS = { '1.4': 'Tight', '1.6': 'Normal', '1.8': 'Relaxed', '2': 'Loose', '2.0': 'Loose' };

    function getInputs() {
        var pagesEl = document.getElementById('Pages');
        var paperEl = document.getElementById('PaperType');
        var titleEl = document.getElementById('Title');
        var authorEl = document.getElementById('Author');
        var descEl = document.getElementById('Description');
        return {
            pages: pagesEl ? parseInt(pagesEl.value, 10) || 150 : 150,
            paper: paperEl ? paperEl.value : 'White paper',
            title: titleEl ? titleEl.value : '',
            author: authorEl ? authorEl.value : '',
            description: descEl ? descEl.value : ''
        };
    }

    function fetchDimensions() {
        var inp = getInputs();
        var pages = Math.max(24, Math.min(828, inp.pages));
        var url = '/Calculator/Dimensions?pages=' + pages + '&paper=' + encodeURIComponent(inp.paper);

        fetch(url)
            .then(function (r) { return r.json(); })
            .then(function (dims) {
                lastDims = dims;
                if (window.CoverPreview) {
                    window.CoverPreview.renderCoverPanels(dims);
                    window.CoverPreview.updatePanelText(inp.title, inp.author, inp.description);
                }
                updateSummary(dims, inp);
            })
            .catch(function (err) {
                console.error('Dimension fetch failed:', err);
            });
    }

    function updateSummary(dims, inp) {
        var sumSpine = document.getElementById('sumSpine');
        var sumTotalWidth = document.getElementById('sumTotalWidth');
        var sumHeight = document.getElementById('sumHeight');
        var sumPages = document.getElementById('sumPages');
        var sumPaper = document.getElementById('sumPaper');
        var sumSpineText = document.getElementById('sumSpineText');
        var sumSpineRow = document.getElementById('sumSpineRow');
        var warningDiv = document.getElementById('spineWarning');
        var warningText = document.getElementById('spineWarningText');

        if (sumSpine) sumSpine.textContent = dims.spineInches.toFixed(3) + '" (' + dims.spineMm.toFixed(2) + 'mm)';
        if (sumTotalWidth) sumTotalWidth.textContent = dims.totalWidthInches.toFixed(3) + '" (' + dims.totalWidthMm.toFixed(2) + 'mm)';
        if (sumHeight) sumHeight.textContent = dims.totalHeightInches.toFixed(3) + '" (' + dims.totalHeightMm.toFixed(2) + 'mm)';
        if (sumPages) sumPages.textContent = String(dims.pages);
        if (sumPaper) sumPaper.textContent = dims.paperType || inp.paper;
        if (sumSpineText) {
            sumSpineText.textContent = dims.spineInches >= 0.25 ? 'Visible' : 'Too narrow \u26A0';
        }

        if (sumSpineRow) {
            if (dims.spineInches < 0.25) sumSpineRow.classList.add('spine-warn');
            else sumSpineRow.classList.remove('spine-warn');
        }

        if (warningDiv && warningText) {
            if (dims.warning) {
                warningText.textContent = dims.warning;
                warningDiv.classList.remove('d-none');
            } else {
                warningDiv.classList.add('d-none');
            }
        }
    }

    function buildSummaryText() {
        if (!lastDims) return '';
        var inp = getInputs();
        return 'KDP Cover Dimensions\n' +
            'Spine: ' + lastDims.spineInches.toFixed(3) + '" (' + lastDims.spineMm.toFixed(2) + 'mm)\n' +
            'Total width: ' + lastDims.totalWidthInches.toFixed(3) + '" (' + lastDims.totalWidthMm.toFixed(2) + 'mm)\n' +
            'Height: ' + lastDims.totalHeightInches.toFixed(3) + '" (' + lastDims.totalHeightMm.toFixed(2) + 'mm)\n' +
            'Pages: ' + lastDims.pages + ' (' + (lastDims.paperType || inp.paper) + ')\n' +
            'Barcode: 2.000" x 1.200"';
    }

    function initSwatches() {
        var palette = document.getElementById('colorPalette');
        if (!palette) return;
        var swatches = palette.querySelectorAll('.swatch');
        swatches.forEach(function (sw) {
            sw.style.backgroundColor = sw.getAttribute('data-bg');
        });
        swatches.forEach(function (sw) {
            sw.addEventListener('click', function () {
                swatches.forEach(function (s) { s.classList.remove('active'); });
                sw.classList.add('active');
                if (window.CoverPreview) {
                    window.CoverPreview.applyPalette(
                        sw.getAttribute('data-bg'),
                        sw.getAttribute('data-text'),
                        sw.getAttribute('data-accent'),
                        sw.getAttribute('data-spine')
                    );
                }
            });
        });
        var first = palette.querySelector('.swatch.active');
        if (first && window.CoverPreview) {
            window.CoverPreview.applyPalette(
                first.getAttribute('data-bg'),
                first.getAttribute('data-text'),
                first.getAttribute('data-accent'),
                first.getAttribute('data-spine')
            );
        }
    }

    function initTextSize() {
        var group = document.getElementById('textSizeGroup');
        if (!group) return;
        var btns = group.querySelectorAll('button[data-size]');
        btns.forEach(function (btn) {
            btn.addEventListener('click', function () {
                btns.forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                var sz = parseInt(btn.getAttribute('data-size'), 10) || 13;
                if (window.CoverPreview) window.CoverPreview.setFontSize(sz);
            });
        });
    }

    function initLineSpacing() {
        var slider = document.getElementById('lineSpacing');
        var label = document.getElementById('lineSpacingLabel');
        if (!slider) return;
        function update() {
            var val = parseFloat(slider.value) || 1.6;
            var key = val.toFixed(1);
            if (key === '2.0') key = '2';
            if (label) label.textContent = LINE_LABELS[key] || key;
            if (window.CoverPreview) window.CoverPreview.setLineHeight(val);
        }
        slider.addEventListener('input', update);
        update();
    }

    function initDescCounter() {
        var desc = document.getElementById('Description');
        var counter = document.getElementById('descCounter');
        if (!desc || !counter) return;
        function upd() { counter.textContent = desc.value.length + '/500'; }
        desc.addEventListener('input', upd);
        upd();
    }

    function initCopyButton() {
        var btn = document.getElementById('btnCopyDims');
        var label = document.getElementById('btnCopyLabel');
        if (!btn) return;
        btn.addEventListener('click', function () {
            var text = buildSummaryText();
            if (!text) return;
            navigator.clipboard.writeText(text).then(function () {
                if (label) label.textContent = 'Copied!';
                setTimeout(function () { if (label) label.textContent = 'Copy dimensions'; }, 2000);
            }).catch(function () {
                if (label) label.textContent = 'Copy failed';
                setTimeout(function () { if (label) label.textContent = 'Copy dimensions'; }, 2000);
            });
        });
    }

    function initSpecSheet() {
        var btn = document.getElementById('btnSpecSheet');
        if (!btn) return;
        btn.addEventListener('click', function () {
            var inp = getInputs();
            var pages = Math.max(24, Math.min(828, inp.pages));
            var url = '/Calculator/SpecSheet?pages=' + pages +
                '&paper=' + encodeURIComponent(inp.paper) +
                '&title=' + encodeURIComponent(inp.title) +
                '&author=' + encodeURIComponent(inp.author);
            window.location.href = url;
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initSwatches();
        initTextSize();
        initLineSpacing();
        initDescCounter();
        initCopyButton();
        initSpecSheet();

        ['Pages', 'PaperType', 'Title', 'Author', 'Description'].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) el.addEventListener('input', fetchDimensions);
        });

        var paperEl = document.getElementById('PaperType');
        if (paperEl) paperEl.addEventListener('change', fetchDimensions);

        fetchDimensions();
    });

})();
