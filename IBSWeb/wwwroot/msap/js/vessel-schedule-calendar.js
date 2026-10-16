(() => {
    'use strict';

    const workspace = document.querySelector('.schedule-calendar-workspace');
    if (!workspace) return;

    const form = document.getElementById('calendarFilters');
    const port = document.getElementById('calendarPort');
    const terminal = document.getElementById('calendarTerminal');
    $(port).on('change', () => {
        $(terminal).val('').trigger('change');
        form.requestSubmit();
    });
    document.getElementById('calendarMonth').addEventListener('change', event => {
        const selectedDate = form.querySelector('[name="date"]');
        if (selectedDate && /^\d{4}-\d{2}$/.test(event.target.value)) {
            const [year, month] = event.target.value.split('-').map(Number);
            const day = Math.min(Number(selectedDate.value.slice(-2)), new Date(year, month, 0).getDate());
            selectedDate.value = `${event.target.value}-${String(day).padStart(2, '0')}`;
        }
        form.requestSubmit();
    });

    workspace.querySelectorAll('.schedule-day').forEach(cell => {
        cell.addEventListener('click', event => {
            if (!event.target.closest('a, button')) {
                cell.querySelector('[data-calendar-day]').click();
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
