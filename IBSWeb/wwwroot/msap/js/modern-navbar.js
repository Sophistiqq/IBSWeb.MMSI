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
       Mega-menu open/close + viewport-edge clamping
    ══════════════════════════════════════════════════════ */
    function setupMegaMenus() {
        var overlay = document.getElementById('mnav-overlay');
        var items   = document.querySelectorAll('.mnav-item[data-mega]');

        function closeAll() {
            items.forEach(function (i) {
                i.classList.remove('mnav-open');
                var trig = i.querySelector('.mnav-trigger');
                if (trig) trig.setAttribute('aria-expanded', 'false');
            });
            if (overlay) overlay.classList.remove('active');
        }

        function positionMega(item) {
            var mega = item.querySelector('.mnav-mega');
            if (!mega) return;
            mega.classList.remove('mnav-mega-align-right');
            var rect = mega.getBoundingClientRect();
            if (rect.right > window.innerWidth - 16) {
                mega.classList.add('mnav-mega-align-right');
            }
        }

        items.forEach(function (item) {
            var trigger = item.querySelector('.mnav-trigger');
            if (!trigger) return;
            trigger.addEventListener('click', function (e) {
                e.stopPropagation();
                var wasOpen = item.classList.contains('mnav-open');
                closeAll();
                if (!wasOpen) {
                    positionMega(item);
                    item.classList.add('mnav-open');
                    trigger.setAttribute('aria-expanded', 'true');
                    if (overlay) overlay.classList.add('active');
                }
            });
        });

        if (overlay) overlay.addEventListener('click', closeAll);
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeAll(); });
    }

    /* ══════════════════════════════════════════════════════
       Responsive Inline-vs-Drawer Layout (ResizeObserver)
       Collapses automatically when the inline row no longer fits.
    ══════════════════════════════════════════════════════ */
    function setupResponsiveLayout(closeDrawer) {
        var header  = document.getElementById('modern-navbar');
        var navList = header ? header.querySelector('.mnav-nav-area .mnav-list') : null;
        if (!header || !navList) return;

        var lastCollapsedWidth = 0;

        function measure() {
            var isCurrentlyWide = header.classList.contains('is-wide');
            var currentWidth    = header.clientWidth;

            // Avoid layout thrashing: while collapsed, only remeasure when header grows
            if (!isCurrentlyWide && lastCollapsedWidth > 0 && currentWidth <= lastCollapsedWidth) {
                return;
            }

            header.classList.add('is-measuring');
            header.classList.add('is-wide');

            var fits = header.scrollWidth <= header.clientWidth;

            if (fits) {
                lastCollapsedWidth = 0;
                if (typeof closeDrawer === 'function') closeDrawer();
            } else {
                header.classList.remove('is-wide');
                lastCollapsedWidth = currentWidth;
            }

            window.requestAnimationFrame(function () {
                header.classList.remove('is-measuring');
            });
        }

        if (typeof ResizeObserver === 'function') {
            var ro = new ResizeObserver(function () {
                measure();
            });
            ro.observe(header);
            ro.observe(navList);
        } else {
            window.addEventListener('resize', measure);
        }

        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(function () {
                lastCollapsedWidth = 0;
                measure();
            });
        }

        measure();
    }

    /* ══════════════════════════════════════════════════════
       Spotlight search
    ══════════════════════════════════════════════════════ */

    function collectAllNavLinks(includeHome) {
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
            if (!includeHome && (url === '/' || url.toLowerCase().includes('/home/'))) return;
            if (seen.has(url)) return;
            seen.add(url);

            var label = getCleanLabel(anchor);
            if (!label) return;

            var section = anchor.getAttribute('data-nav-section') || '';
            if (!section) {
                var el = anchor.closest('.mnav-link-list, .mnav-mega');
                if (el) {
                    var heading = el.querySelector('.mnav-section-label') || el.previousElementSibling;
                    if (heading && heading.classList.contains('mnav-section-label')) {
                        section = heading.textContent.trim();
                    }
                }
            }

            var isCurrent = anchor.getAttribute('aria-current') === 'page';
            links.push({ url: url, label: label, section: section, isCurrent: isCurrent });
        }

        document.querySelectorAll('#modern-navbar .mnav-nav-area a[href]:not([href="#"]):not([data-search-ignore])').forEach(push);

        return links;
    }

    function buildSearchDropdown() {
        var drop = document.createElement('div');
        drop.id = 'mnav-search-results';
        return drop;
    }

    function renderSearchResults(drop, query, remoteItems, isLoading) {
        drop.innerHTML = '';

        if (!query) { drop.style.display = 'none'; return; }

        var q     = query.toLowerCase();
        var links = collectAllNavLinks().filter(function (l) {
            return l.label.toLowerCase().includes(q) || l.url.toLowerCase().includes(q);
        });
        var records = Array.isArray(remoteItems) ? remoteItems : [];

        if (links.length === 0 && records.length === 0) {
            drop.style.display = 'block';
            if (isLoading) {
                drop.innerHTML = '<div class="mnav-search-empty">Searching records for "<strong>' + escapeHtml(query) + '</strong>"...</div>';
            } else {
                drop.innerHTML = '<div class="mnav-search-empty"><span class="material-symbols-outlined">search_off</span>No results for "<strong>' + escapeHtml(query) + '</strong>"</div>';
            }
            return;
        }

        drop.style.display = 'block';

        var grouped = {};
        links.forEach(function (l) {
            var sec = l.section || 'Navigation';
            if (!grouped[sec]) grouped[sec] = [];
            grouped[sec].push({ url: l.url, label: l.label, icon: 'chevron_right', sub: '' });
        });
        records.forEach(function (r) {
            if (!r || !r.url || !r.label) return;
            var sec = r.section || 'Records';
            if (!grouped[sec]) grouped[sec] = [];
            grouped[sec].push({ url: r.url, label: r.label, icon: r.icon || 'description', sub: r.sub || '' });
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
                var subHtml = l.sub ? '<span class="mnav-search-sub">' + escapeHtml(l.sub) + '</span>' : '';
                a.innerHTML = '<span class="material-symbols-outlined">' + escapeHtml(l.icon) + '</span><span class="mnav-search-text"><span>' + highlightMatch(escapeHtml(l.label), escapeHtml(q)) + '</span>' + subHtml + '</span>';
                drop.appendChild(a);
            });
        });

        if (isLoading) {
            var loadingEl = document.createElement('div');
            loadingEl.className = 'mnav-search-section-header';
            loadingEl.textContent = 'Searching records...';
            drop.appendChild(loadingEl);
        }
    }

    function escapeHtml(str) {
        return String(str).replace(/[&<>"']/g, function (c) { return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]; });
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

        var globalSearchUrl = input.getAttribute('data-global-search-url') || '';
        var debounceTimer = null;
        var activeController = null;

        input.addEventListener('input', function () {
            var query = input.value.trim();
            if (debounceTimer) window.clearTimeout(debounceTimer);
            if (activeController) {
                activeController.abort();
                activeController = null;
            }

            if (!query) {
                renderSearchResults(drop, '', [], false);
                return;
            }

            var shouldFetch = Boolean(globalSearchUrl && query.length >= 2);
            renderSearchResults(drop, query, [], shouldFetch);

            if (shouldFetch) {
                debounceTimer = window.setTimeout(function () {
                    activeController = new AbortController();
                    fetch(globalSearchUrl + '?q=' + encodeURIComponent(query), {
                        credentials: 'same-origin',
                        signal: activeController.signal
                    })
                        .then(function (resp) { return resp.ok ? resp.json() : []; })
                        .then(function (items) {
                            if (input.value.trim() === query) {
                                renderSearchResults(drop, query, items, false);
                            }
                        })
                        .catch(function (err) {
                            if (err && err.name === 'AbortError') return;
                            if (input.value.trim() === query) {
                                renderSearchResults(drop, query, [], false);
                            }
                        });
                }, 220);
            }
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
        if (!hamburger || !drawer || !drawerList || !overlay) return null;

        var links   = collectAllNavLinks(true);
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
            drawerList.appendChild(header);

            grouped[sec].forEach(function (l) {
                var a = document.createElement('a');
                a.href = l.url;
                a.className = 'mnav-mega-link';
                a.textContent = l.label;
                if (l.isCurrent) a.setAttribute('aria-current', 'page');
                drawerList.appendChild(a);
            });
        });

        function open()  {
            document.body.classList.add('mnav-drawer-open');
            overlay.classList.add('active');
            hamburger.setAttribute('aria-expanded', 'true');
        }
        function close() {
            document.body.classList.remove('mnav-drawer-open');
            overlay.classList.remove('active');
            hamburger.setAttribute('aria-expanded', 'false');
        }

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

        return close;
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
        var closeDrawer = setupMobileDrawer();
        setupResponsiveLayout(closeDrawer);
        setupUserDropdown();
    });
})();
