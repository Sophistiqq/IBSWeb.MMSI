$(document).ready(function () {
    const spinnerWrapper = $('.loader-container');

    if (!spinnerWrapper.parent().is('body')) {
        $('body').append(spinnerWrapper);
    }

    spinnerWrapper.hide();

    $(document).on('submit', 'form:not([data-download])', function (event) {
        if (event.isDefaultPrevented()) return;
        if ($(this).data('submitting')) return false;
        $(this).data('submitting', 'true');

        $(this).validate();
        if (!$(this).valid()) {
            $(this).removeData('submitting');
            return false;
        }

        spinnerWrapper.show();
        $('body').addClass('loading');
    });
});
