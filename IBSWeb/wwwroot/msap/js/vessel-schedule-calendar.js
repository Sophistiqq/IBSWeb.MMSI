(() => {
    'use strict';

    const workspace = document.querySelector('.schedule-calendar-workspace');
    if (!workspace) return;

    const form = document.getElementById('calendarFilters');
    const filtersToggle = document.getElementById('scheduleFiltersToggle');
    if (filtersToggle && form) {
        filtersToggle.addEventListener('click', () => {
            const willOpen = !form.classList.contains('is-open');
            form.classList.toggle('is-open', willOpen);
            filtersToggle.setAttribute('aria-expanded', String(willOpen));
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && form.classList.contains('is-open') && !event.target.closest('.modern-select-dropdown')) {
                form.classList.remove('is-open');
                filtersToggle.setAttribute('aria-expanded', 'false');
                filtersToggle.focus();
            }
        });
    }

    const port = document.getElementById('calendarPort');
    const terminal = document.getElementById('calendarTerminal');
    const statusSelect = document.getElementById('calendarStatus');
    if (port && form) {
        $(port).on('change', () => {
            if (terminal) $(terminal).val('').trigger('change.select2');
            form.requestSubmit();
        });
    }
    if (terminal && form) {
        $(terminal).on('change', () => form.requestSubmit());
    }
    if (statusSelect && form) {
        $(statusSelect).on('change', () => form.requestSubmit());
    }

    const listTable = document.getElementById('vesselSchedulesTable');
    if (listTable && typeof $.fn.DataTable === 'function' && typeof ModernTable !== 'undefined') {
        const htmlPassthrough = data => data;
        $(listTable).DataTable(ModernTable.config({
            serverSide: false,
            processing: false,
            order: [[0, 'desc']],
            placeholder: 'Search vessel, customer, VS-#, COS-#, voyage, port...',
            language: {
                emptyTable: 'No vessel schedules match these filters.'
            },
            columns: [
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough, className: 'text-center' },
                { render: htmlPassthrough, orderable: false, className: 'text-center' }
            ]
        }));
    }

    function syncUrlParam(key, value) {
        try {
            const url = new URL(window.location.href);
            if (value) url.searchParams.set(key, value);
            else url.searchParams.delete(key);
            window.history.replaceState(null, '', url.toString());
        } catch {}
    }

    function installLightDismissFallback(dialogEl) {
        if (!dialogEl || 'closedBy' in HTMLDialogElement.prototype) return;
        let pressedOutside = false;
        const isOutside = e => {
            const r = dialogEl.getBoundingClientRect();
            return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
        };
        dialogEl.addEventListener('pointerdown', e => {
            pressedOutside = isOutside(e);
        });
        dialogEl.addEventListener('pointerup', e => {
            if (pressedOutside && isOutside(e) && dialogEl.open) dialogEl.close();
        });
    }

    // View tabs (Schedules | Tugboats)
    const tabs = [...workspace.querySelectorAll('[data-panel-tab]')];
    const filterViewInput = document.getElementById('filterViewInput');
    function selectTab(tab, updateUrl = true) {
        tabs.forEach(item => {
            const selected = item === tab;
            item.setAttribute('aria-selected', String(selected));
            item.classList.toggle('active', selected);
            item.tabIndex = selected ? 0 : -1;
            const target = document.getElementById(item.getAttribute('aria-controls'));
            if (target) target.hidden = !selected;
        });
        const viewName = tab.dataset.viewName || 'calendar';
        if (filterViewInput) filterViewInput.value = viewName;
        const isTugs = viewName === 'tugboats';
        if (filtersToggle) filtersToggle.hidden = isTugs;
        if (form && isTugs) {
            form.classList.remove('is-open');
            filtersToggle?.setAttribute('aria-expanded', 'false');
        }
        if (updateUrl) syncUrlParam('view', viewName === 'calendar' ? '' : viewName);
    }
    const activeInitialTab = tabs.find(t => t.classList.contains('active'));
    if (activeInitialTab && filtersToggle) {
        filtersToggle.hidden = activeInitialTab.dataset.viewName === 'tugboats';
    }
    tabs.forEach((tab, index) => {
        tab.addEventListener('click', () => selectTab(tab));
        tab.addEventListener('keydown', event => {
            const keys = ['ArrowLeft', 'ArrowRight', 'Home', 'End'];
            if (!keys.includes(event.key)) return;
            event.preventDefault();
            const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
                : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
            selectTab(tabs[next]);
            tabs[next].focus();
        });
    });

    // Operational triage bar (0ms client-side filter for both Day Columns and List)
    const triageButtons = [...workspace.querySelectorAll('[data-triage-filter]')];
    const dayGroupsContainer = document.getElementById('scheduleDayGroups');
    const isColumnsLayout = dayGroupsContainer?.dataset.boardLayout === 'columns';
    const dayGroups = [...workspace.querySelectorAll('[data-day-group]')];
    const triageEmptyNotice = document.getElementById('triageEmptyNotice');
    const filterTriageInput = document.getElementById('filterTriageInput');

    function applyTriageFilter(triageKey, updateUrl = true) {
        const key = triageKey || 'all';
        triageButtons.forEach(btn => {
            btn.classList.toggle('active', btn.dataset.triageFilter === key);
        });
        if (filterTriageInput) filterTriageInput.value = key === 'all' ? '' : key;

        let visibleTotal = 0;
        let totalScheduleRows = 0;
        dayGroups.forEach(group => {
            const rows = [...group.querySelectorAll('[data-schedule-row]')];
            totalScheduleRows += rows.length;
            let groupVisible = 0;
            rows.forEach(row => {
                const show = key === 'all'
                    || (key === 'overlaps' && row.dataset.triageOverlap === 'true')
                    || (key === 'needs-tugs' && row.dataset.triageNeedsTugs === 'true')
                    || (key === 'pending-review' && row.dataset.triagePendingReview === 'true')
                    || (key === 'missing-tickets' && row.dataset.triageMissingTickets === 'true');
                row.hidden = !show;
                if (show) {
                    groupVisible++;
                    visibleTotal++;
                }
            });

            group.querySelectorAll('[data-conflict-divider]').forEach(div => {
                const prevRow = div.previousElementSibling;
                const nextRow = div.nextElementSibling;
                div.hidden = !prevRow || !nextRow || prevRow.hidden || nextRow.hidden;
            });

            if (isColumnsLayout && key === 'all') {
                group.hidden = false;
            } else {
                group.hidden = groupVisible === 0;
            }

            const countEl = group.querySelector('[data-day-group-count]');
            if (countEl) {
                countEl.textContent = isColumnsLayout
                    ? String(groupVisible)
                    : `${groupVisible} schedule${groupVisible === 1 ? '' : 's'}`;
            }
        });

        if (triageEmptyNotice) {
            triageEmptyNotice.hidden = key === 'all' || totalScheduleRows === 0 || visibleTotal > 0;
        }
        if (updateUrl) syncUrlParam('triage', key === 'all' ? '' : key);
    }

    triageButtons.forEach(btn => {
        btn.addEventListener('click', () => applyTriageFilter(btn.dataset.triageFilter));
    });

    // Fit Day-Columns board exactly to remaining viewport height (accounting for footer) + mouse-wheel sideways scroll
    if (dayGroupsContainer && isColumnsLayout) {
        const syncColumnsHeight = () => {
            if (dayGroupsContainer.offsetParent === null) return;
            const rect = dayGroupsContainer.getBoundingClientRect();
            const footer = document.querySelector('footer');
            const footerH = footer ? footer.getBoundingClientRect().height : 0;
            const available = Math.floor(window.innerHeight - rect.top - footerH - 12);
            const targetH = Math.max(240, available);
            if (dayGroupsContainer.classList.contains('is-single-day')) {
                dayGroupsContainer.style.minHeight = `${targetH}px`;
            } else {
                dayGroupsContainer.style.height = `${targetH}px`;
            }
        };

        requestAnimationFrame(syncColumnsHeight);
        window.addEventListener('resize', syncColumnsHeight);
        filtersToggle?.addEventListener('click', () => requestAnimationFrame(syncColumnsHeight));

        dayGroupsContainer.addEventListener('wheel', event => {
            if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
            if (dayGroupsContainer.scrollWidth <= dayGroupsContainer.clientWidth) return;

            const colBody = event.target.closest('.schedule-day-column-body');
            if (colBody && colBody.scrollHeight > colBody.clientHeight) {
                const atTop = colBody.scrollTop <= 0 && event.deltaY < 0;
                const atBottom = colBody.scrollTop + colBody.clientHeight >= colBody.scrollHeight - 1 && event.deltaY > 0;
                if (!atTop && !atBottom) return;
            }

            event.preventDefault();
            dayGroupsContainer.scrollLeft += event.deltaY;
        }, { passive: false });
    }

    // Schedule Detail / Quick-Glance Modal (opens on clicking any schedule card/row; stays open for screenshots)
    const detailModal = document.getElementById('scheduleDetailModal');
    const detailModalBody = document.getElementById('scheduleDetailModalBody');
    const detailModalTitle = document.getElementById('scheduleDetailModalTitle');
    const closeDetailModalBtn = document.getElementById('closeScheduleDetailBtn');

    if (detailModal) {
        installLightDismissFallback(detailModal);
        detailModal.addEventListener('close', () => {
            detailModal.returnValue = '';
        });
        closeDetailModalBtn?.addEventListener('click', () => detailModal.close());
    }

    function openScheduleDetailModal(scheduleId, fallbackHref) {
        if (!scheduleId) return;
        const source = document.getElementById(`schedule-preview-${scheduleId}`);
        if (!source || !detailModal || !detailModalBody) {
            if (fallbackHref) window.location.assign(fallbackHref);
            return;
        }
        const clone = document.createElement('div');
        clone.innerHTML = source.innerHTML;
        if (detailModalTitle) detailModalTitle.textContent = `Schedule VS-${scheduleId}`;
        detailModalBody.replaceChildren(clone);
        detailModal.showModal();
    }

    workspace.addEventListener('click', event => {
        if (event.target.closest('a')) return;
        const card = event.target.closest('[data-open-schedule-modal]');
        if (!card) return;
        openScheduleDetailModal(card.dataset.openScheduleModal, card.dataset.fallbackHref);
    });

    workspace.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        if (event.target.closest('a, button, input, select')) return;
        const card = event.target.closest('[data-open-schedule-modal]');
        if (!card) return;
        event.preventDefault();
        openScheduleDetailModal(card.dataset.openScheduleModal, card.dataset.fallbackHref);
    });

    // Tugboat fleet pill filters + search + mouse-wheel sideways scroll on matrix
    const tugSearch = document.getElementById('tugSearch');
    const tugScopeBtns = [...workspace.querySelectorAll('[data-tug-scope]')];
    const tugOwnerBtns = [...workspace.querySelectorAll('[data-tug-owner]')];
    let activeTugScope = 'all';
    let activeTugOwner = 'all';

    function filterTugs() {
        const search = (tugSearch?.value || '').trim().toLowerCase();
        let visibleCount = 0;

        workspace.querySelectorAll('[data-tug-group]').forEach(group => {
            let groupVisible = 0;
            group.querySelectorAll('[data-calendar-tug]').forEach(row => {
                const isBooked = row.dataset.booked === 'true';
                const matchesScope = activeTugScope === 'all' || (activeTugScope === 'booked' ? isBooked : !isBooked);
                const matchesOwner = activeTugOwner === 'all' || row.dataset.ownership === activeTugOwner;
                const matchesSearch = !search || (row.dataset.search || '').toLowerCase().includes(search);
                const show = matchesScope && matchesOwner && matchesSearch;
                row.hidden = !show;
                if (show) {
                    groupVisible++;
                    visibleCount++;
                }
            });
            group.hidden = groupVisible === 0;
        });

        const emptyTugs = document.getElementById('noCalendarTugs');
        if (emptyTugs) emptyTugs.hidden = visibleCount > 0;
    }

    tugScopeBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            activeTugScope = btn.dataset.tugScope || 'all';
            tugScopeBtns.forEach(b => b.classList.toggle('active', b === btn));
            filterTugs();
        });
    });

    tugOwnerBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const val = btn.dataset.tugOwner || 'all';
            activeTugOwner = activeTugOwner === val ? 'all' : val;
            tugOwnerBtns.forEach(b => b.classList.toggle('active', b.dataset.tugOwner === activeTugOwner));
            filterTugs();
        });
    });

    if (tugSearch) {
        tugSearch.addEventListener('input', filterTugs);
        filterTugs();
    }

    workspace.querySelectorAll('[data-fleet-matrix-scroll]').forEach(scroller => {
        scroller.addEventListener('wheel', event => {
            if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
            if (scroller.scrollWidth <= scroller.clientWidth) return;
            event.preventDefault();
            scroller.scrollLeft += event.deltaY;
        }, { passive: false });
    });

    // Full-Screen 1D / 1M / 3M / 6M / 1Y Calendar Modal (1 Day default + controlled Start/End range picking)
    const modal = document.getElementById('scheduleCalendarModal');
    const openModalBtn = document.getElementById('openCalendarModalBtn');
    if (modal && openModalBtn) {
        installLightDismissFallback(modal);
        modal.addEventListener('close', () => {
            modal.returnValue = '';
            isDragging = false;
        });

        const monthsContainer = document.getElementById('calendarModalMonths');
        const stickyWeekdays = document.getElementById('modalStickyWeekdays');
        const rangeBar = document.getElementById('modalRangeBar');
        const startFieldBtn = document.getElementById('modalStartField');
        const endFieldBtn = document.getElementById('modalEndField');
        const startFieldVal = document.getElementById('modalStartFieldValue');
        const endFieldVal = document.getElementById('modalEndFieldValue');
        const periodLabel = document.getElementById('modalPeriodLabel');
        const monthJumpInput = document.getElementById('calendarModalMonthJump');
        const monthJumpBtn = document.getElementById('modalMonthJumpBtn');
        const prevBtn = document.getElementById('modalPrevBtn');
        const nextBtn = document.getElementById('modalNextBtn');
        const closeBtn = document.getElementById('closeCalendarModalBtn');
        const cancelBtn = document.getElementById('cancelCalendarModalBtn');
        const applyBtn = document.getElementById('applyCalendarRangeBtn');
        const loadingBadge = document.getElementById('modalLoadingBadge');
        const selectionText = document.getElementById('calendarModalSelectionText');
        const selectionMeta = document.getElementById('calendarModalSelectionMeta');
        const gridModeBtns = [...modal.querySelectorAll('[data-modal-grid-mode]')];
        const presetBtns = [...modal.querySelectorAll('[data-modal-preset]')];

        const summaryCache = new Map();
        try {
            const initialJson = document.getElementById('initialCalendarSummaries')?.textContent;
            if (initialJson) {
                const parsed = JSON.parse(initialJson);
                Object.entries(parsed).forEach(([k, v]) => summaryCache.set(k, v));
            }
        } catch {}

        const todayIso = modal.dataset.today || toIso(new Date());
        let selStart = modal.dataset.initialStart || todayIso;
        let selEnd = modal.dataset.initialEnd || selStart;
        let gridMode = '1d';
        let activeField = 'start';
        let anchorMonth = parseIsoMonthStart(selStart);
        let isDragging = false;
        let dragAnchorIso = null;
        let didDragMove = false;
        let fetchController = null;

        function pad2(n) {
            return String(n).padStart(2, '0');
        }

        function toIso(dt) {
            return `${dt.getFullYear()}-${pad2(dt.getMonth() + 1)}-${pad2(dt.getDate())}`;
        }

        function parseIso(iso) {
            const [y, m, d] = iso.split('-').map(Number);
            return new Date(y, m - 1, d);
        }

        function parseIsoMonthStart(iso) {
            const [y, m] = iso.split('-').map(Number);
            return new Date(y, m - 1, 1);
        }

        function addDaysIso(iso, delta) {
            const dt = parseIso(iso);
            dt.setDate(dt.getDate() + delta);
            return toIso(dt);
        }

        function diffDaysInclusive(startIso, endIso) {
            const a = parseIso(startIso);
            const b = parseIso(endIso);
            return Math.round((b - a) / 86400000) + 1;
        }

        function clampRange(startIso, endIso) {
            const effectiveEnd = endIso || startIso;
            const ordered = startIso <= effectiveEnd ? [startIso, effectiveEnd] : [effectiveEnd, startIso];
            if (diffDaysInclusive(ordered[0], ordered[1]) > 366) {
                return [ordered[0], addDaysIso(ordered[0], 365)];
            }
            return ordered;
        }

        function formatHuman(iso) {
            return parseIso(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
        }

        function getMonthCount() {
            if (gridMode === '1d' || gridMode === '1m') return 1;
            if (gridMode === '3m') return 3;
            if (gridMode === '1y') return 12;
            return 6;
        }

        function getNavStepMonths() {
            if (gridMode === '1d' || gridMode === '1m') return 1;
            if (gridMode === '3m') return 3;
            if (gridMode === '1y') return 12;
            return 6;
        }

        function isRangeMode() {
            return gridMode !== '1d';
        }

        function syncRangeBar() {
            const showRange = isRangeMode();
            if (rangeBar) rangeBar.hidden = !showRange;
            if (!showRange) return;

            if (startFieldVal) startFieldVal.textContent = selStart ? formatHuman(selStart) : 'Pick start';
            if (endFieldVal) endFieldVal.textContent = selEnd ? formatHuman(selEnd) : 'Pick end';

            if (startFieldBtn) {
                const isStartActive = activeField === 'start';
                startFieldBtn.classList.toggle('active', isStartActive);
                startFieldBtn.setAttribute('aria-pressed', String(isStartActive));
            }
            if (endFieldBtn) {
                const isEndActive = activeField === 'end';
                endFieldBtn.classList.toggle('active', isEndActive);
                endFieldBtn.classList.toggle('is-empty', !selEnd);
                endFieldBtn.setAttribute('aria-pressed', String(isEndActive));
            }
        }

        function syncModeControls() {
            gridModeBtns.forEach(b => {
                const active = b.dataset.modalGridMode === gridMode;
                b.classList.toggle('active', active);
                b.setAttribute('aria-pressed', String(active));
            });
            const isSingleCol = gridMode === '1d' || gridMode === '1m';
            monthsContainer.classList.toggle('is-single-month', isSingleCol);
            monthsContainer.classList.toggle('is-three-months', gridMode === '3m');
            monthsContainer.classList.toggle('is-six-months', gridMode === '6m');
            monthsContainer.classList.toggle('is-one-year', gridMode === '1y');
            if (stickyWeekdays) {
                stickyWeekdays.classList.toggle('is-desktop-visible', isSingleCol);
            }
            syncRangeBar();
        }

        function updateFooterSummary() {
            const effectiveEnd = isRangeMode() ? (selEnd || selStart) : selStart;
            const [s, e] = clampRange(selStart, effectiveEnd);
            const days = diffDaysInclusive(s, e);
            if (selectionText) {
                if (!isRangeMode() || (s === e && selEnd)) {
                    selectionText.innerHTML = `<span class="schedule-sel-start-text">${formatHuman(s)}</span> <span class="schedule-muted">(1 day)</span>`;
                } else if (!selEnd) {
                    selectionText.innerHTML = `<span class="schedule-sel-start-text">${formatHuman(s)}</span> → <span class="schedule-muted">Select end date</span>`;
                } else {
                    selectionText.innerHTML = `<span class="schedule-sel-start-text">${formatHuman(s)}</span> → <span class="schedule-sel-end-text">${formatHuman(e)}</span> <span class="schedule-muted">(${days} days)</span>`;
                }
            }
            if (selectionMeta) {
                let totalSchedules = 0;
                let totalOverlaps = 0;
                let cursor = s;
                while (cursor <= e) {
                    const info = summaryCache.get(cursor);
                    if (info) {
                        totalSchedules += info.total || info.Total || 0;
                        totalOverlaps += info.overlaps || info.Overlaps || 0;
                    }
                    cursor = addDaysIso(cursor, 1);
                }
                const layoutHint = days <= 15 ? 'Day columns view' : 'Compact list view';
                selectionMeta.textContent = totalOverlaps > 0
                    ? `${totalSchedules} schedule-day entries · ${totalOverlaps} overlap alerts · ${layoutHint}`
                    : `${totalSchedules} schedule-day entries · ${layoutHint}`;
            }
            syncRangeBar();
        }

        function populateDayMeta(btn, iso, isCompactYear, rangeS, rangeE, hasValidEnd) {
            const metaSpan = btn.querySelector('.schedule-modal-day-meta');
            if (!metaSpan) return;
            metaSpan.replaceChildren();

            const isStartTile = isRangeMode() && iso === rangeS;
            const isEndTile = isRangeMode() && hasValidEnd && iso === rangeE;

            if (isStartTile) {
                const tag = document.createElement('span');
                tag.className = 'schedule-modal-day-tag is-start';
                tag.textContent = isCompactYear ? 'S' : 'Start';
                metaSpan.appendChild(tag);
                return;
            }
            if (isEndTile) {
                const tag = document.createElement('span');
                tag.className = 'schedule-modal-day-tag is-end';
                tag.textContent = isCompactYear ? 'E' : 'End';
                metaSpan.appendChild(tag);
                return;
            }

            const info = summaryCache.get(iso);
            const total = info ? (info.total || info.Total || 0) : 0;
            const overlaps = info ? (info.overlaps || info.Overlaps || 0) : 0;

            if (iso === todayIso && !isCompactYear) {
                const tag = document.createElement('span');
                tag.className = 'schedule-modal-day-tag is-today';
                tag.textContent = 'Today';
                metaSpan.appendChild(tag);
                if (overlaps > 0) {
                    const ov = document.createElement('span');
                    ov.className = 'schedule-modal-day-warn';
                    ov.textContent = '⚠';
                    metaSpan.appendChild(ov);
                }
                return;
            }

            if (isCompactYear) {
                if (overlaps > 0) {
                    const dot = document.createElement('span');
                    dot.className = 'schedule-modal-dot is-overlap';
                    metaSpan.appendChild(dot);
                } else if (total > 0) {
                    const dot = document.createElement('span');
                    dot.className = 'schedule-modal-dot is-scheduled';
                    metaSpan.appendChild(dot);
                }
            } else {
                if (total > 0) {
                    const cnt = document.createElement('span');
                    cnt.className = 'schedule-modal-day-count';
                    cnt.textContent = String(total);
                    metaSpan.appendChild(cnt);
                }
                if (overlaps > 0) {
                    const ov = document.createElement('span');
                    ov.className = 'schedule-modal-day-warn';
                    ov.textContent = '⚠';
                    metaSpan.appendChild(ov);
                }
            }
        }

        function renderMonths() {
            const count = getMonthCount();
            const isCompactYear = gridMode === '1y';
            const firstMonth = new Date(anchorMonth.getFullYear(), anchorMonth.getMonth(), 1);
            const lastMonth = new Date(anchorMonth.getFullYear(), anchorMonth.getMonth() + count - 1, 1);

            if (periodLabel) {
                periodLabel.textContent = count === 1
                    ? firstMonth.toLocaleDateString('en-US', { month: 'long', year: 'numeric' })
                    : `${firstMonth.toLocaleDateString('en-US', { month: 'short', year: 'numeric' })} – ${lastMonth.toLocaleDateString('en-US', { month: 'short', year: 'numeric' })}`;
            }
            if (monthJumpInput) {
                monthJumpInput.value = `${firstMonth.getFullYear()}-${pad2(firstMonth.getMonth() + 1)}`;
            }

            const hasValidEnd = isRangeMode() && Boolean(selEnd) && selEnd !== selStart;
            const [rangeS, rangeE] = hasValidEnd ? clampRange(selStart, selEnd) : [selStart, selStart];
            const isSingleSelection = !hasValidEnd;
            const fragment = document.createDocumentFragment();
            const weekdays = isCompactYear
                ? ['S', 'M', 'T', 'W', 'T', 'F', 'S']
                : ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

            for (let mOffset = 0; mOffset < count; mOffset++) {
                const mDate = new Date(firstMonth.getFullYear(), firstMonth.getMonth() + mOffset, 1);
                const year = mDate.getFullYear();
                const monthIdx = mDate.getMonth();
                const daysInMonth = new Date(year, monthIdx + 1, 0).getDate();
                const firstDow = mDate.getDay();

                const card = document.createElement('section');
                card.className = 'schedule-modal-month-card';
                const heading = document.createElement('h2');
                heading.className = 'schedule-modal-month-title';
                const monthLabel = mDate.toLocaleDateString('en-US', { month: isCompactYear ? 'short' : 'long' });
                heading.innerHTML = `<span>${monthLabel}</span> <span class="schedule-modal-month-dot">•</span> <span class="schedule-modal-month-year">${year}</span>`;
                card.appendChild(heading);

                const grid = document.createElement('div');
                grid.className = 'schedule-modal-grid';
                weekdays.forEach(w => {
                    const wh = document.createElement('div');
                    wh.className = 'schedule-modal-weekday';
                    wh.textContent = w;
                    grid.appendChild(wh);
                });

                for (let blank = 0; blank < firstDow; blank++) {
                    const padCell = document.createElement('div');
                    padCell.className = 'schedule-modal-day is-empty';
                    grid.appendChild(padCell);
                }

                for (let dayNum = 1; dayNum <= daysInMonth; dayNum++) {
                    const iso = `${year}-${pad2(monthIdx + 1)}-${pad2(dayNum)}`;
                    const info = summaryCache.get(iso);
                    const total = info ? (info.total || info.Total || 0) : 0;
                    const overlaps = info ? (info.overlaps || info.Overlaps || 0) : 0;

                    const btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'schedule-modal-day';
                    btn.dataset.modalDay = iso;
                    if (iso === todayIso) btn.classList.add('is-today');
                    if (isSingleSelection && iso === rangeS) {
                        btn.classList.add('is-range-single');
                    } else if (!isSingleSelection) {
                        if (iso === rangeS) btn.classList.add('is-range-start');
                        if (iso === rangeE) btn.classList.add('is-range-end');
                        if (iso > rangeS && iso < rangeE) btn.classList.add('is-in-range');
                    }
                    if (total > 0) btn.classList.add(total >= 4 ? 'is-busy-high' : 'is-busy');
                    if (overlaps > 0) btn.classList.add('has-overlap');

                    const numSpan = document.createElement('span');
                    numSpan.className = 'schedule-modal-day-num';
                    numSpan.textContent = String(dayNum);
                    btn.appendChild(numSpan);

                    const metaSpan = document.createElement('span');
                    metaSpan.className = 'schedule-modal-day-meta';
                    btn.appendChild(metaSpan);
                    populateDayMeta(btn, iso, isCompactYear, rangeS, rangeE, hasValidEnd);
                    grid.appendChild(btn);
                }

                card.appendChild(grid);
                fragment.appendChild(card);
            }

            monthsContainer.replaceChildren(fragment);
            updateFooterSummary();
        }

        function refreshSelectionClasses() {
            const isCompactYear = gridMode === '1y';
            const hasValidEnd = isRangeMode() && Boolean(selEnd) && selEnd !== selStart;
            const [rangeS, rangeE] = hasValidEnd ? clampRange(selStart, selEnd) : [selStart, selStart];
            const isSingleSelection = !hasValidEnd;
            monthsContainer.querySelectorAll('[data-modal-day]').forEach(btn => {
                const iso = btn.dataset.modalDay;
                btn.classList.toggle('is-range-single', isSingleSelection && iso === rangeS);
                btn.classList.toggle('is-range-start', !isSingleSelection && iso === rangeS);
                btn.classList.toggle('is-range-end', !isSingleSelection && iso === rangeE);
                btn.classList.toggle('is-in-range', !isSingleSelection && iso > rangeS && iso < rangeE);
                populateDayMeta(btn, iso, isCompactYear, rangeS, rangeE, hasValidEnd);
            });
            updateFooterSummary();
        }

        async function loadVisibleMonthSummaries() {
            const urlBase = modal.dataset.summaryUrl;
            if (!urlBase) return;
            const count = getMonthCount();
            const startDt = new Date(anchorMonth.getFullYear(), anchorMonth.getMonth(), 1);
            const endDt = new Date(anchorMonth.getFullYear(), anchorMonth.getMonth() + count, 0);

            fetchController?.abort();
            fetchController = new AbortController();
            if (loadingBadge) loadingBadge.hidden = false;

            const params = new URLSearchParams({
                start: toIso(startDt),
                end: toIso(endDt)
            });
            if (port?.value) params.set('portId', port.value);
            if (terminal?.value) params.set('terminalId', terminal.value);
            if (statusSelect?.value) params.set('status', statusSelect.value);
            const searchInput = document.getElementById('calendarSearch');
            if (searchInput?.value) params.set('search', searchInput.value);

            try {
                const resp = await fetch(`${urlBase}?${params}`, { signal: fetchController.signal });
                if (!resp.ok) return;
                const data = await resp.json();
                if (data?.days) {
                    Object.entries(data.days).forEach(([k, v]) => summaryCache.set(k, v));
                    renderMonths();
                }
            } catch (err) {
                if (err.name !== 'AbortError') {
                    // Keep rendered calendar usable even if summary fetch fails.
                }
            } finally {
                if (loadingBadge) loadingBadge.hidden = true;
            }
        }

        function applyAndNavigate() {
            const effectiveEnd = isRangeMode() ? (selEnd || selStart) : selStart;
            const [s, e] = clampRange(selStart, effectiveEnd);
            const url = new URL(window.location.href);
            url.searchParams.set('month', s.slice(0, 7));
            url.searchParams.set('date', s);
            if (s === e) url.searchParams.delete('endDate');
            else url.searchParams.set('endDate', e);
            modal.close('apply');
            window.location.assign(url.toString());
        }

        function pickInitialGridMode(startIso, endIso) {
            if (!endIso || startIso === endIso) return '1d';
            const s = parseIso(startIso);
            const e = parseIso(endIso);
            const monthsSpan = (e.getFullYear() - s.getFullYear()) * 12 + (e.getMonth() - s.getMonth()) + 1;
            if (monthsSpan <= 1) return '1m';
            if (monthsSpan <= 3) return '3m';
            if (monthsSpan <= 6) return '6m';
            return '1y';
        }

        openModalBtn.addEventListener('click', () => {
            selStart = modal.dataset.initialStart || todayIso;
            selEnd = modal.dataset.initialEnd || selStart;
            gridMode = pickInitialGridMode(selStart, selEnd);
            activeField = 'start';
            anchorMonth = parseIsoMonthStart(selStart);
            syncModeControls();
            renderMonths();
            modal.showModal();
            loadVisibleMonthSummaries();
        });

        closeBtn?.addEventListener('click', () => modal.close('cancel'));
        cancelBtn?.addEventListener('click', () => modal.close('cancel'));
        applyBtn?.addEventListener('click', applyAndNavigate);

        startFieldBtn?.addEventListener('click', () => {
            activeField = 'start';
            syncRangeBar();
        });
        endFieldBtn?.addEventListener('click', () => {
            activeField = 'end';
            syncRangeBar();
        });

        prevBtn?.addEventListener('click', () => {
            anchorMonth = new Date(anchorMonth.getFullYear(), anchorMonth.getMonth() - getNavStepMonths(), 1);
            renderMonths();
            loadVisibleMonthSummaries();
        });
        nextBtn?.addEventListener('click', () => {
            anchorMonth = new Date(anchorMonth.getFullYear(), anchorMonth.getMonth() + getNavStepMonths(), 1);
            renderMonths();
            loadVisibleMonthSummaries();
        });
        monthJumpBtn?.addEventListener('click', () => {
            if (typeof monthJumpInput?.showPicker === 'function') {
                try { monthJumpInput.showPicker(); return; } catch {}
            }
            monthJumpInput?.focus();
            monthJumpInput?.click();
        });
        monthJumpInput?.addEventListener('change', () => {
            if (/^\d{4}-\d{2}$/.test(monthJumpInput.value)) {
                const [y, m] = monthJumpInput.value.split('-').map(Number);
                anchorMonth = new Date(y, m - 1, 1);
                renderMonths();
                loadVisibleMonthSummaries();
            }
        });

        gridModeBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                gridMode = btn.dataset.modalGridMode || '1d';
                if (gridMode === '1d') {
                    selEnd = selStart;
                    activeField = 'start';
                } else {
                    if (!selEnd) selEnd = selStart;
                    activeField = 'start';
                }
                syncModeControls();
                renderMonths();
                loadVisibleMonthSummaries();
            });
        });

        presetBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const preset = btn.dataset.modalPreset;
                const t = parseIso(todayIso);
                if (preset === 'today') {
                    selStart = todayIso;
                    selEnd = todayIso;
                    gridMode = '1d';
                } else if (preset === 'tomorrow') {
                    selStart = addDaysIso(todayIso, 1);
                    selEnd = selStart;
                    gridMode = '1d';
                } else {
                    if (gridMode === '1d') gridMode = '1m';
                    if (preset === 'next7') {
                        selStart = todayIso;
                        selEnd = addDaysIso(todayIso, 6);
                    } else if (preset === 'next15') {
                        selStart = todayIso;
                        selEnd = addDaysIso(todayIso, 14);
                    } else if (preset === 'week') {
                        const dow = t.getDay();
                        selStart = addDaysIso(todayIso, -dow);
                        selEnd = addDaysIso(selStart, 6);
                    } else if (preset === 'month') {
                        const first = new Date(t.getFullYear(), t.getMonth(), 1);
                        const last = new Date(t.getFullYear(), t.getMonth() + 1, 0);
                        selStart = toIso(first);
                        selEnd = toIso(last);
                    } else if (preset === 'next30') {
                        selStart = todayIso;
                        selEnd = addDaysIso(todayIso, 29);
                    }
                }
                activeField = 'start';
                anchorMonth = parseIsoMonthStart(selStart);
                syncModeControls();
                renderMonths();
                loadVisibleMonthSummaries();
            });
        });

        // Desktop pointer drag (works in all modes — dragging in 1 Day seamlessly activates 1M range bar) + controlled Start/End tap selection
        monthsContainer.addEventListener('pointerdown', event => {
            const dayBtn = event.target.closest('[data-modal-day]');
            if (!dayBtn || event.button !== 0 || event.pointerType === 'touch') return;
            const iso = dayBtn.dataset.modalDay;
            if (!iso) return;
            isDragging = true;
            dragAnchorIso = iso;
            didDragMove = false;
        });

        monthsContainer.addEventListener('pointermove', event => {
            if (!isDragging || !dragAnchorIso) return;
            const el = document.elementFromPoint(event.clientX, event.clientY)?.closest('[data-modal-day]');
            const iso = el?.dataset.modalDay;
            if (!iso || iso === dragAnchorIso) return;
            didDragMove = true;
            if (gridMode === '1d') {
                gridMode = '1m';
                syncModeControls();
            }
            [selStart, selEnd] = clampRange(dragAnchorIso, iso);
            activeField = 'start';
            refreshSelectionClasses();
        });

        window.addEventListener('pointerup', () => {
            if (isDragging) {
                isDragging = false;
                dragAnchorIso = null;
            }
        });

        monthsContainer.addEventListener('click', event => {
            const dayBtn = event.target.closest('[data-modal-day]');
            if (!dayBtn) return;
            const iso = dayBtn.dataset.modalDay;
            if (!iso) return;

            if (didDragMove) {
                didDragMove = false;
                return;
            }

            if (!isRangeMode()) {
                selStart = iso;
                selEnd = iso;
                refreshSelectionClasses();
                return;
            }

            if (activeField === 'start') {
                selStart = iso;
                if (selEnd && iso > selEnd) {
                    selEnd = null;
                }
                activeField = 'end';
            } else {
                if (!selStart || iso < selStart) {
                    selStart = iso;
                    selEnd = null;
                    activeField = 'end';
                } else {
                    [selStart, selEnd] = clampRange(selStart, iso);
                    activeField = 'start';
                }
            }
            refreshSelectionClasses();
        });
    }
})();
