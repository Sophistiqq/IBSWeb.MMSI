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
        alert("Date From must be less than or equal to Date To");
        return false;
    }
    return true;
}

// Select2 Focus Hack
$(document).on('select2:open', () => {
    const searchField = document.querySelector('.select2-container--open .select2-search__field');
    if (searchField) searchField.focus();
});

// Document Ready Handlers
$(document).ready(function () {
    // legacy dataTable fallback
    if ($('#dataTable').length) {
        $('#dataTable').DataTable({
            stateSave: true,
            processing: true
        });
    }

    // Dynamic date sync in reports
    const $dateFrom = $('#DateFrom');
    const $dateTo = $('#DateTo');
    $dateFrom?.on('change', () => $dateTo.val($dateFrom.val()));

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
