// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Template-style selects (Choices.js). The original <select> stays in the form and keeps
// its value, name and change events (so onchange="this.form.submit()" still works).
// Selects whose options are rebuilt by page scripts carry data-no-choices and keep the
// native control, styled to match.
(function () {
    document.addEventListener('DOMContentLoaded', function () {
        if (typeof Choices === 'undefined') return;
        document.querySelectorAll('select:not([data-no-choices])').forEach(function (select) {
            if (select.multiple || (select.size && select.size > 1)) return;
            new Choices(select, {
                allowHTML: false,
                shouldSort: false,
                placeholder: false,
                itemSelectText: '',
                searchEnabled: select.options.length > 7,
                searchPlaceholderValue: 'Search…',
                position: 'auto'
            });
        });
    });
})();

// Client-side table pagination ("Default with Active" style from the Dreams Core template).
// Long list tables are split into pages of PAGE_SIZE rows. Tables that contain editable
// fields (attendance marking, grading, assigning) are skipped so every row stays visible
// while a form is being filled in. Add data-no-paginate to a table to opt out.
(function () {
    var PAGE_SIZE = 10;

    function pad(n) {
        return n < 10 ? '0' + n : String(n);
    }

    function hasEditableFields(table) {
        return table.querySelector('input:not([type=hidden]), select, textarea') !== null;
    }

    function pageWindow(current, total) {
        if (total <= 7) {
            return Array.from({ length: total }, function (_, i) { return i + 1; });
        }
        var pages = [1];
        var start = Math.max(2, current - 1);
        var end = Math.min(total - 1, current + 1);
        if (start > 2) pages.push('…');
        for (var p = start; p <= end; p++) pages.push(p);
        if (end < total - 1) pages.push('…');
        pages.push(total);
        return pages;
    }

    function paginate(table) {
        var body = table.tBodies[0];
        if (!body) return;
        var rows = Array.prototype.slice.call(body.rows);
        if (rows.length <= PAGE_SIZE) return;

        var totalPages = Math.ceil(rows.length / PAGE_SIZE);
        var current = 1;

        var pager = document.createElement('div');
        pager.className = 'table-pager';
        var info = document.createElement('span');
        var nav = document.createElement('nav');
        nav.setAttribute('aria-label', 'Table pages');
        var list = document.createElement('ul');
        list.className = 'pagination';
        nav.appendChild(list);
        pager.appendChild(info);
        pager.appendChild(nav);
        table.insertAdjacentElement('afterend', pager);

        function item(label, page, opts) {
            var li = document.createElement('li');
            li.className = 'page-item' + (opts.active ? ' active' : '') + (opts.disabled ? ' disabled' : '');
            var a = document.createElement('a');
            a.className = 'page-link';
            a.href = '#';
            a.textContent = label;
            if (opts.active) a.setAttribute('aria-current', 'page');
            if (!opts.disabled && page) {
                a.addEventListener('click', function (e) {
                    e.preventDefault();
                    show(page);
                });
            } else {
                a.addEventListener('click', function (e) { e.preventDefault(); });
            }
            li.appendChild(a);
            return li;
        }

        function show(page) {
            current = page;
            var from = (page - 1) * PAGE_SIZE;
            var to = Math.min(from + PAGE_SIZE, rows.length);
            rows.forEach(function (row, i) {
                row.style.display = i >= from && i < to ? '' : 'none';
            });

            info.textContent = 'Showing ' + (from + 1) + '–' + to + ' of ' + rows.length;

            list.innerHTML = '';
            list.appendChild(item('Pre', page - 1, { disabled: page === 1 }));
            pageWindow(page, totalPages).forEach(function (p) {
                if (p === '…') {
                    list.appendChild(item('…', null, { disabled: true }));
                } else {
                    list.appendChild(item(pad(p), p, { active: p === page }));
                }
            });
            list.appendChild(item('Next', page + 1, { disabled: page === totalPages }));
        }

        show(current);
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.content-area table.table').forEach(function (table) {
            if (table.hasAttribute('data-no-paginate') || hasEditableFields(table)) return;
            paginate(table);
        });
    });
})();
