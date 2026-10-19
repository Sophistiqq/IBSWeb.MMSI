/* Portable back links retain their href as the direct-entry/no-script fallback. */
(function () {
    'use strict';

    document.addEventListener('click', function (event) {
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target.closest?.('a[data-ui-back]');
        if (!link || link.hasAttribute('download') || (link.target && link.target !== '_self')) return;
        if (window.history.length <= 1) return;

        event.preventDefault();
        window.history.back();
    });
})();
