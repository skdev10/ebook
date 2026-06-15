/**
 * Dashboard-first book flow — SweetAlert when users skip steps or risk losing work.
 */
(function (global) {
    'use strict';

    var FLOW_ORDER_HTML = '<b>AI Writer → Book Formatting → AI Cover Design → Publish</b>';

    function hasSwal() {
        return typeof global.Swal !== 'undefined';
    }

    function showUseDashboardAlert(dashboardUrl) {
        var url = dashboardUrl || '/Dashboard';
        var html = 'Open your book from the <b>Dashboard</b> under <b>Continue Editing</b>. '
            + 'That keeps your chapters, formatting, and cover work safe.<br><br>'
            + 'If you jump around without following the steps, <b>unsaved work can be lost or deleted</b>.';

        if (!hasSwal()) {
            if (global.confirm(html.replace(/<[^>]+>/g, ''))) global.location.href = url;
            return Promise.resolve();
        }

        return global.Swal.fire({
            title: 'Start from your Dashboard',
            html: html,
            icon: 'info',
            showCancelButton: true,
            confirmButtonText: 'Go to Dashboard',
            cancelButtonText: 'Stay here',
            confirmButtonColor: '#7c3aed',
            cancelButtonColor: '#64748b',
            focusCancel: true
        }).then(function (r) {
            if (r.isConfirmed) global.location.href = url;
        });
    }

    function showSkippedStepAlert(featureName, gotoUrl) {
        var name = (featureName && String(featureName).trim()) || 'This step';
        var html = name + ' is not available yet on this path.<br><br>'
            + 'Please follow the steps in order: ' + FLOW_ORDER_HTML + '.<br><br>'
            + '<b>Warning:</b> skipping ahead or opening the wrong screen can cause '
            + '<b>unsaved chapters, formatting, or cover work to be lost</b>. '
            + 'Always continue from the <b>Dashboard → Continue Editing</b> card for your book.';

        if (!hasSwal()) {
            global.alert(html.replace(/<[^>]+>/g, ''));
            if (gotoUrl) global.location.href = gotoUrl;
            return Promise.resolve();
        }

        var buttons = {
            icon: 'warning',
            title: 'Follow your book flow',
            html: html,
            confirmButtonText: gotoUrl ? 'Go to the right step' : 'OK',
            confirmButtonColor: '#7c3aed',
            showCancelButton: true,
            cancelButtonText: 'Go to Dashboard',
            cancelButtonColor: '#64748b',
            focusCancel: true
        };

        return global.Swal.fire(buttons).then(function (r) {
            if (r.isConfirmed && gotoUrl) global.location.href = gotoUrl;
            else if (r.isDismissed && r.dismiss === global.Swal.DismissReason.cancel) {
                global.location.href = '/Dashboard';
            }
        });
    }

    function confirmLeaveToDashboard(targetUrl) {
        var url = targetUrl || '/Dashboard';
        var isDirty = !!(global.EbookUnsavedGuard
            && typeof global.EbookUnsavedGuard.isDirty === 'function'
            && global.EbookUnsavedGuard.isDirty());

        if (isDirty && global.EbookUnsavedGuard && typeof global.EbookUnsavedGuard.confirmLeave === 'function') {
            return global.EbookUnsavedGuard.confirmLeave(url).then(function (ok) {
                if (ok) global.location.href = url;
                return ok;
            });
        }

        if (!hasSwal()) {
            global.location.href = url;
            return Promise.resolve(true);
        }

        return global.Swal.fire({
            title: 'Return to Dashboard?',
            html: 'Your book stays saved. Pick it again under <b>Continue Editing</b> to resume at the right step (' + FLOW_ORDER_HTML + ').',
            icon: 'question',
            showCancelButton: true,
            confirmButtonText: 'Go to Dashboard',
            cancelButtonText: 'Keep working',
            confirmButtonColor: '#7c3aed',
            cancelButtonColor: '#64748b',
            focusCancel: true
        }).then(function (r) {
            if (r.isConfirmed) {
                global.location.href = url;
                return true;
            }
            return false;
        });
    }

    function confirmNewBookWhileInProgress(customHtml) {
        var html = customHtml || ('Starting a <b>new book</b> while another draft is in progress can confuse your workflow. '
            + 'If you reset, <b>unsaved work on the current book may be deleted</b>.<br><br>'
            + 'To continue an existing book, use <b>Dashboard → Continue Editing</b> instead.');

        if (!hasSwal()) {
            return Promise.resolve(global.confirm(html.replace(/<[^>]+>/g, '')));
        }

        return global.Swal.fire({
            title: 'Start a new book?',
            html: html,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Start new book',
            cancelButtonText: 'Continue current book',
            confirmButtonColor: '#7c3aed',
            cancelButtonColor: '#64748b',
            focusCancel: true,
            allowOutsideClick: false
        }).then(function (r) { return !!r.isConfirmed; });
    }

    global.FlowNavGuard = {
        FLOW_ORDER_HTML: FLOW_ORDER_HTML,
        showUseDashboardAlert: showUseDashboardAlert,
        showSkippedStepAlert: showSkippedStepAlert,
        confirmLeaveToDashboard: confirmLeaveToDashboard,
        confirmNewBookWhileInProgress: confirmNewBookWhileInProgress
    };
})(window);
