(() => {
    'use strict';
    const editor = document.getElementById('rateEditor');
    if (!editor) return;

    const canEdit = editor.dataset.canEdit === 'true';
    const isCombined = editor.dataset.mode === 'combined';
    const model = JSON.parse(document.getElementById('rateEditorData').textContent);
    const body = document.getElementById('rateEditorRows');
    const error = document.getElementById('rateEditorError');
    const saveBtn = document.getElementById('rateSave');
    const submitBtn = document.getElementById('rateSubmit');
    const approveBtn = document.getElementById('rateApprove');
    const filters = ['Customer', 'Port', 'Terminal', 'Service'].map(name => document.getElementById('rate' + name));
    const search = document.getElementById('rateSearch');
    const changedOnly = document.getElementById('rateChangedOnly');
    const pendingOnly = document.getElementById('ratePendingOnly');
    const paginationEl = document.getElementById('ratePagination');
    const prevPageBtn = document.getElementById('ratePrevPage');
    const nextPageBtn = document.getElementById('rateNextPage');
    const pageLabelEl = document.getElementById('ratePageLabel');

    const PAGE_SIZE = 100;
    let currentPage = 1;
    let dirty = false;
    let saving = false;

    const escape = value => $('<div>').text(value ?? '').html();
    const showError = message => {
        error.textContent = message || '';
        error.hidden = !message;
    };

    const normalizeAmount = raw => {
        const trimmed = (raw || '').trim();
        if (!trimmed) return '';
        return /^\d{1,3}(,\d{3})+(\.\d{1,4})?$/.test(trimmed) ? trimmed.replaceAll(',', '') : trimmed;
    };

    const scaled = value => {
        const normalized = normalizeAmount(value);
        if (!/^\d{1,14}(?:\.\d{1,4})?$/.test(normalized)) return null;
        const [whole, fraction = ''] = normalized.split('.');
        return BigInt(whole) * 10000n + BigInt(fraction.padEnd(4, '0'));
    };

    const validDate = value => /^\d{4}-\d{2}-\d{2}$/.test(value)
        && value >= '1900-01-01'
        && value <= '9998-12-31'
        && !Number.isNaN(Date.parse(value))
        && new Date(value + 'T00:00:00Z').toISOString().slice(0, 10) === value;

    const isTuesday = value => validDate(value) && new Date(value + 'T00:00:00Z').getUTCDay() === 2;

    const bafCycleStart = isoDate => {
        if (!validDate(isoDate)) return isoDate;
        const d = new Date(isoDate + 'T00:00:00Z');
        const daysSinceTuesday = (d.getUTCDay() - 2 + 7) % 7;
        d.setUTCDate(d.getUTCDate() - daysSinceTuesday);
        return d.toISOString().slice(0, 10);
    };

    const formatMoney = value => value == null
        ? 'No previous rate'
        : '₱' + Number(value).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 4 });

    // Normalize single vs combined rows into uniform state objects.
    const rows = isCombined
        ? (model.combinedRows || []).map(row => ({
            ...row,
            tariffAmount: row.tariffAmountText ?? '',
            bafAmount: row.bafAmountText ?? '',
            originalTariffAmount: row.tariffAmountText ?? '',
            originalTariffDate: row.tariffEffectiveDate,
            originalBafAmount: row.bafAmountText ?? '',
            originalBafDate: row.bafEffectiveDate
        }))
        : (model.rows || []).map(row => ({
            ...row,
            amount: row.amountText ?? '',
            originalAmount: row.amountText ?? '',
            originalDate: row.effectiveDate
        }));

    // Change detection helpers
    const singleChanged = row => !row.existingRateId
        || row.effectiveDate !== row.baselineDate
        || scaled(row.amount) !== scaled(row.storedAmountText ?? '');

    const singleIsNewDate = row => !row.baselineRateId || row.effectiveDate !== row.baselineDate;

    const combinedTariffChanged = row => {
        const amt = normalizeAmount(row.tariffAmount);
        if (amt === '') return false;
        return !row.tariffExistingRateId
            || row.tariffEffectiveDate !== row.tariffBaselineDate
            || scaled(amt) !== scaled(row.tariffStoredAmountText ?? '');
    };

    const combinedBafChanged = row => {
        const amt = normalizeAmount(row.bafAmount);
        if (amt === '') return false;
        return !row.bafExistingRateId
            || row.bafEffectiveDate !== row.bafBaselineDate
            || scaled(amt) !== scaled(row.bafStoredAmountText ?? '');
    };

    const rowChanged = row => isCombined
        ? (combinedTariffChanged(row) || combinedBafChanged(row))
        : singleChanged(row);

    const rowVisible = row => {
        if (!filters.every((filter, index) => !filter.value || row[['customerId', 'portId', 'terminalId', 'serviceId'][index]] === Number(filter.value))) {
            return false;
        }
        if (changedOnly.checked && !rowChanged(row)) {
            return false;
        }
        if (isCombined && pendingOnly?.checked && !row.isPendingTariff && !row.isPendingBaf && !rowChanged(row)) {
            return false;
        }
        const query = search.value.trim().toLowerCase();
        if (!query) return true;
        return `${row.customerCode} ${row.customerName} ${row.portName} ${row.terminalName} ${row.serviceName}`.toLowerCase().includes(query);
    };

    function addCell(tr, text) {
        const cell = document.createElement('td');
        cell.textContent = text;
        tr.append(cell);
        return cell;
    }

    function createSingleRowElement(row, pageRows) {
        const tr = document.createElement('tr');
        addCell(tr, `${row.customerCode} · ${row.customerName}`);
        addCell(tr, `${row.portName} / ${row.terminalName}`);
        addCell(tr, row.serviceName);
        addCell(tr, row.baselineDate ? `${row.baselineDate} / ${row.storedAmountText ?? ''}` : 'No previous rate');

        ['effectiveDate', 'amount'].forEach((key, column) => {
            const input = document.createElement('input');
            input.type = column === 0 ? 'date' : 'text';
            input.className = 'modern-input';
            input.dataset.column = column;
            input.value = row[key];
            input.setAttribute('aria-label', `${row.customerName}, ${row.serviceName}: ${column === 0 ? 'effective date' : model.kind === 2 ? 'BAF amount' : 'tariff amount'}`);
            input.required = canEdit;
            input.readOnly = !canEdit;
            if (column === 0) {
                input.min = '1900-01-01';
                input.max = '9998-12-31';
                row.dateInput = input;
            } else {
                input.inputMode = 'decimal';
                input.autocomplete = 'off';
                row.amountInput = input;
            }

            input.addEventListener('input', () => {
                if (!canEdit) return;
                row[key] = input.value.trim();
                dirty = true;
                syncSingleRowClasses(row, tr);
                updateCountsOnly();
            });

            input.addEventListener('keydown', event => {
                if (event.key !== 'Enter') return;
                event.preventDefault();
                const idx = pageRows.indexOf(row);
                const next = pageRows[idx + (event.shiftKey ? -1 : 1)];
                next?.[column === 0 ? 'dateInput' : 'amountInput']?.focus();
            });

            input.addEventListener('paste', event => {
                if (!canEdit) { event.preventDefault(); return; }
                const text = event.clipboardData.getData('text').trim();
                if (!/[\t\r\n]/.test(text)) return;
                event.preventDefault();
                const cells = text.split(/\r?\n/).map(line => line.split('\t'));
                const start = pageRows.indexOf(row);
                const changes = [];
                for (let index = 0; index < cells.length; index++) {
                    if (!pageRows[start + index] || cells[index].length + column > 2) {
                        showError('Paste only into visible date and amount cells on this page. Add new customer/service combinations before pasting.');
                        return;
                    }
                    for (let offset = 0; offset < cells[index].length; offset++) {
                        const target = column + offset;
                        let value = cells[index][offset].trim();
                        if (target === 1 && /^\d{1,3}(,\d{3})+(\.\d{1,4})?$/.test(value)) value = value.replaceAll(',', '');
                        if (target === 0 ? !validDate(value) : scaled(value) === null) {
                            showError(`Paste row ${index + 1}: use ISO dates (yyyy-mm-dd) and nonnegative amounts with at most four decimals.`);
                            return;
                        }
                        changes.push([pageRows[start + index], target, value]);
                    }
                }
                changes.forEach(([item, target, value]) => {
                    item[target === 0 ? 'effectiveDate' : 'amount'] = value;
                    const targetInput = item[target === 0 ? 'dateInput' : 'amountInput'];
                    if (targetInput) targetInput.value = value;
                    if (item.element) syncSingleRowClasses(item, item.element);
                });
                dirty = true;
                showError('');
                updateCountsOnly();
            });

            addCell(tr, '').append(input);
        });

        const action = addCell(tr, '');
        row.status = document.createElement('span');
        action.append(row.status);
        const discard = document.createElement('button');
        discard.type = 'button';
        discard.className = 'rate-discard';
        discard.textContent = 'Remove from draft';
        discard.addEventListener('click', () => {
            rows.splice(rows.indexOf(row), 1);
            dirty = true;
            renderPage();
        });
        row.discard = discard;
        action.append(discard);

        row.element = tr;
        syncSingleRowClasses(row, tr);
        return tr;
    }

    function syncSingleRowClasses(row, tr) {
        const modified = singleChanged(row);
        const isNewDate = singleIsNewDate(row);
        tr.classList.toggle('rate-row-new', modified && isNewDate);
        tr.classList.toggle('rate-row-changed', modified && !isNewDate);
        row.dateInput.classList.toggle('rate-cell-new', isNewDate);
        row.amountInput.classList.toggle('rate-cell-changed', row.storedAmountText != null && scaled(row.amount) !== scaled(row.storedAmountText));
        row.amountInput.classList.toggle('rate-cell-new', row.storedAmountText == null);
        row.discard.hidden = !canEdit || (!modified && !model.id);
        const valid = validDate(row.effectiveDate) && scaled(row.amount) !== null;
        row.dateInput.setAttribute('aria-invalid', String(!validDate(row.effectiveDate)));
        row.amountInput.setAttribute('aria-invalid', String(scaled(row.amount) === null));
        row.status.textContent = !valid ? 'Check date / amount' : modified ? (isNewDate ? 'New dated rate' : 'Correction') : 'Unchanged';
    }

    function createCombinedRowElement(row) {
        const tr = document.createElement('tr');
        addCell(tr, `${row.customerCode} · ${row.customerName}`);
        addCell(tr, `${row.portName} / ${row.terminalName}`);

        const serviceCell = addCell(tr, '');
        const svcStrong = document.createElement('strong');
        svcStrong.textContent = row.serviceName;
        serviceCell.append(svcStrong);
        if (row.contextNote) {
            const note = document.createElement('small');
            note.className = 'ui-caption ui-muted';
            note.style.display = 'block';
            note.textContent = row.contextNote;
            serviceCell.append(note);
        }

        // Basic (Tariff) cell
        const tariffCell = addCell(tr, '');
        const tariffGroup = document.createElement('div');
        tariffGroup.className = 'rate-combined-inputs';
        const tDate = document.createElement('input');
        tDate.type = 'date';
        tDate.className = 'modern-input';
        tDate.min = '1900-01-01';
        tDate.max = '9998-12-31';
        tDate.value = row.tariffEffectiveDate;
        tDate.readOnly = !canEdit;
        tDate.setAttribute('aria-label', `${row.customerName}, ${row.serviceName} Basic effective date`);

        const tAmt = document.createElement('input');
        tAmt.type = 'text';
        tAmt.inputMode = 'decimal';
        tAmt.autocomplete = 'off';
        tAmt.className = 'modern-input';
        tAmt.placeholder = row.isPendingTariff ? 'Enter Basic ₱' : 'Basic ₱';
        tAmt.value = row.tariffAmount;
        tAmt.readOnly = !canEdit;
        tAmt.setAttribute('aria-label', `${row.customerName}, ${row.serviceName} Basic amount`);

        row.tDateInput = tDate;
        row.tAmtInput = tAmt;
        tariffGroup.append(tDate, tAmt);
        const tMeta = document.createElement('small');
        tMeta.className = 'ui-caption ui-muted';
        tMeta.style.display = 'block';
        tMeta.textContent = row.tariffBaselineDate
            ? `Prev: ${row.tariffBaselineDate} / ${row.tariffStoredAmountText ?? ''}`
            : 'No previous Basic rate';
        tariffCell.append(tariffGroup, tMeta);

        // BAF cell
        const bafCell = addCell(tr, '');
        const bafGroup = document.createElement('div');
        bafGroup.className = 'rate-combined-inputs';
        const bDate = document.createElement('input');
        bDate.type = 'date';
        bDate.className = 'modern-input';
        bDate.min = '1900-01-02';
        bDate.step = '7';
        bDate.max = '9998-12-31';
        bDate.value = row.bafEffectiveDate;
        bDate.readOnly = !canEdit;
        bDate.setAttribute('aria-label', `${row.customerName}, ${row.serviceName} BAF effective date (Tuesday)`);

        const bAmt = document.createElement('input');
        bAmt.type = 'text';
        bAmt.inputMode = 'decimal';
        bAmt.autocomplete = 'off';
        bAmt.className = 'modern-input';
        bAmt.placeholder = row.isPendingBaf ? '0.00 or BAF ₱' : 'BAF ₱';
        bAmt.value = row.bafAmount;
        bAmt.readOnly = !canEdit;
        bAmt.setAttribute('aria-label', `${row.customerName}, ${row.serviceName} BAF amount`);

        row.bDateInput = bDate;
        row.bAmtInput = bAmt;
        bafGroup.append(bDate, bAmt);
        const bMeta = document.createElement('small');
        bMeta.className = 'ui-caption ui-muted';
        bMeta.style.display = 'block';
        bMeta.textContent = row.bafBaselineDate
            ? `Prev: ${row.bafBaselineDate} / ${row.bafStoredAmountText ?? ''}`
            : 'No previous BAF rate';
        bafCell.append(bafGroup, bMeta);

        // Status cell
        const actionCell = addCell(tr, '');
        row.status = document.createElement('span');
        row.status.className = 'ui-caption';
        actionCell.append(row.status);
        const discard = document.createElement('button');
        discard.type = 'button';
        discard.className = 'rate-discard';
        discard.textContent = 'Remove';
        discard.addEventListener('click', () => {
            rows.splice(rows.indexOf(row), 1);
            dirty = true;
            renderPage();
        });
        row.discard = discard;
        actionCell.append(discard);

        const onInput = () => {
            if (!canEdit) return;
            row.tariffEffectiveDate = tDate.value;
            row.tariffAmount = tAmt.value.trim();
            row.bafEffectiveDate = bDate.value;
            row.bafAmount = bAmt.value.trim();
            dirty = true;
            syncCombinedRowClasses(row, tr);
            updateCountsOnly();
        };
        [tDate, tAmt, bDate, bAmt].forEach(el => el.addEventListener('input', onInput));

        row.element = tr;
        syncCombinedRowClasses(row, tr);
        return tr;
    }

    function syncCombinedRowClasses(row, tr) {
        const tChanged = combinedTariffChanged(row);
        const bChanged = combinedBafChanged(row);
        const modified = tChanged || bChanged;

        const tAmtNorm = normalizeAmount(row.tariffAmount);
        const tHasAmt = tAmtNorm !== '';
        const tValid = !tHasAmt || (validDate(row.tariffEffectiveDate) && scaled(tAmtNorm) !== null && (!model.scheduleId || Number(tAmtNorm) > 0));
        row.tDateInput.setAttribute('aria-invalid', String(tHasAmt && !validDate(row.tariffEffectiveDate)));
        row.tAmtInput.setAttribute('aria-invalid', String(tHasAmt && (scaled(tAmtNorm) === null || (model.scheduleId && Number(tAmtNorm) <= 0))));
        row.tAmtInput.classList.toggle('rate-cell-new', tChanged && !row.tariffExistingRateId);
        row.tAmtInput.classList.toggle('rate-cell-changed', tChanged && !!row.tariffExistingRateId);

        const bAmtNorm = normalizeAmount(row.bafAmount);
        const bHasAmt = bAmtNorm !== '';
        const bValid = !bHasAmt || (isTuesday(row.bafEffectiveDate) && scaled(bAmtNorm) !== null);
        row.bDateInput.setAttribute('aria-invalid', String(bHasAmt && !isTuesday(row.bafEffectiveDate)));
        row.bAmtInput.setAttribute('aria-invalid', String(bHasAmt && scaled(bAmtNorm) === null));
        row.bAmtInput.classList.toggle('rate-cell-new', bChanged && !row.bafExistingRateId);
        row.bAmtInput.classList.toggle('rate-cell-changed', bChanged && !!row.bafExistingRateId);

        const isNew = (tChanged && !row.tariffExistingRateId) || (bChanged && !row.bafExistingRateId);
        tr.classList.toggle('rate-row-new', modified && isNew);
        tr.classList.toggle('rate-row-changed', modified && !isNew);
        row.discard.hidden = !canEdit || !row.isAdded;

        const statuses = [];
        if (!tValid || !bValid) {
            statuses.push(!tValid ? 'Check Basic' : 'Check BAF (Tue)');
        } else {
            if (tChanged) statuses.push(`Basic: ${!row.tariffExistingRateId ? 'New' : 'Correction'}`);
            else if (row.isPendingTariff) statuses.push('Basic: Pending');
            if (bChanged) statuses.push(`BAF: ${!row.bafExistingRateId ? 'New' : 'Correction'}`);
            else if (row.isPendingBaf) statuses.push('BAF: Pending');
        }
        row.status.textContent = statuses.length ? statuses.join(' · ') : 'Up to date';
    }

    function updateCountsOnly() {
        const matched = rows.filter(rowVisible);
        const totalChanged = rows.filter(rowChanged).length;
        const hiddenChanges = rows.filter(r => rowChanged(r) && !rowVisible(r)).length;
        document.getElementById('rateCounts').textContent = `${matched.length} matching · ${totalChanged} changed · ${hiddenChanges} hidden changes`;
    }

    function renderPage() {
        const matched = rows.filter(rowVisible);
        const totalPages = Math.max(1, Math.ceil(matched.length / PAGE_SIZE));
        if (currentPage > totalPages) currentPage = totalPages;
        const start = (currentPage - 1) * PAGE_SIZE;
        const pageRows = matched.slice(start, start + PAGE_SIZE);

        body.replaceChildren();
        const fragment = document.createDocumentFragment();
        if (pageRows.length === 0) {
            const emptyTr = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = isCombined ? 6 : 7;
            td.className = 'ui-caption ui-muted';
            td.textContent = 'No matching rate rows. Adjust filters or use "+ Add rate for selected combination".';
            emptyTr.append(td);
            fragment.append(emptyTr);
        } else {
            pageRows.forEach(row => {
                fragment.append(isCombined ? createCombinedRowElement(row) : createSingleRowElement(row, pageRows));
            });
        }
        body.append(fragment);

        if (paginationEl) {
            paginationEl.hidden = totalPages <= 1;
            prevPageBtn.disabled = currentPage <= 1;
            nextPageBtn.disabled = currentPage >= totalPages;
            pageLabelEl.textContent = `Page ${currentPage} of ${totalPages} (${matched.length} rows)`;
        }
        updateCountsOnly();
    }

    prevPageBtn?.addEventListener('click', () => {
        if (currentPage > 1) { currentPage--; renderPage(); }
    });
    nextPageBtn?.addEventListener('click', () => {
        currentPage++; renderPage();
    });

    const resetAndRender = () => { currentPage = 1; renderPage(); };
    $(filters).on('change', resetAndRender);
    [search, changedOnly, pendingOnly].filter(Boolean).forEach(control => control.addEventListener('input', resetAndRender));

    $(filters[1]).on('change', () => {
        const terminal = filters[2];
        const previous = terminal.value;
        terminal.replaceChildren(new Option('All terminals', ''));
        model.terminals
            .filter(item => !filters[1].value || item.portId === Number(filters[1].value))
            .forEach(item => terminal.add(new Option(item.name, item.id)));
        terminal.value = Array.from(terminal.options).some(option => option.value === previous) ? previous : '';
        window.refreshModernSelect?.($(terminal));
        $(terminal).trigger('change');
        resetAndRender();
    });

    // Apply initial selections from query/schedule context
    if (model.selectedCustomerId) {
        filters[0].value = String(model.selectedCustomerId);
        window.refreshModernSelect?.($(filters[0]));
    }
    if (model.selectedPortId) {
        filters[1].value = String(model.selectedPortId);
        window.refreshModernSelect?.($(filters[1]));
        const terminal = filters[2];
        terminal.replaceChildren(new Option('All terminals', ''));
        model.terminals
            .filter(item => item.portId === Number(filters[1].value))
            .forEach(item => terminal.add(new Option(item.name, item.id)));
        if (model.selectedTerminalId) terminal.value = String(model.selectedTerminalId);
        window.refreshModernSelect?.($(terminal));
    } else if (model.selectedTerminalId) {
        filters[2].value = String(model.selectedTerminalId);
        window.refreshModernSelect?.($(filters[2]));
    }
    if (model.selectedServiceId) {
        filters[3].value = String(model.selectedServiceId);
        window.refreshModernSelect?.($(filters[3]));
    }

    // Seed single-mode row when navigated from a legacy link with all 4 IDs
    if (!isCombined && canEdit && model.selectedCustomerId && model.selectedPortId && model.selectedTerminalId && model.selectedServiceId) {
        const existingRow = rows.find(r => r.customerId === Number(model.selectedCustomerId)
            && r.portId === Number(model.selectedPortId)
            && r.terminalId === Number(model.selectedTerminalId)
            && r.serviceId === Number(model.selectedServiceId));
        if (!existingRow) {
            const customer = model.customers.find(item => item.id === Number(model.selectedCustomerId));
            const port = model.ports.find(item => item.id === Number(model.selectedPortId));
            const terminal = model.terminals.find(item => item.id === Number(model.selectedTerminalId));
            const service = model.services.find(item => item.id === Number(model.selectedServiceId));
            if (customer && port && terminal && service && terminal.portId === port.id) {
                const initialAmt = model.initialAmount != null ? String(model.initialAmount) : '';
                rows.unshift({
                    customerId: customer.id, customerCode: customer.code, customerName: customer.name,
                    portId: port.id, portName: port.name,
                    terminalId: terminal.id, terminalName: terminal.name,
                    serviceId: service.id, serviceName: service.name,
                    effectiveDate: model.asOfDate, amount: initialAmt,
                    originalAmount: '', originalDate: model.asOfDate
                });
                dirty = true;
            }
        } else if (model.initialAmount != null && (!existingRow.amount || scaled(existingRow.amount) === null)) {
            existingRow.amount = String(model.initialAmount);
            dirty = true;
        }
    }

    document.getElementById('rateAdd')?.addEventListener('click', () => {
        if (filters.some(filter => !filter.value)) {
            showError('Select a customer, port, terminal and service first.');
            return;
        }
        const customer = model.customers.find(item => item.id === Number(filters[0].value));
        const port = model.ports.find(item => item.id === Number(filters[1].value));
        const terminal = model.terminals.find(item => item.id === Number(filters[2].value));
        const service = model.services.find(item => item.id === Number(filters[3].value));
        if (terminal.portId !== port.id) {
            showError('The selected terminal must belong to the port.');
            return;
        }

        if (isCombined) {
            const existing = rows.find(r => r.customerId === customer.id && r.terminalId === terminal.id && r.serviceId === service.id);
            if (existing) {
                showError('This customer, terminal and service already exists in the table. Edit it directly.');
                renderPage();
                existing.tAmtInput?.focus();
                return;
            }
            const newRow = {
                customerId: customer.id, customerCode: customer.code, customerName: customer.name,
                portId: port.id, portName: port.name,
                terminalId: terminal.id, terminalName: terminal.name,
                serviceId: service.id, serviceName: service.name,
                tariffEffectiveDate: model.asOfDate, tariffAmount: '',
                bafEffectiveDate: bafCycleStart(model.asOfDate), bafAmount: '',
                isPendingTariff: true, isPendingBaf: true, isAdded: true
            };
            rows.unshift(newRow);
            currentPage = 1;
            dirty = true;
            showError('');
            renderPage();
            newRow.tAmtInput?.focus();
        } else {
            const row = {
                customerId: customer.id, customerCode: customer.code, customerName: customer.name,
                portId: port.id, portName: port.name,
                terminalId: terminal.id, terminalName: terminal.name,
                serviceId: service.id, serviceName: service.name,
                effectiveDate: model.asOfDate, amount: '', originalAmount: '', originalDate: model.asOfDate
            };
            rows.unshift(row);
            currentPage = 1;
            dirty = true;
            showError('');
            renderPage();
            row.amountInput?.focus();
        }
    });

    function collectBatchPayload(action) {
        if (!isCombined) {
            const selected = rows.filter(row => model.id || singleChanged(row));
            if (!selected.length) {
                return { error: 'Edit an amount or effective date, or add a new rate first.' };
            }
            const invalid = selected.find(row => !validDate(row.effectiveDate) || scaled(row.amount) === null);
            if (invalid) {
                return { error: 'Correct invalid dates and amounts before saving. Hidden edited rows are included.' };
            }
            const keys = new Set();
            for (const row of selected) {
                const key = [row.customerId, row.terminalId, row.serviceId, row.effectiveDate].join('|');
                if (keys.has(key)) {
                    return { error: 'Duplicate customer, terminal, service and effective date. Each combination/date must occur once.' };
                }
                keys.add(key);
            }
            const mapped = selected.map(row => ({
                customerId: row.customerId, portId: row.portId, terminalId: row.terminalId, serviceId: row.serviceId,
                effectiveDate: row.effectiveDate, amount: normalizeAmount(row.amount),
                sourceScheduleId: row.sourceScheduleId ?? model.scheduleId ?? null,
                sourceServiceEntryId: row.sourceServiceEntryId ?? null,
                sourceRevision: row.sourceRevision ?? null
            }));
            return {
                action,
                scheduleId: model.scheduleId ?? null,
                tariffId: model.kind === 1 ? model.id : null,
                bafId: model.kind === 2 ? model.id : null,
                requireTuesdayForBaf: model.scheduleId != null,
                tariffRows: model.kind === 1 ? mapped : [],
                bafRows: model.kind === 2 ? mapped : []
            };
        }

        const tariffRows = [];
        const bafRows = [];
        const tKeys = new Set();
        const bKeys = new Set();

        for (const row of rows) {
            if (combinedTariffChanged(row)) {
                const amt = normalizeAmount(row.tariffAmount);
                if (!validDate(row.tariffEffectiveDate) || scaled(amt) === null || (model.scheduleId && Number(amt) <= 0)) {
                    return { error: `Check Basic date/amount for ${row.customerName} · ${row.serviceName}.` };
                }
                const key = [row.customerId, row.terminalId, row.serviceId, row.tariffEffectiveDate].join('|');
                if (tKeys.has(key)) return { error: 'Duplicate Basic customer, terminal, service and effective date.' };
                tKeys.add(key);
                tariffRows.push({
                    customerId: row.customerId, portId: row.portId, terminalId: row.terminalId, serviceId: row.serviceId,
                    effectiveDate: row.tariffEffectiveDate, amount: amt,
                    sourceScheduleId: row.sourceScheduleId ?? model.scheduleId ?? null,
                    sourceServiceEntryId: row.sourceServiceEntryId ?? null,
                    sourceRevision: row.sourceRevision ?? null
                });
            }
            if (combinedBafChanged(row)) {
                const amt = normalizeAmount(row.bafAmount);
                if (!isTuesday(row.bafEffectiveDate) || scaled(amt) === null) {
                    return { error: `Check BAF Tuesday date/amount for ${row.customerName} · ${row.serviceName}.` };
                }
                const key = [row.customerId, row.terminalId, row.serviceId, row.bafEffectiveDate].join('|');
                if (bKeys.has(key)) return { error: 'Duplicate BAF customer, terminal, service and effective date.' };
                bKeys.add(key);
                bafRows.push({
                    customerId: row.customerId, portId: row.portId, terminalId: row.terminalId, serviceId: row.serviceId,
                    effectiveDate: row.bafEffectiveDate, amount: amt,
                    sourceScheduleId: row.sourceScheduleId ?? model.scheduleId ?? null,
                    sourceServiceEntryId: row.sourceServiceEntryId ?? null,
                    sourceRevision: row.sourceRevision ?? null
                });
            }
        }

        if (!tariffRows.length && !bafRows.length) {
            return { error: 'Enter or modify at least one Basic or BAF rate before continuing.' };
        }

        return {
            action,
            scheduleId: model.scheduleId ?? null,
            requireTuesdayForBaf: true,
            tariffRows,
            bafRows
        };
    }

    async function runBatchAction(action) {
        if (saving) return;
        const payload = collectBatchPayload(action);
        if (payload.error) {
            showError(payload.error);
            return;
        }

        saving = true;
        [saveBtn, submitBtn, approveBtn].filter(Boolean).forEach(b => { b.disabled = true; });
        showError('');
        const token = editor.querySelector('[name="__RequestVerificationToken"]').value;

        try {
            if (action === 'draft' && !isCombined) {
                const response = await fetch(editor.dataset.saveUrl, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                    body: JSON.stringify({ id: model.id, kind: model.kind, rows: model.kind === 1 ? payload.tariffRows : payload.bafRows })
                });
                if (response.redirected) throw new Error('Your session may have expired. Sign in again and retry.');
                const result = await response.json();
                if (!response.ok || !result.url) throw new Error(result.message || 'The draft could not be saved.');
                dirty = false;
                window.location.assign(result.url);
                return;
            }

            if (action === 'submit' || action === 'approve') {
                const previewResp = await fetch(editor.dataset.previewUrl, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                    body: JSON.stringify(payload)
                });
                if (previewResp.redirected) throw new Error('Your session may have expired. Sign in again and retry.');
                const preview = await previewResp.json();
                if (!previewResp.ok || !preview.fingerprint) throw new Error(preview.message || 'Could not prepare rate comparison.');

                const rowsHtml = preview.items.map(item => `
                    <tr>
                        <td><strong>${escape(item.customerName)}</strong><br><small>${escape(item.serviceName)} · ${escape(item.kindLabel)}</small></td>
                        <td>${escape(item.effectiveDate)}</td>
                        <td>${escape(formatMoney(item.storedAmount))}</td>
                        <td><strong>${escape(formatMoney(item.amount))}</strong><br><small>${escape(item.change)}</small></td>
                    </tr>`).join('');

                const isApprove = action === 'approve';
                const confirmation = await ModernAlert.confirm({
                    title: isApprove ? `Save & approve ${preview.items.length} rate(s)?` : `Submit ${preview.items.length} rate(s) for approval?`,
                    html: `<div class="schedule-batch-preview">
                        <p class="ui-caption ui-muted" style="margin-bottom: .75rem; text-align: left;">
                            ${isApprove
                                ? 'These rates will become active immediately in the rate tables and recalculate eligible unbilled tickets.'
                                : 'Submits these rates to an authorized approver without changing live rates yet.'}
                        </p>
                        <table class="schedule-confirm-table">
                            <thead><tr><th>Customer / Service</th><th>Effective</th><th>Previous</th><th>Proposed</th></tr></thead>
                            <tbody>${rowsHtml}</tbody>
                        </table>
                    </div>`,
                    icon: 'question',
                    confirmText: isApprove ? 'Save & approve' : 'Submit for approval',
                    cancelText: 'Keep editing'
                });
                if (!confirmation.isConfirmed) return;
                payload.fingerprint = preview.fingerprint;
                ModernAlert.showLoading(isApprove ? 'Activating rates…' : 'Submitting rates…');
            }

            const saveResp = await fetch(editor.dataset.batchSaveUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                body: JSON.stringify(payload)
            });
            if (saveResp.redirected) throw new Error('Your session may have expired. Sign in again and retry.');
            const saved = await saveResp.json();
            if (!saveResp.ok || !saved.success) throw new Error(saved.message || 'The rate update could not be saved.');

            dirty = false;
            const targetUrl = (action !== 'draft' && editor.dataset.returnUrl) ? editor.dataset.returnUrl : saved.url;
            if (targetUrl) {
                window.location.assign(targetUrl);
            } else {
                window.location.reload();
            }
        } catch (failure) {
            ModernAlert.close();
            showError(failure.message || 'The rate update could not be saved. Your edits remain here.');
        } finally {
            saving = false;
            [saveBtn, submitBtn, approveBtn].filter(Boolean).forEach(b => { b.disabled = false; });
        }
    }

    saveBtn?.addEventListener('click', () => runBatchAction('draft'));
    submitBtn?.addEventListener('click', () => runBatchAction('submit'));
    approveBtn?.addEventListener('click', () => runBatchAction('approve'));

    window.addEventListener('beforeunload', event => {
        if (dirty) {
            event.preventDefault();
            event.returnValue = '';
        }
    });

    renderPage();
})();
