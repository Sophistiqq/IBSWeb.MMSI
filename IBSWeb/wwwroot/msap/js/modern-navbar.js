/**
 * modern-navbar.js
 * Modern mega-menu navbar controller (always-on).
 *
 * Features:
 *  - Mega-menu open/close (click + Escape)
 *  - Spotlight search — scans all nav links, opens a results dropdown
 *  - Suppresses Quick Access sidebar (superseded by modern nav)
 *  - Mobile drawer with flat link list
 *  - User dropdown menu
 */
(function () {
    'use strict';

    /* ══════════════════════════════════════════════════════
       Quick Access sidebar — always suppressed
    ══════════════════════════════════════════════════════ */
    function suppressQuickAccess() {
        var panel   = document.getElementById('qa-panel');
        var trigger = document.getElementById('qa-nav-trigger');
        if (panel)   { panel.classList.add('qa-hidden'); panel.style.display = 'none'; }
        if (trigger) { trigger.style.display = 'none'; }
    }

    /* ══════════════════════════════════════════════════════
       Mega-menu open/close
    ══════════════════════════════════════════════════════ */
    function setupMegaMenus() {
        var overlay = document.getElementById('mnav-overlay');
        var items   = document.querySelectorAll('.mnav-item[data-mega]');

        function closeAll() {
            items.forEach(function (i) { i.classList.remove('mnav-open'); });
            if (overlay) overlay.classList.remove('active');
        }

        items.forEach(function (item) {
            var trigger = item.querySelector('.mnav-trigger');
            if (!trigger) return;
            trigger.addEventListener('click', function (e) {
                e.stopPropagation();
                var wasOpen = item.classList.contains('mnav-open');
                closeAll();
                if (!wasOpen) {
                    item.classList.add('mnav-open');
                    if (overlay) overlay.classList.add('active');
                }
            });
        });

        if (overlay) overlay.addEventListener('click', closeAll);
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeAll(); });
    }

    /* ══════════════════════════════════════════════════════
       Spotlight search
    ══════════════════════════════════════════════════════ */

    function collectAllNavLinks() {
        var seen  = new Set();
        var links = [];

        function sanitize(url) {
            if (!url || url === '#' || url === '') return null;
            try {
                var p = new URL(url, window.location.origin);
                if (p.origin !== window.location.origin) return null;
                return p.pathname + p.search;
            } catch { return null; }
        }

        function getCleanLabel(anchor) {
            var titleEl = anchor.querySelector('.mnav-link-title, .font-title-md, .title-md');
            if (titleEl) return titleEl.textContent.trim();

            var clone = anchor.cloneNode(true);
            clone.querySelectorAll('.material-symbols-outlined, i, svg, .mnav-link-sub, .text-outline').forEach(function (el) { el.remove(); });
            clone.querySelectorAll('div, span, p').forEach(function (el) {
                if (el.classList.contains('text-[10px]')) el.remove();
            });
            return clone.textContent.trim().replace(/\s+/g, ' ');
        }

        function push(anchor) {
            var raw = anchor.getAttribute('href') || '';
            var url = sanitize(raw);
            if (!url) return;
            if (url === '/' || url.toLowerCase().includes('/home/')) return;
            if (seen.has(url)) return;
            seen.add(url);

            var label = getCleanLabel(anchor);
            if (!label) return;

            var section = '';
            var el = anchor.closest('.mnav-link-list, .mnav-mega');
            if (el) {
                var heading = el.querySelector('.mnav-section-label') || el.previousElementSibling;
                if (heading && heading.classList.contains('mnav-section-label')) {
                    section = heading.textContent.trim();
                }
            }

            links.push({ url: url, label: label, section: section });
        }

        document.querySelectorAll('#modern-navbar a[href]:not([href="#"]):not([data-search-ignore])').forEach(push);

        return links;
    }

    function buildSearchDropdown() {
        var drop = document.createElement('div');
        drop.id = 'mnav-search-results';
        return drop;
    }

    function renderSearchResults(drop, query) {
        drop.innerHTML = '';

        if (!query) { drop.style.display = 'none'; return; }

        var q     = query.toLowerCase();
        var links = collectAllNavLinks().filter(function (l) {
            return l.label.toLowerCase().includes(q) || l.url.toLowerCase().includes(q);
        });

        if (links.length === 0) {
            drop.style.display = 'block';
            drop.innerHTML = '<div class="mnav-search-empty"><span class="material-symbols-outlined">search_off</span>No results for "<strong>' + escapeHtml(query) + '</strong>"</div>';
            return;
        }

        drop.style.display = 'block';

        var grouped = {};
        links.forEach(function (l) {
            var sec = l.section || 'Navigation';
            if (!grouped[sec]) grouped[sec] = [];
            grouped[sec].push(l);
        });

        Object.entries(grouped).forEach(function (_a) {
            var sec = _a[0], items = _a[1];
            var header = document.createElement('div');
            header.className = 'mnav-search-section-header';
            header.textContent = sec;
            drop.appendChild(header);

            items.forEach(function (l) {
                var a = document.createElement('a');
                a.href = l.url;
                a.className = 'mnav-search-result-item';
                a.innerHTML = '<span class="material-symbols-outlined">chevron_right</span><span>' + highlightMatch(escapeHtml(l.label), escapeHtml(q)) + '</span>';
                drop.appendChild(a);
            });
        });
    }

    function escapeHtml(str) {
        return str.replace(/[&<>"']/g, function (c) { return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]; });
    }

    function highlightMatch(label, query) {
        var idx = label.toLowerCase().indexOf(query.toLowerCase());
        if (idx === -1) return label;
        return label.slice(0, idx) + '<mark>' + label.slice(idx, idx + query.length) + '</mark>' + label.slice(idx + query.length);
    }

    function setupSearch() {
        var wrap  = document.querySelector('.mnav-search-wrap');
        var input = document.querySelector('.mnav-search');
        if (!wrap || !input) return;

        wrap.style.position = 'relative';

        var drop = buildSearchDropdown();
        wrap.appendChild(drop);

        input.addEventListener('input', function () {
            renderSearchResults(drop, input.value.trim());
        });

        input.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                input.value = ''; drop.style.display = 'none'; input.blur();
            }
            if (e.key === 'Enter') {
                var first = drop.querySelector('a');
                if (first) first.click();
            }
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                var items = [...drop.querySelectorAll('a')];
                if (!items.length) return;
                var focused = drop.querySelector('a:focus');
                var idx = focused ? items.indexOf(focused) : -1;
                var next = e.key === 'ArrowDown'
                    ? items[(idx + 1) % items.length]
                    : items[(idx - 1 + items.length) % items.length];
                next.focus();
            }
        });

        document.addEventListener('click', function (e) {
            if (!wrap.contains(e.target)) { drop.style.display = 'none'; }
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === '/' && document.activeElement.tagName !== 'INPUT' && document.activeElement.tagName !== 'TEXTAREA') {
                e.preventDefault();
                input.focus();
                input.select();
            }
        });
    }

    /* ══════════════════════════════════════════════════════
        Mobile drawer
    ══════════════════════════════════════════════════════ */
    function setupMobileDrawer() {
        var hamburger   = document.getElementById('mnav-hamburger');
        var drawer      = document.getElementById('mnav-drawer');
        var overlay     = document.getElementById('mnav-drawer-overlay');
        var drawerList  = drawer ? drawer.querySelector('.mnav-list') : null;
        if (!hamburger || !drawer || !drawerList || !overlay) return;

        var links   = collectAllNavLinks();
        var grouped = {};
        links.forEach(function (l) {
            var sec = l.section || 'Navigation';
            if (!grouped[sec]) grouped[sec] = [];
            grouped[sec].push(l);
        });

        drawerList.innerHTML = '';
        Object.keys(grouped).forEach(function (sec) {
            var header = document.createElement('div');
            header.className = 'mnav-section-label';
            header.textContent = sec;
            header.style.cssText = 'padding:12px 16px 4px;margin:0;border-bottom:none';
            drawerList.appendChild(header);

            grouped[sec].forEach(function (l) {
                var a = document.createElement('a');
                a.href = l.url;
                a.className = 'mnav-mega-link';
                a.textContent = escapeHtml(l.label);
                drawerList.appendChild(a);
            });
        });

        function open()  { document.body.classList.add('mnav-drawer-open'); overlay.classList.add('active'); }
        function close() { document.body.classList.remove('mnav-drawer-open'); overlay.classList.remove('active'); }

        hamburger.addEventListener('click', function () {
            if (document.body.classList.contains('mnav-drawer-open')) { close(); } else { open(); }
        });
        overlay.addEventListener('click', close);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && document.body.classList.contains('mnav-drawer-open')) close();
        });
        drawer.addEventListener('click', function (e) {
            if (e.target.closest('a[href]')) close();
        });
    }

    /* ══════════════════════════════════════════════════════
        User dropdown
    ══════════════════════════════════════════════════════ */
    function setupUserDropdown() {
        var trigger = document.getElementById('mnav-user-trigger');
        var menu    = document.getElementById('mnav-dropdown-menu');
        if (!trigger || !menu) return;

        function close() {
            menu.classList.remove('open');
            trigger.setAttribute('aria-expanded', 'false');
        }

        trigger.addEventListener('click', function (e) {
            e.stopPropagation();
            var isOpen = menu.classList.contains('open');
            if (isOpen) { close(); } else { menu.classList.add('open'); trigger.setAttribute('aria-expanded', 'true'); }
        });

        document.addEventListener('click', function (e) {
            if (!trigger.contains(e.target)) close();
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') close();
        });
    }

    /* ══════════════════════════════════════════════════════
        Init
    ══════════════════════════════════════════════════════ */
    document.addEventListener('DOMContentLoaded', function () {
        suppressQuickAccess();
        setupMegaMenus();
        setupSearch();
        setupMobileDrawer();
        setupUserDropdown();
    });
})();
