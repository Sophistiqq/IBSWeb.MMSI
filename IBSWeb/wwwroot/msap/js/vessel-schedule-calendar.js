(() => {
    'use strict';

    const workspace = document.querySelector('.schedule-calendar-workspace');
    if (!workspace) return;

    const form = document.getElementById('calendarFilters');
    const filtersToggle = document.getElementById('scheduleFiltersToggle');
    if (filtersToggle && form) {
        filtersToggle.addEventListener('click', () => {
            const willOpen = !form.classList.contains('is-mobile-open');
            form.classList.toggle('is-mobile-open', willOpen);
            filtersToggle.setAttribute('aria-expanded', String(willOpen));
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && form.classList.contains('is-mobile-open') && !event.target.closest('.modern-select-dropdown')) {
                form.classList.remove('is-mobile-open');
                filtersToggle.setAttribute('aria-expanded', 'false');
                filtersToggle.focus();
            }
        });
    }
    const port = document.getElementById('calendarPort');
    const terminal = document.getElementById('calendarTerminal');
    const status = document.getElementById('calendarStatus');
    $(port).on('change', () => {
        $(terminal).val('').trigger('change.select2');
        form.requestSubmit();
    });
    $(terminal).on('change', () => {
        form.requestSubmit();
    });
    $(status).on('change', () => {
        form.requestSubmit();
    });
    const monthInput = document.getElementById('calendarMonth');
    const monthBtn = document.getElementById('calendarMonthBtn');
    if (monthBtn && monthInput) {
        monthBtn.addEventListener('click', () => {
            if (typeof monthInput.showPicker === 'function') {
                try {
                    monthInput.showPicker();
                    return;
                } catch {}
            }
            monthInput.focus();
            monthInput.click();
        });
    }
    if (monthInput) {
        monthInput.addEventListener('change', event => {
            const selectedDate = form.querySelector('[name="date"]');
            const selectedEnd = form.querySelector('[name="endDate"]');
            if (selectedDate && /^\d{4}-\d{2}$/.test(event.target.value)) {
                const [year, month] = event.target.value.split('-').map(Number);
                const day = Math.min(Number(selectedDate.value.slice(-2)) || 1, new Date(year, month, 0).getDate());
                selectedDate.value = `${event.target.value}-${String(day).padStart(2, '0')}`;
            }
            if (selectedEnd) {
                selectedEnd.value = '';
            }
            form.requestSubmit();
        });
    }

    const listTable = document.getElementById('vesselSchedulesTable');
    if (listTable && typeof $.fn.DataTable === 'function' && typeof ModernTable !== 'undefined') {
        const htmlPassthrough = data => data;
        $(listTable).DataTable(ModernTable.config({
            serverSide: false,
            processing: false,
            order: [[0, 'desc']],
            placeholder: 'Search vessel, customer, COS #, voyage, port...',
            language: {
                emptyTable: 'No schedules match these filters.'
            },
            columns: [
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough },
                { render: htmlPassthrough, className: 'text-center' },
                { render: htmlPassthrough, className: 'text-center' },
                { render: htmlPassthrough, orderable: false, className: 'text-center' }
            ]
        }));
    }

    // Unified calendar: Day / Range selection (persists across month navigation) + Shift-click shortcut
    const PENDING_RANGE_KEY = 'msap_schedule_pending_range';
    const monthSection = workspace.querySelector('.schedule-month');
    const modeButtons = [...workspace.querySelectorAll('[data-range-mode]')];
    const rangeStatus = document.getElementById('rangePickerStatus');
    const rangeHint = document.getElementById('rangePickerHint');
    const rangeCancelBtn = document.getElementById('rangeCancelBtn');
    const dayTiles = [...workspace.querySelectorAll('[data-calendar-day]')];
    let isRangeMode = monthSection?.dataset.rangeActive === 'true';
    let pendingRangeStart = null;

    function formatShortIso(iso) {
        const parts = iso.split('-').map(Number);
        if (parts.length !== 3) return iso;
        const dt = new Date(parts[0], parts[1] - 1, parts[2]);
        return dt.toLocaleDateString('en-US', { day: 'numeric', month: 'short' });
    }

    function readPendingRange() {
        try {
            const val = window.sessionStorage.getItem(PENDING_RANGE_KEY);
            return val && /^\d{4}-\d{2}-\d{2}$/.test(val) ? val : null;
        } catch {
            return null;
        }
    }

    function savePendingRange(iso) {
        try {
            if (iso) window.sessionStorage.setItem(PENDING_RANGE_KEY, iso);
            else window.sessionStorage.removeItem(PENDING_RANGE_KEY);
        } catch {}
    }

    function syncRangeUi() {
        modeButtons.forEach(btn => {
            const active = (btn.dataset.rangeMode === 'range') === isRangeMode;
            btn.classList.toggle('active', active);
            btn.setAttribute('aria-pressed', String(active));
        });
        dayTiles.forEach(tile => {
            tile.classList.toggle('schedule-range-pending', Boolean(pendingRangeStart && tile.dataset.calendarDay === pendingRangeStart));
        });
        if (rangeStatus && rangeHint) {
            if (pendingRangeStart) {
                rangeHint.textContent = `Start: ${formatShortIso(pendingRangeStart)} · Pick end…`;
                rangeStatus.hidden = false;
                if (rangeCancelBtn) rangeCancelBtn.hidden = false;
            } else if (isRangeMode) {
                rangeHint.textContent = 'Pick start date…';
                rangeStatus.hidden = false;
                if (rangeCancelBtn) rangeCancelBtn.hidden = true;
            } else {
                rangeStatus.hidden = true;
                if (rangeCancelBtn) rangeCancelBtn.hidden = true;
            }
        }
    }

    function clearPendingRange() {
        pendingRangeStart = null;
        savePendingRange(null);
    }

    function navigateRange(startStr, endStr, fallbackHref) {
        clearPendingRange();
        const ordered = startStr <= endStr ? [startStr, endStr] : [endStr, startStr];
        const url = new URL(fallbackHref, window.location.origin);
        url.searchParams.set('month', ordered[0].slice(0, 7));
        url.searchParams.set('date', ordered[0]);
        if (ordered[0] === ordered[1]) {
            url.searchParams.delete('endDate');
        } else {
            url.searchParams.set('endDate', ordered[1]);
        }
        window.location.assign(url.toString());
    }

    // Restore pending cross-month range start if user stepped to another month
    const storedStart = readPendingRange();
    if (storedStart && monthSection) {
        pendingRangeStart = storedStart;
        isRangeMode = true;
        syncRangeUi();
    }

    modeButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            isRangeMode = btn.dataset.rangeMode === 'range';
            clearPendingRange();
            syncRangeUi();
        });
    });

    if (rangeCancelBtn) {
        rangeCancelBtn.addEventListener('click', () => {
            clearPendingRange();
            syncRangeUi();
        });
    }

    dayTiles.forEach(tile => {
        tile.addEventListener('click', event => {
            const clickedDate = tile.dataset.calendarDay;
            if (!clickedDate) return;

            if (event.shiftKey && monthSection?.dataset.selectedStart) {
                event.preventDefault();
                navigateRange(monthSection.dataset.selectedStart, clickedDate, tile.href);
                return;
            }

            if (!isRangeMode) {
                clearPendingRange();
                return;
            }

            event.preventDefault();
            if (!pendingRangeStart) {
                pendingRangeStart = clickedDate;
                savePendingRange(clickedDate);
                syncRangeUi();
            } else {
                navigateRange(pendingRangeStart, clickedDate, tile.href);
            }
        });
    });

    const tabs = [...workspace.querySelectorAll('[data-panel-tab]')];
    function selectTab(tab) {
        tabs.forEach(item => {
            const selected = item === tab;
            item.setAttribute('aria-selected', String(selected));
            item.tabIndex = selected ? 0 : -1;
            document.getElementById(item.getAttribute('aria-controls')).hidden = !selected;
        });
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

    const tugSearch = document.getElementById('tugSearch');
    const tugScope = document.getElementById('tugScope');
    function filterTugs() {
        const search = tugSearch.value.trim().toLowerCase();
        let count = 0;
        workspace.querySelectorAll('[data-calendar-tug]').forEach(row => {
            const matchesScope = tugScope.value === 'all' || (row.dataset.booked === 'true') === (tugScope.value === 'booked');
            row.hidden = !matchesScope || !row.dataset.search.toLowerCase().includes(search);
            if (!row.hidden) count++;
        });
        document.getElementById('noCalendarTugs').hidden = count > 0;
    }
    if (tugSearch) {
        tugSearch.addEventListener('input', filterTugs);
        $(tugScope).on('change', filterTugs);
        filterTugs();
    }

    let activePreview = null;
    let activeTrigger = null;
    let dismissTimer;
    function keepPreview() {
        window.clearTimeout(dismissTimer);
    }
    function closePreview() {
        if (activePreview?.matches(':popover-open')) activePreview.hidePopover();
    }
    function dismissPreview() {
        keepPreview();
        dismissTimer = window.setTimeout(() => {
            if (!activePreview?.contains(document.activeElement) && !activeTrigger?.matches(':focus-visible')) closePreview();
        }, 200);
    }
    function positionPreview() {
        if (!activePreview?.matches(':popover-open') || !activeTrigger) return;
        const rect = (activeTrigger.closest('.schedule-calendar-entry') || activeTrigger).getBoundingClientRect();
        const width = activePreview.offsetWidth;
        const height = activePreview.offsetHeight;
        const fitsLeft = rect.left - width - 12 >= 8;
        const fitsRight = rect.right + width + 12 <= window.innerWidth - 8;
        const left = fitsLeft ? rect.left - width - 12 : fitsRight ? rect.right + 12 : rect.left;
        const top = fitsLeft || fitsRight ? rect.top + (rect.height - height) / 2
            : rect.bottom + height + 8 > window.innerHeight ? rect.top - height - 8 : rect.bottom + 8;
        activePreview.style.left = `${Math.max(8, Math.min(left, window.innerWidth - width - 8))}px`;
        activePreview.style.top = `${Math.max(8, Math.min(top, window.innerHeight - height - 8))}px`;
    }
    workspace.querySelectorAll('[data-preview-id]').forEach(trigger => {
        const preview = document.getElementById(trigger.dataset.previewId);
        if (!preview?.showPopover) return;
        function showPreview() {
            keepPreview();
            if (activePreview !== preview) closePreview();
            activePreview = preview;
            activeTrigger = trigger;
            if (!preview.matches(':popover-open')) preview.showPopover();
            positionPreview();
        }
        trigger.addEventListener('pointerenter', event => {
            if (event.pointerType !== 'touch') showPreview();
        });
        trigger.addEventListener('pointerleave', dismissPreview);
        trigger.addEventListener('focus', showPreview);
        trigger.addEventListener('blur', dismissPreview);
    });
    workspace.querySelectorAll('.schedule-vessel-preview').forEach(preview => {
        preview.addEventListener('pointerenter', keepPreview);
        preview.addEventListener('pointerleave', dismissPreview);
        preview.addEventListener('focusin', keepPreview);
        preview.addEventListener('focusout', dismissPreview);
    });
    window.addEventListener('resize', positionPreview);
    document.addEventListener('scroll', event => {
        if (!activePreview?.contains(event.target)) positionPreview();
    }, { capture: true, passive: true });
})();
