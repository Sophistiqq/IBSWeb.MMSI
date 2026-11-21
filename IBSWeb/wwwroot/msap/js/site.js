// Core Utilities
function formatNumber(number) {
    if (number === null || number === undefined) return '0.00';
    return parseFloat(number).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatNumberToFour(number) {
    if (number === null || number === undefined) return '0.0000';
    return parseFloat(number).toLocaleString('en-US', { minimumFractionDigits: 4, maximumFractionDigits: 4 });
}

function parseNumber(formattedNum) {
    if (typeof formattedNum !== 'string') return parseFloat(formattedNum) || 0;
    return parseFloat(formattedNum.replace(/,/g, '')) || 0;
}

function validateDate() {
    let dateFrom = document.getElementById("dateFrom")?.value;
    let dateTo = document.getElementById("dateTo")?.value;
    if (dateFrom && dateTo && dateFrom > dateTo) {
        if (typeof ModernAlert !== 'undefined' && typeof ModernAlert.warning === 'function') {
            ModernAlert.warning('Date From must be less than or equal to Date To.');
        }
        return false;
    }
    return true;
}

// Document Ready Handlers
$(document).ready(function () {
    // Dynamic date sync in reports
    const $dateFrom = $('#DateFrom');
    const $dateTo = $('#DateTo');
    if ($dateFrom.length && $dateTo.length) {
        $dateFrom.on('change', () => $dateTo.val($dateFrom.val()));
    }

    // TIN Number Auto-formatting
    $(document).on('input', '.formattedTinNumberInput', function(e) {
        let value = e.target.value.replace(/-/g, '');
        let formattedValue = '';
        for (let i = 0; i < value.length; i++) {
            if (i === 3 || i === 6 || i === 9) formattedValue += '-';
            formattedValue += value[i];
        }
        if (formattedValue.length > 12) {
            formattedValue = formattedValue.substring(0, 12) + formattedValue.substring(12).replace(/-/g, '');
        }
        e.target.value = formattedValue;
    });

    $(document).on('keydown', '.formattedTinNumberInput', function(e) {
        if (e.key === 'Backspace' && e.target.value.endsWith('-')) {
            e.target.value = e.target.value.slice(0, -1);
        }
    });

    // Sidebar/Navbar dropend implementation
    $('.dropend').on('click', function(e) {
        e.stopPropagation();
        const $menu = $(this).find('.dropdown-menu');
        $('.dropend .dropdown-menu').not($menu).removeClass('show');
        $menu.toggleClass('show');
    });
});
