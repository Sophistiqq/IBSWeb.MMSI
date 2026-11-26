(function () {
    'use strict';

    var STORAGE_KEY = 'msap_whats_new_seen_version';
    var cachedReleases = null;
    var fetchPromise = null;
    var dialogEl = null;
    var showAllReleases = false;

    var CSS = ''
        + 'dialog.msap-wn-dialog{padding:0;border:1px solid var(--outline-variant);border-radius:14px;'
        +   'background:var(--surface-container-lowest,var(--surface));color:var(--on-surface);'
        +   'max-width:540px;width:calc(100vw - 32px);max-height:min(80vh,640px);'
        +   'box-shadow:0 18px 44px rgba(0,0,0,0.28);overflow:hidden;margin:auto;}'
        + 'dialog.msap-wn-dialog::backdrop{background:rgba(0,0,0,0.45);backdrop-filter:blur(2px);}'
        + '.msap-wn-shell{display:flex;flex-direction:column;max-height:min(80vh,640px);}'
        + '.msap-wn-header{display:flex;align-items:center;justify-content:space-between;gap:12px;'
        +   'padding:16px 20px;border-bottom:1px solid var(--outline-variant);}'
        + '.msap-wn-title-wrap{display:flex;align-items:center;gap:10px;min-width:0;}'
        + '.msap-wn-title-wrap .material-symbols-outlined{color:var(--primary);font-size:22px;}'
        + '.msap-wn-title{margin:0;font-size:17px;font-weight:700;color:var(--on-surface);line-height:1.3;}'
        + '.msap-wn-close{background:transparent;border:none;color:var(--on-surface-variant);'
        +   'cursor:pointer;padding:4px;border-radius:8px;display:inline-flex;align-items:center;justify-content:center;}'
        + '.msap-wn-close:hover{background:var(--surface-container-high,rgba(128,128,128,0.12));color:var(--on-surface);}'
        + '.msap-wn-body{padding:18px 20px;overflow-y:auto;display:flex;flex-direction:column;gap:20px;}'
        + '.msap-wn-release{display:flex;flex-direction:column;gap:10px;}'
        + '.msap-wn-release + .msap-wn-release{padding-top:16px;border-top:1px solid var(--outline-variant);}'
        + '.msap-wn-release-head{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap;}'
        + '.msap-wn-version{font-size:14px;font-weight:700;color:var(--on-surface);}'
        + '.msap-wn-date{font-size:12px;color:var(--on-surface-variant);}'
        + '.msap-wn-list{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:10px;}'
        + '.msap-wn-item{display:flex;align-items:flex-start;gap:10px;font-size:13.5px;line-height:1.5;color:var(--on-surface-variant);}'
        + '.msap-wn-item .modern-badge-sm{flex:0 0 auto;margin-top:2px;}'
        + '.msap-wn-item-body{min-width:0;flex:1;}'
        + '.msap-wn-item-title{font-weight:600;color:var(--on-surface);}'
        + '.msap-wn-empty{margin:0;font-size:13.5px;color:var(--on-surface-variant);}'
        + '.msap-wn-footer{display:flex;align-items:center;justify-content:space-between;gap:8px;'
        +   'padding:12px 20px;border-top:1px solid var(--outline-variant);}'
        + '.msap-wn-footer-right{margin-left:auto;display:flex;align-items:center;gap:8px;}'
        + '.mnav-wn-dot{width:7px;height:7px;border-radius:999px;background:var(--primary);margin-left:auto;display:none;}'
        + '.mnav-wn-dot.is-visible{display:inline-block;}';

    function injectStyles() {
        if (document.getElementById('msap-whats-new-style')) {
            return;
        }
        var style = document.createElement('style');
        style.id = 'msap-whats-new-style';
        style.textContent = CSS;
        document.head.appendChild(style);
    }

    function getEndpointUrl() {
        var btn = document.getElementById('mnav-whats-new-btn');
        return (btn && btn.getAttribute('data-whats-new-url')) || '/MSAP/Home/WhatsNew';
    }

    function isDashboardPage() {
        var p = window.location.pathname.replace(/\/+$/, '').toLowerCase();
        return p === '/msap' || p === '/msap/home' || p === '/msap/home/index';
    }

    function badgeClassForCategory(category) {
        var norm = String(category || '').toLowerCase();
        if (norm === 'added' || norm === 'new') {
            return 'status-open';
        }
        if (norm === 'fixed' || norm === 'bugfix') {
            return 'status-tariff';
        }
        if (norm === 'removed') {
            return 'status-closed';
        }
        return 'status-billing';
    }

    function loadReleases() {
        if (cachedReleases !== null) {
            return Promise.resolve(cachedReleases);
        }
        if (fetchPromise) {
            return fetchPromise;
        }
        fetchPromise = fetch(getEndpointUrl(), {
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        })
            .then(function (resp) {
                if (!resp.ok) {
                    throw new Error('HTTP ' + resp.status);
                }
                return resp.json();
            })
            .then(function (data) {
                cachedReleases = Array.isArray(data && data.releases) ? data.releases : [];
                return cachedReleases;
            })
            .catch(function () {
                cachedReleases = [];
                return cachedReleases;
            });
        return fetchPromise;
    }

    function markSeen(releases) {
        if (!releases || !releases.length || !releases[0].version) {
            return;
        }
        try {
            localStorage.setItem(STORAGE_KEY, String(releases[0].version));
        } catch (err) {
            // Ignore storage quota or private-mode errors
        }
        var dot = document.getElementById('mnav-whats-new-dot');
        if (dot) {
            dot.classList.remove('is-visible');
        }
    }

    function hasUnseenRelease(releases) {
        if (!releases || !releases.length || !releases[0].version) {
            return false;
        }
        try {
            return localStorage.getItem(STORAGE_KEY) !== String(releases[0].version);
        } catch (err) {
            return false;
        }
    }

    function ensureDialog() {
        if (dialogEl) {
            return dialogEl;
        }
        injectStyles();

        dialogEl = document.createElement('dialog');
        dialogEl.className = 'msap-wn-dialog';
        dialogEl.setAttribute('aria-labelledby', 'msap-wn-heading');

        var shell = document.createElement('div');
        shell.className = 'msap-wn-shell';

        var header = document.createElement('div');
        header.className = 'msap-wn-header';

        var titleWrap = document.createElement('div');
        titleWrap.className = 'msap-wn-title-wrap';

        var icon = document.createElement('span');
        icon.className = 'material-symbols-outlined';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = 'auto_awesome';

        var heading = document.createElement('h2');
        heading.id = 'msap-wn-heading';
        heading.className = 'msap-wn-title';
        heading.textContent = "What's New in MSAP";

        titleWrap.appendChild(icon);
        titleWrap.appendChild(heading);

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'msap-wn-close';
        closeBtn.setAttribute('aria-label', 'Close');
        var closeIcon = document.createElement('span');
        closeIcon.className = 'material-symbols-outlined';
        closeIcon.textContent = 'close';
        closeBtn.appendChild(closeIcon);

        header.appendChild(titleWrap);
        header.appendChild(closeBtn);

        var body = document.createElement('div');
        body.className = 'msap-wn-body';

        var footer = document.createElement('div');
        footer.className = 'msap-wn-footer';

        var toggleBtn = document.createElement('button');
        toggleBtn.type = 'button';
        toggleBtn.className = 'modern-btn-secondary msap-wn-toggle';
        toggleBtn.style.display = 'none';
        footer.appendChild(toggleBtn);

        var footerRight = document.createElement('div');
        footerRight.className = 'msap-wn-footer-right';

        var okBtn = document.createElement('button');
        okBtn.type = 'button';
        okBtn.className = 'modern-btn-primary';
        okBtn.textContent = 'Got it';
        footerRight.appendChild(okBtn);
        footer.appendChild(footerRight);

        shell.appendChild(header);
        shell.appendChild(body);
        shell.appendChild(footer);
        dialogEl.appendChild(shell);
        document.body.appendChild(dialogEl);

        function closeDialog() {
            if (dialogEl && dialogEl.open) {
                dialogEl.close();
            }
            markSeen(cachedReleases);
        }

        toggleBtn.addEventListener('click', function () {
            showAllReleases = !showAllReleases;
            renderReleases(cachedReleases);
        });
        closeBtn.addEventListener('click', closeDialog);
        okBtn.addEventListener('click', closeDialog);
        dialogEl.addEventListener('close', function () {
            markSeen(cachedReleases);
        });
        dialogEl.addEventListener('click', function (e) {
            if (e.target === dialogEl) {
                closeDialog();
            }
        });

        return dialogEl;
    }

    function renderReleases(releases) {
        var dlg = ensureDialog();
        var body = dlg.querySelector('.msap-wn-body');
        var toggleBtn = dlg.querySelector('.msap-wn-toggle');
        body.textContent = '';

        if (!releases || !releases.length) {
            if (toggleBtn) {
                toggleBtn.style.display = 'none';
            }
            var empty = document.createElement('p');
            empty.className = 'msap-wn-empty';
            empty.textContent = 'No release notes are available yet.';
            body.appendChild(empty);
            return;
        }

        if (toggleBtn) {
            if (releases.length > 1) {
                toggleBtn.style.display = '';
                toggleBtn.textContent = showAllReleases
                    ? 'Show latest only'
                    : 'View full changelog (' + releases.length + ')';
            } else {
                toggleBtn.style.display = 'none';
            }
        }

        var visible = showAllReleases ? releases : releases.slice(0, 1);

        visible.forEach(function (rel) {
            var section = document.createElement('section');
            section.className = 'msap-wn-release';

            var head = document.createElement('div');
            head.className = 'msap-wn-release-head';

            var ver = document.createElement('span');
            ver.className = 'msap-wn-version';
            ver.textContent = 'v' + String(rel.version || '').replace(/^v/i, '');
            head.appendChild(ver);

            if (rel.date) {
                var dateEl = document.createElement('span');
                dateEl.className = 'msap-wn-date';
                dateEl.textContent = rel.date;
                head.appendChild(dateEl);
            }
            section.appendChild(head);

            var list = document.createElement('ul');
            list.className = 'msap-wn-list';

            (rel.items || []).forEach(function (item) {
                var li = document.createElement('li');
                li.className = 'msap-wn-item';

                var badge = document.createElement('span');
                badge.className = 'modern-badge-sm ' + badgeClassForCategory(item.category);
                badge.textContent = item.category || 'Updated';
                li.appendChild(badge);

                var itemBody = document.createElement('div');
                itemBody.className = 'msap-wn-item-body';

                if (item.title) {
                    var strong = document.createElement('span');
                    strong.className = 'msap-wn-item-title';
                    strong.textContent = item.title + ': ';
                    itemBody.appendChild(strong);
                }

                itemBody.appendChild(document.createTextNode(item.text || ''));
                li.appendChild(itemBody);
                list.appendChild(li);
            });

            section.appendChild(list);
            body.appendChild(section);
        });
    }

    function openWhatsNew() {
        showAllReleases = false;
        loadReleases().then(function (releases) {
            renderReleases(releases);
            var dlg = ensureDialog();
            if (!dlg.open) {
                dlg.showModal();
            }
        });
    }

    window.openMsapWhatsNew = openWhatsNew;

    function init() {
        injectStyles();

        var navBtn = document.getElementById('mnav-whats-new-btn');
        if (navBtn) {
            navBtn.addEventListener('click', function (e) {
                e.preventDefault();
                var dd = document.getElementById('mnav-dropdown-menu');
                if (dd) {
                    dd.classList.remove('open');
                }
                openWhatsNew();
            });
        }

        if (!navBtn) {
            return;
        }

        loadReleases().then(function (releases) {
            if (!hasUnseenRelease(releases)) {
                return;
            }
            var dot = document.getElementById('mnav-whats-new-dot');
            if (dot) {
                dot.classList.add('is-visible');
            }
            if (isDashboardPage()) {
                openWhatsNew();
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
