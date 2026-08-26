// Drag-to-rank for the review screen (Track 8 milestone 8.6.4).
//
// A progressive enhancement, not a requirement. The list is a form with a hidden `order` field and
// per-row Move up / Move down buttons that submit the same field, so ordering works with JavaScript
// disabled, on a screen reader, and on a phone where a long drag competes with page scroll. This
// script adds the drag affordance on top.
(function () {
    'use strict';

    var list = document.getElementById('risk-list');
    var order = document.getElementById('order');
    if (!list || !order) return;

    function syncOrder() {
        var ids = Array.prototype.map.call(
            list.querySelectorAll('[data-item-id]'),
            function (row) { return row.getAttribute('data-item-id'); });

        order.value = ids.join(',');
    }

    // --- Move up / Move down ------------------------------------------------------------------
    // These submit the form, so they have to reorder the DOM and rewrite the field *before* the
    // submission goes out. Hence preventDefault, then an explicit requestSubmit.
    list.addEventListener('click', function (event) {
        var button = event.target.closest('button[data-move]');
        if (!button) return;

        event.preventDefault();

        var row = button.closest('[data-item-id]');
        if (!row) return;

        if (button.getAttribute('data-move') === 'up') {
            var previous = row.previousElementSibling;
            if (previous) list.insertBefore(row, previous);
        } else {
            var next = row.nextElementSibling;
            if (next) list.insertBefore(next, row);
        }

        syncOrder();

        var form = document.getElementById('rank-form');
        if (form) form.requestSubmit();
    });

    // --- Drag and drop ------------------------------------------------------------------------
    var dragging = null;

    list.addEventListener('dragstart', function (event) {
        var row = event.target.closest('[data-item-id]');
        if (!row) return;

        dragging = row;
        row.classList.add('dragging');

        // Firefox will not start a drag without data on the transfer object.
        if (event.dataTransfer) {
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', row.getAttribute('data-item-id'));
        }
    });

    list.addEventListener('dragend', function () {
        if (dragging) dragging.classList.remove('dragging');
        dragging = null;
        syncOrder();
    });

    list.addEventListener('dragover', function (event) {
        if (!dragging) return;

        event.preventDefault();

        var over = event.target.closest('[data-item-id]');
        if (!over || over === dragging) return;

        // Insert before or after depending on which half of the row the pointer is over, so the drop
        // target does not flicker as the pointer crosses a boundary.
        var box = over.getBoundingClientRect();
        var after = (event.clientY - box.top) > (box.height / 2);

        list.insertBefore(dragging, after ? over.nextElementSibling : over);
    });

    syncOrder();
}());
