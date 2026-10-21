(function () {
    'use strict';

    function markHotkey(el) {
        var key = el.getAttribute('data-hotkey');
        if (!key || el.hasAttribute('data-hotkey-marked')) return;
        var children = Array.from(el.childNodes);
        for (var i = 0; i < children.length; i++) {
            var child = children[i];
            if (child.nodeType !== 3) continue;
            var text = child.textContent;
            if (!text.trim()) continue;
            var regex = new RegExp(key, 'i');
            var match = text.match(regex);
            if (match) {
                var idx = match.index;
                var span = document.createElement('span');
                span.appendChild(document.createTextNode(text.slice(0, idx)));
                var u = document.createElement('u');
                u.textContent = text.slice(idx, idx + 1);
                span.appendChild(u);
                span.appendChild(document.createTextNode(text.slice(idx + 1)));
                child.parentNode.replaceChild(span, child);
                break;
            }
        }
        el.setAttribute('data-hotkey-marked', 'true');
    }

    function handleKeydown(e) {
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        if ($(e.target).is('input,textarea,select,[contenteditable]')) return;
        var key = e.key.toLowerCase();
        if (key === 'escape') {
            if (document.querySelector('.modal.show, .swal2-container, dialog[open]')) return;
            if (window.location.pathname === '/' || window.location.pathname === '/MSAP/Home/Index') return;
            e.preventDefault();
            var backLink = document.querySelector('a[data-ui-back]');
            if (backLink) backLink.click();
            else history.back();
            return;
        }
        var el = document.querySelector('[data-hotkey="' + key + '"]');
        if (el) {
            e.preventDefault();
            el.click();
        }
    }

    $(document).ready(function () {
        document.querySelectorAll('[data-hotkey]').forEach(markHotkey);
        $(document).on('keydown', handleKeydown);
    });
})();
