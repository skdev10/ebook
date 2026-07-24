/**
 * Soft navigation helpers for module pages — no forced Writer → Format → Cover chain.
 * Unsaved-work warning only when EbookUnsavedGuard reports dirty = true.
 */
(function (global) {
    'use strict';

    function hasSwal() {
        return typeof global.Swal !== 'undefined';
    }

    function showUseDashboardAlert(dashboardUrl) {
        var url = dashboardUrl || '/Dashboard';
        var html = 'Pick a book from the <b>Dashboard</b> under <b>Continue Editing</b>, '
            + 'or open <b>AI Writer</b>, <b>Formatting</b>, or <b>Book Cover</b> from the sidebar.';

        if (!hasSwal()) {
            if (global.confirm(html.replace(/<[^>]+>/g, ''))) global.location.href = url;
            return Promise.resolve();
        }

        return global.Swal.fire({
            title: 'Choose a book',
            html: html,
            icon: 'info',
            showCancelButton: true,
            confirmButtonText: 'Go to Dashboard',
            cancelButtonText: 'Stay here',
            confirmButtonColor: '#0f766e',
            cancelButtonColor: '#64748b',
            focusCancel: true
        }).then(function (r) {
            if (r.isConfirmed) global.location.href = url;
        });
    }

    function showSkippedStepAlert(featureName, gotoUrl) {
        var name = (featureName && String(featureName).trim()) || 'This module';
        var html = name + ' needs a selected book.<br><br>'
            + 'Open it from the Dashboard, the sidebar, or <b>Continue Editing</b>. '
            + 'You can use Writer, Formatting, and Cover in any order.';

        if (!hasSwal()) {
            global.alert(html.replace(/<[^>]+>/g, ''));
            if (gotoUrl) global.location.href = gotoUrl;
            return Promise.resolve();
        }

        return global.Swal.fire({
            icon: 'info',
            title: 'Select a book first',
            html: html,
            confirmButtonText: gotoUrl ? 'Continue' : 'OK',
            confirmButtonColor: '#0f766e',
            showCancelButton: true,
            cancelButtonText: 'Go to Dashboard',
            cancelButtonColor: '#64748b',
            focusCancel: true
        }).then(function (r) {
            if (r.isConfirmed && gotoUrl) global.location.href = gotoUrl;
            else if (r.isDismissed && r.dismiss === global.Swal.DismissReason.cancel) {
                global.location.href = '/Dashboard';
            }
        });
    }

    function confirmLeaveToDashboard(targetUrl) {
        var url = targetUrl || '/Dashboard';
        // Soft leave: never block. Kick a background save if the page registered one.
        if (global.EbookUnsavedGuard && typeof global.EbookUnsavedGuard.confirmLeave === 'function') {
            return global.EbookUnsavedGuard.confirmLeave(url).then(function () {
                global.location.href = url;
                return true;
            });
        }
        global.location.href = url;
        return Promise.resolve(true);
    }

    function confirmNewBookWhileInProgress(customHtml) {
        var html = customHtml || ('Starting a <b>new book</b> while another draft is open may leave '
            + 'unsaved edits behind.<br><br>'
            + 'To continue an existing book, use <b>Dashboard → Continue Editing</b>.');

        if (!hasSwal()) {
            return Promise.resolve(global.confirm(html.replace(/<[^>]+>/g, '')));
        }

        return global.Swal.fire({
            title: 'Start a new book?',
            html: html,
            icon: 'question',
            showCancelButton: true,
            confirmButtonText: 'Start new book',
            cancelButtonText: 'Stay',
            confirmButtonColor: '#0f766e',
            cancelButtonColor: '#64748b',
            focusCancel: true,
            allowOutsideClick: false
        }).then(function (r) { return !!r.isConfirmed; });
    }

    global.FlowNavGuard = {
        FLOW_ORDER_HTML: 'AI Writer, Formatting, and Book Cover (any order)',
        showUseDashboardAlert: showUseDashboardAlert,
        showSkippedStepAlert: showSkippedStepAlert,
        confirmLeaveToDashboard: confirmLeaveToDashboard,
        confirmNewBookWhileInProgress: confirmNewBookWhileInProgress
    };
})(window);
