/**
 * ModernTable Helper
 * Simplifies DataTables initialization and standardizes UI components across the MSAP modules.
 */
const ModernTable = {
    // Generates a base configuration for DataTables
    config: function(options = {}) {
        const initComplete = options.initComplete;
        return $.extend(true, {
            dom: '<"flex-stack mb-2"lf><"modern-table-container"t><"flex-stack mt-2"ip>',
            pageLength: 10,
            processing: true,
            serverSide: true,
            stateSave: true,
            stateSaveCallback: function(settings, data) {
                const key = 'dt_' + window.location.pathname + '_' + settings.nTable.id;
                sessionStorage.setItem(key, JSON.stringify(data));
            },
            stateLoadCallback: function(settings) {
                const key = 'dt_' + window.location.pathname + '_' + settings.nTable.id;
                return JSON.parse(sessionStorage.getItem(key));
            },
            language: {
                search: "",
                searchPlaceholder: options.placeholder || "Search records...",
                lengthMenu: "_MENU_ per page",
                info: "Showing _START_ to _END_ of _TOTAL_ entries",
                processing: `<div class="modern-loader-inline"></div>`,
                emptyTable: "No records found matching the criteria."
            }
        }, options, {
            initComplete: function(settings, json) {
                const $dateFilter = $('#dateFilter');
                if ($dateFilter.length) $dateFilter.val(this.api().column(0).search());
                if (initComplete) initComplete.call(this, settings, json);
            }
        });
    },

    // Standard AJAX helper with CSRF protection
    ajax: function(url, dataCallback) {
        return {
            url: url,
            type: "POST",
            data: function(d) {
                d.__RequestVerificationToken = $('input[name="__RequestVerificationToken"]').val();
                return typeof dataCallback === 'function' ? dataCallback(d) : d;
            }
        };
    },

    // Reusable column renderers
    render: {
        date: (data) => data ? new Date(data).toLocaleDateString('en-US', { month: 'short', day: '2-digit', year: 'numeric' }) : '-',

        dateTime: (datePart, timePart) => {
            if (!datePart) return '-';
            return `${datePart} ${timePart || ''}`.trim();
        },

        currency: (data) => '₱' + parseFloat(data || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }),

        badge: (data, statusClass) => `<span class="modern-badge-sm ${statusClass || ''}">${data}</span>`,

        ellipsis: (data, maxWidth = '250px') => {
            if (!data) return '';
            return `<div style="max-width: ${maxWidth}; overflow: hidden; text-overflow: ellipsis;" title="${data}">${data}</div>`;
        },

        // Generates a standard modern action dropdown
        // items: Array of { label, icon, url, isError, onclick, condition }
        dropdown: function(items = []) {
            const filteredItems = items.filter(item => item === 'divider' || (item && (item.condition === undefined || item.condition === true)));

            if (filteredItems.length === 0) return '';

            let html = `
                <div class="relative inline-block text-left dropdown-container">
                    <button type="button" class="modern-btn-dropdown dropdown-trigger">
                        ACTIONS <span class="material-symbols-outlined" style="font-size:16px">arrow_drop_down</span>
                    </button>
                    <div class="modern-dropdown-menu dropdown-menu">`;

            filteredItems.forEach(item => {
                if (item === 'divider') {
                    html += `<div class="border-t border-outline-variant my-1"></div>`;
                    return;
                }
                const color = item.isError ? 'text-error' : '';
                html += `
                    <a class="modern-dropdown-item ${color}" href="${item.url || 'javascript:void(0)'}" ${item.onclick ? `onclick="${item.onclick}"` : ''}>
                        <span class="material-symbols-outlined">${item.icon}</span> ${item.label}
                    </a>`;
            });

            html += `</div></div>`;
            return html;
        }
    }
};

/**
 * Global UI Event Handlers for Modern Components
 */
$(document).ready(function() {
    // Action dropdown positioning logic (Delegated)
    $(document).on('click', '.dropdown-trigger', function(e) {
        e.preventDefault();
        e.stopImmediatePropagation();
        const $trigger = $(this);
        const $menu = $trigger.siblings('.modern-dropdown-menu');

        $('.modern-dropdown-menu').not($menu).removeClass('show');
        $menu.toggleClass('show');

        if ($menu.hasClass('show')) {
            const rect = $trigger[0].getBoundingClientRect();
            const menuWidth = $menu.outerWidth();

            $menu.css({
                'position': 'fixed',
                'top': (rect.bottom + 4) + 'px',
                'left': (rect.right - menuWidth) + 'px',
                'z-index': '9999'
            });

            // Flip logic if hitting bottom of viewport
            const menuHeight = $menu.outerHeight();
            if (rect.bottom + menuHeight > window.innerHeight) {
                $menu.css('top', (rect.top - menuHeight - 4) + 'px');
            }
        }
    });

    $(document).on('click', function() {
        $('.modern-dropdown-menu').removeClass('show');
    });

    // Handle scroll/resize to close open dropdowns (prevents disconnected menus)
    $(window).on('scroll resize', () => $('.modern-dropdown-menu.show').removeClass('show'));
});
