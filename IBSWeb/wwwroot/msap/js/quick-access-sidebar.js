(function () {
    'use strict';

    const STORAGE_KEY = 'msap_qa_clicks';
    const STATE_KEY   = 'msap_qa_open';
    const MAX_TOP     = 10;
    const MIN_COUNT   = 1;

    function safeLocalGet(key, defaultValue) {
        try {
            return localStorage.getItem(key) ?? defaultValue;
        } catch {
            return defaultValue;
        }
    }

    function safeSessionGet(key, defaultValue) {
        try {
            return sessionStorage.getItem(key) ?? defaultValue;
        } catch {
            return defaultValue;
        }
    }

    function safeLocalSet(key, value) {
        try {
            localStorage.setItem(key, value);
        } catch {
            /* quota / security */
        }
    }

    function safeSessionSet(key, value) {
        try {
            sessionStorage.setItem(key, value);
        } catch {
            /* quota / security */
        }
    }

    function getClicks() {
        try { return JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}'); }
        catch { return {}; }
    }

    function saveClicks(data) {
        safeLocalSet(STORAGE_KEY, JSON.stringify(data));
    }

    function clearClicks() {
        try { localStorage.removeItem(STORAGE_KEY); }
        catch { /* quota / security */ }
    }

    function isPanelOpen() {
        return safeLocalGet(STATE_KEY, '') !== 'false';
    }

    function isHomePage() {
        return window.location.pathname === '/';
    }

    function getCurrentCompany() {
        const fromInput = (document.getElementById('hfCompany')?.value || '').trim();
        const fromData  = (
            document.documentElement.dataset.selectedCompany ||
            document.body.dataset.selectedCompany ||
            ''
        ).trim();
        return fromInput || fromData;
    }

    function getCompanyFromUrl(url) {
        const lower = (url || '').toLowerCase();
        if (lower.includes('/filpride/')) return 'Filpride';
        if (lower.includes('/mmsi/'))     return 'MMSI';
        return '';
    }

    function sanitizeUrl(url) {
        if (!url || typeof url !== 'string') return null;
        try {
            const parsed = new URL(url, window.location.origin);
            const protocol = parsed.protocol.toLowerCase();
            if (protocol !== 'http:' && protocol !== 'https:') return null;
            if (parsed.origin !== window.location.origin) return null;
            return parsed.pathname + parsed.search + parsed.hash;
        } catch {
            return null;
        }
    }

    function isAvailable(entry) {
        const current = getCurrentCompany();
        if (!current) return true;
        if (!entry.company) return true;
        return entry.company === current;
    }

    function isHomeUrl(url) {
        return url === '/' || url.toLowerCase().includes('/home/');
    }

    function getBreadcrumb(anchor) {
        const parts = [];
        let el = anchor.parentElement;

        while (el && !el.classList.contains('navbar-nav')) {
            if (el.classList.contains('dropdown-menu')) {
                const toggle = el.previousElementSibling ||
                    el.parentElement?.querySelector(':scope > .dropdown-toggle, :scope > .nav-link.dropdown-toggle');
                if (toggle) {
                    const text = (toggle.textContent || '').trim();
                    if (text) parts.unshift(text);
                }
            }
            el = el.parentElement;
        }

        return parts.join(' \u203A ');
    }

    function getAllNavLinks() {
        const seen  = new Set();
        const links = [];

        document.querySelectorAll(
            'nav.navbar a.dropdown-item:not([href="#"]):not([href=""]),' +
            'nav.navbar a.nav-link:not([href="#"]):not([href=""])'
        ).forEach(anchor => {
            const rawUrl  = anchor.getAttribute('href') || '';
            const url     = sanitizeUrl(rawUrl);
            const label   = (anchor.textContent || '').trim();
            if (!url || !label || isHomeUrl(url)) return;
            if (anchor.classList.contains('dropdown-toggle')) return;
            const company = getCompanyFromUrl(url);
            const key     = url + '|' + company;
            if (seen.has(key)) return;
            seen.add(key);
            links.push({
                url,
                label,
                breadcrumb : getBreadcrumb(anchor),
                company,
            });
        });

        return links;
    }

    const _tracked = new WeakSet();

    function attachTracking() {
        const navLinks = document.querySelectorAll(
            'nav.navbar a.nav-link:not([href="#"]):not([href=""]),' +
            'nav.navbar a.dropdown-item:not([href="#"]):not([href=""])'
        );

        navLinks.forEach(anchor => {
            if (_tracked.has(anchor)) return;
            _tracked.add(anchor);

            anchor.addEventListener('click', function () {
                const rawUrl     = this.getAttribute('href') || this.href;
                const url        = sanitizeUrl(rawUrl);
                const label      = (this.textContent || '').trim();
                const breadcrumb = getBreadcrumb(this);
                if (!url || !label) return;
                if (isHomeUrl(url)) return;
                recordClick(url, label, breadcrumb);
            });
        });
    }

    function recordClick(url, label, breadcrumb) {
        const data    = getClicks();
        const company = getCompanyFromUrl(url);
        if (!data[url]) {
            data[url] = { label, count: 0, company, breadcrumb: breadcrumb || '' };
        }
        data[url].count++;
        data[url].label     = label;
        data[url].company   = company;
        data[url].breadcrumb = breadcrumb || data[url].breadcrumb || '';
        saveClicks(data);
    }

    function makeItem(url, label, count, breadcrumb) {
        const safeUrl = sanitizeUrl(url);
        const a = document.createElement('a');
        // codeql[js/xss-through-dom] safeUrl is always a same-origin relative path produced by sanitizeUrl()
        a.href      = safeUrl !== null ? safeUrl : '#';
        a.className = 'qa-item';
        a.title     = count > 1 ? `${label} \u2014 visited ${count}\u00D7` : label;

        const labelEl = document.createElement('span');
        labelEl.className = 'qa-item-label';
        labelEl.textContent = label;
        a.appendChild(labelEl);

        if (breadcrumb) {
            const crumbEl = document.createElement('span');
            crumbEl.className = 'qa-item-crumb';
            crumbEl.textContent = breadcrumb;
            a.appendChild(crumbEl);
        }

        a.addEventListener('click', function (e) {
            e.stopPropagation();
            if (safeUrl) recordClick(safeUrl, label, breadcrumb);
            togglePanel();
        });

        return a;
    }

    function togglePanel() {
        const panel = document.getElementById('qa-panel');
        if (!panel) return;
        const isNowHidden = panel.classList.toggle('qa-hidden');
        safeLocalSet(STATE_KEY, isNowHidden ? 'false' : 'true');

        if (!isNowHidden) {
            const search = document.getElementById('qa-search');
            if (search) {
                const savedSearch = safeSessionGet('qa_search', '');
                search.value = savedSearch;
                renderList(savedSearch.toLowerCase());
                search.focus();
            }
        }
    }

    function renderList(filter) {
        const list   = document.getElementById('qa-list');
        if (!list) return;
        const clicks = getClicks();

        list.innerHTML = '';

        if (filter) {
            const allLinks = getAllNavLinks()
                .filter(l => isAvailable(l))
                .filter(l =>
                    l.label.toLowerCase().includes(filter) ||
                    l.url.toLowerCase().includes(filter)
                );

            if (allLinks.length > 0) {
                const label = document.createElement('div');
                label.className = 'qa-section-label';
                label.textContent = `Results (${allLinks.length})`;
                list.appendChild(label);

                allLinks.forEach(l => {
                    const clickData = clicks[l.url];
                    list.appendChild(makeItem(l.url, l.label, clickData?.count || null, l.breadcrumb));
                });
            } else {
                const empty = document.createElement('div');
                empty.className = 'qa-empty';
                empty.textContent = 'No links found.';
                list.appendChild(empty);
            }
            return;
        }

        const items = Object.entries(clicks)
            .filter(([, v]) => v.count >= MIN_COUNT && isAvailable(v))
            .sort((a, b) => b[1].count - a[1].count)
            .slice(0, MAX_TOP);

        if (items.length > 0) {
            const label = document.createElement('div');
            label.className = 'qa-section-label';
            label.textContent = 'Quick Access';
            list.appendChild(label);

            items.forEach(([url, v]) => {
                list.appendChild(makeItem(url, v.label, v.count, v.breadcrumb));
            });
        } else {
            const empty = document.createElement('div');
            empty.className = 'qa-empty';
            empty.textContent = 'Click nav links to start building quick access.';
            list.appendChild(empty);
        }
    }

    function buildSidebar() {
        const panel = document.createElement('div');
        panel.id = 'qa-panel';

        if (!isHomePage() && !isPanelOpen()) {
            panel.classList.add('qa-hidden');
        }

        panel.innerHTML = `
            <div class="qa-panel-header">
                <span>Quick Access</span>
                <button id="qa-close-btn" title="Close" aria-label="Close Quick Access sidebar">
                    <span class="material-symbols-outlined">close</span>
                </button>
            </div>
            <div class="qa-search-wrap">
                <input type="text" id="qa-search" placeholder="Search all links\u2026" autocomplete="off" />
            </div>
            <div class="qa-list-area" id="qa-list"></div>
            <div class="qa-footer">
                <button class="qa-clear-btn" id="qa-clear" title="Clear history">
                    <span class="material-symbols-outlined" style="font-size:16px;">delete</span> Reset history
                </button>
            </div>
        `;

        document.body.appendChild(panel);

        document.getElementById('qa-close-btn').addEventListener('click', togglePanel);
        document.getElementById('qa-clear').addEventListener('click', () => {
            if (confirm('Clear all Quick Access history?')) {
                clearClicks();
                renderList();
            }
        });
        document.getElementById('qa-search').addEventListener('input', function () {
            const term = this.value.trim().toLowerCase();
            safeSessionSet('qa_search', this.value.trim());
            renderList(term);
        });

        const savedSearch = safeSessionGet('qa_search', '');
        const searchInput = document.getElementById('qa-search');
        searchInput.value = savedSearch;
        renderList(savedSearch.toLowerCase());

        document.addEventListener('click', function (e) {
            const panel      = document.getElementById('qa-panel');
            const navTrigger = document.getElementById('qa-nav-trigger');
            if (!panel.classList.contains('qa-hidden') &&
                !panel.contains(e.target) &&
                !(navTrigger && navTrigger.contains(e.target))) {
                togglePanel();
            }
        });
    }

    function injectNavbarTrigger() {
        const navList = document.querySelector('nav.navbar ul.navbar-nav');
        if (!navList) return;

        const li = document.createElement('li');
        li.className = 'nav-item';
        li.id = 'qa-nav-trigger';

        const btn = document.createElement('a');
        btn.className = 'nav-link';
        btn.href = '#';
        btn.title = 'Quick Access';
        btn.setAttribute('aria-label', 'Toggle Quick Access sidebar');
        btn.innerHTML = '<span class="material-symbols-outlined">bolt</span>';
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            togglePanel();
        });

        li.appendChild(btn);
        navList.insertBefore(li, navList.firstChild);
    }

    function init() {
        buildSidebar();
        injectNavbarTrigger();
        attachTracking();

        const navbar = document.querySelector('nav.navbar');
        if (navbar) {
            const observer = new MutationObserver(() => attachTracking());
            observer.observe(navbar, { childList: true, subtree: true });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
