(function () {
    'use strict';

    const form = document.getElementById('masterImportForm');
    if (!form) return;
    const workspace = document.getElementById('masterImportWorkspace');
    const fingerprint = form.querySelector('[name="ReviewedFingerprint"]');
    const importButton = document.getElementById('importMasters');
    const exportButton = document.getElementById('exportMasters');
    const correctionsButton = document.getElementById('exportMasterCorrections');
    const status = document.getElementById('masterImportStatus');
    const fileSelect = document.getElementById('masterEditorFile');
    const filter = document.getElementById('masterEditorFilter');
    const search = document.getElementById('masterEditorSearch');
    const previous = document.getElementById('masterEditorPrevious');
    const next = document.getElementById('masterEditorNext');
    let busy = false;
    let confirming = false;
    let reviewed = false;
    let validatedOnce = false;
    let documents = [];
    let results = new Map();
    let originals = new Map();
    let page = 0;
    const pageSize = 50;

    function rowKey(document, row) { return document.fileName + '/' + row.row; }
    function cellKey(document, row, column) { return rowKey(document, row) + '/' + column; }
    function isEdited(document, row) {
        return document.columns.some(column => (row.fields[column.key] || '') !== (originals.get(cellKey(document, row, column.key)) || ''));
    }
    function invalidate() {
        fingerprint.value = '';
        reviewed = false;
        importButton.disabled = true;
        exportButton.disabled = true;
    }
    function lock(value) {
        busy = value;
        workspace.querySelectorAll('input, select, button').forEach(control => { control.disabled = value; });
        if (!value) {
            importButton.disabled = !reviewed;
            exportButton.disabled = !reviewed;
            correctionsButton.disabled = !validatedOnce;
            renderEditor();
        }
    }

    function renderEditor() {
        const doc = documents.find(document => document.kind === fileSelect.value);
        const body = document.getElementById('masterImportRows');
        const headers = document.getElementById('masterEditorHeaders');
        body.replaceChildren();
        headers.replaceChildren();
        if (!doc) { previous.disabled = true; next.disabled = true; return; }
        const header = document.createElement('tr');
        ['Source row / result', ...doc.columns.map(column => column.label)].forEach(label => {
            const th = document.createElement('th'); th.scope = 'col'; th.textContent = label; header.append(th);
        });
        headers.append(header);
        const term = search.value.trim().toLowerCase();
        const rows = doc.rows.filter(row => {
            const result = results.get(rowKey(doc, row));
            if (filter.value === 'attention' && result?.action !== 'Invalid' && !result?.warning) return false;
            if (filter.value === 'edited' && !isEdited(doc, row)) return false;
            return !term || Object.values(row.fields).some(value => value.toLowerCase().includes(term));
        });
        page = Math.max(0, Math.min(page, Math.ceil(rows.length / pageSize) - 1));
        const fragment = document.createDocumentFragment();
        rows.slice(page * pageSize, (page + 1) * pageSize).forEach(row => {
            const result = results.get(rowKey(doc, row));
            const tr = document.createElement('tr');
            if (result?.action === 'Invalid') tr.className = 'ui-critical';
            else if (result?.warning) tr.className = 'ui-attention';
            const state = document.createElement('td');
            state.className = 'master-import-row-state';
            const label = document.createElement('strong');
            label.textContent = 'Row ' + row.row + ' · ' + (isEdited(doc, row) ? 'Edited · ' : '') + (result?.action || 'Not validated');
            const detail = document.createElement('p');
            detail.id = 'master-row-' + row.row;
            detail.className = 'ui-caption';
            detail.textContent = result?.action === 'Invalid' ? result.changes : [result?.warning, result?.action === 'Update' || result?.action === 'Skip existing' ? result.changes : ''].filter(Boolean).join(' ');
            state.append(label, detail); tr.append(state);
            doc.columns.forEach(column => {
                const td = document.createElement('td');
                const input = document.createElement(column.choices.length ? 'select' : 'input');
                input.className = 'ui-input';
                if (column.choices.length) {
                    input.add(new Option('Unknown / not supplied', ''));
                    column.choices.forEach(value => input.add(new Option(value, value)));
                    const raw = row.fields[column.key] || '';
                    if (raw && !column.choices.includes(raw)) input.add(new Option(raw, raw));
                } else {
                    input.type = 'text'; input.maxLength = 1000;
                }
                input.value = row.fields[column.key] || '';
                input.setAttribute('aria-label', column.label + ', ' + doc.fileName + ', row ' + row.row);
                input.title = column.note || '';
                input.setAttribute('aria-describedby', detail.id);
                input.disabled = busy;
                const original = originals.get(cellKey(doc, row, column.key)) || '';
                input.classList.toggle('master-import-cell-edited', input.value !== original);
                if (result?.action === 'Invalid' && result.changes.toUpperCase().includes(column.key)) input.setAttribute('aria-invalid', 'true');
                input.addEventListener('input', function () {
                    row.fields[column.key] = input.value;
                    input.classList.toggle('master-import-cell-edited', input.value !== original);
                    label.textContent = 'Row ' + row.row + ' · Edited · validate again';
                    input.removeAttribute('aria-invalid');
                    invalidate();
                    status.textContent = 'Edits are kept in this workspace. Validate again before import or cleaned export.';
                });
                td.append(input); tr.append(td);
            });
            fragment.append(tr);
        });
        body.append(fragment);
        previous.disabled = busy || page === 0;
        next.disabled = busy || (page + 1) * pageSize >= rows.length;
        document.getElementById('masterEditorPage').textContent = rows.length ? 'Showing ' + (page * pageSize + 1) + '–' + Math.min((page + 1) * pageSize, rows.length) + ' of ' + rows.length : 'No matching records.';
    }
    [fileSelect, filter, search].forEach(control => control.addEventListener(control === search ? 'input' : 'change', () => { page = 0; renderEditor(); }));
    previous.addEventListener('click', () => { page--; renderEditor(); });
    next.addEventListener('click', () => { page++; renderEditor(); });

    form.addEventListener('change', function (event) {
        if (busy) return;
        invalidate();
        if (event.target.type === 'file') {
            const other = document.getElementById(event.target.id === 'masterFiles' ? 'masterFolder' : 'masterFiles');
            other.value = '';
            documents = []; results.clear(); originals.clear();
            validatedOnce = false;
            correctionsButton.disabled = true;
            fileSelect.replaceChildren(new Option('Load files to begin', ''));
            renderEditor();
        }
        status.textContent = 'Selection or options changed. Validate and review again.';
    });

    async function request(importing, exporting, corrections = false) {
        if (busy || confirming) return;
        if ((importing || exporting && !corrections) && (!reviewed || !fingerprint.value)) return;
        if (corrections && !validatedOnce) return;
        if (importing) {
            confirming = true;
            let result;
            try {
                result = await ModernAlert.confirm({ title: 'Import reviewed master files?', text: 'All valid additions and selected updates will be saved together.', confirmText: 'Import files', cancelText: 'Keep reviewing' });
            } finally { confirming = false; }
            if (!result.isConfirmed || !reviewed || !fingerprint.value) return;
        }
        const data = new FormData(form);
        data.set('import', importing ? 'true' : 'false');
        if (documents.length) data.set('EditedFilesJson', JSON.stringify(documents));
        lock(true);
        status.textContent = corrections ? 'Validating current data and creating a correction workbook…' : exporting ? 'Creating cleaned Excel workbook…' : importing ? 'Importing reviewed records…' : 'Validating corrected records and comparing stored master data…';
        try {
            const response = await fetch(corrections ? workspace.dataset.correctionsUrl : exporting ? workspace.dataset.exportUrl : form.action, { method: 'POST', body: data, headers: { Accept: exporting ? 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' : 'application/json' } });
            if (exporting) {
                if (!response.ok || !response.headers.get('content-type')?.includes('spreadsheetml')) throw new Error('Export was not completed. Validate current records again before retrying.');
                const url = URL.createObjectURL(await response.blob());
                const link = document.createElement('a'); link.href = url; link.download = corrections ? 'MSAP-Master-File-Corrections.xlsx' : 'MSAP-Master-Files-Cleaned.xlsx'; link.click();
                setTimeout(() => URL.revokeObjectURL(url), 1000);
                status.textContent = corrections ? 'Correction workbook downloaded. Staff can complete highlighted cells and return this workbook for fresh validation. No records were imported.' : 'Cleaned workbook downloaded. Export did not import or change database records.';
                return;
            }
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('The request did not complete. Check your session and validate again.');
            const result = await response.json();
            validatedOnce = true;
            const errors = document.getElementById('masterImportErrors'); errors.replaceChildren();
            // Record-level messages live beside their editable row, not in a duplicate error list.
            (result.errors || []).filter(message => !/, row \d+:/i.test(message)).forEach(message => {
                const notice = document.createElement('p'); notice.className = 'ui-notice ui-critical'; notice.textContent = message; errors.append(notice);
            });
            document.getElementById('ignoredMasterFiles').textContent = result.ignoredFiles?.length ? 'Ignored files: ' + result.ignoredFiles.join(', ') : '';
            if (result.documents?.length) {
                const selected = fileSelect.value;
                documents = result.documents;
                documents.forEach(doc => doc.rows.forEach(row => doc.columns.forEach(column => {
                    const key = cellKey(doc, row, column.key);
                    if (!originals.has(key)) originals.set(key, row.fields[column.key] || '');
                })));
                fileSelect.replaceChildren(...documents.map(doc => new Option(doc.fileName + ' (' + doc.rows.length + ')', doc.kind)));
                if (documents.some(doc => doc.kind === selected)) fileSelect.value = selected;
            }
            results = new Map((result.items || []).map(item => [item.fileName + '/' + item.row, item]));
            const counts = {};
            (result.items || []).forEach(item => { counts[item.action] = (counts[item.action] || 0) + 1; });
            document.getElementById('masterImportCounts').textContent = Object.entries(counts).map(([key, count]) => key + ': ' + count).join(' · ') || 'No supported rows loaded.';
            const selectedDocument = documents.find(doc => doc.kind === fileSelect.value);
            if (!selectedDocument?.rows.some(row => results.get(rowKey(selectedDocument, row))?.action === 'Invalid')) {
                const invalid = documents.find(doc => doc.rows.some(row => results.get(rowKey(doc, row))?.action === 'Invalid'));
                if (invalid) fileSelect.value = invalid.kind;
            }
            const port = document.getElementById('NewTugboatPortCode');
            (result.ports || []).forEach(item => {
                if (![...port.options].some(option => option.value === item.value)) port.add(new Option(item.text, item.value));
            });
            fingerprint.value = result.reviewedFingerprint || '';
            reviewed = !!fingerprint.value && !result.imported && !(result.errors || []).length;
            const rateLinks = document.getElementById('masterImportedRates');
            rateLinks.replaceChildren();
            (result.rateDrafts || []).forEach((draft, index) => {
                const link = document.createElement('a'); link.className = 'ui-button'; link.href = draft.url;
                link.textContent = index === 0 ? 'Review imported tariff draft' : 'Review imported BAF draft'; rateLinks.append(link);
            });
            status.textContent = result.imported ? result.rateDrafts?.length
                ? 'Master changes saved and Tariff/BAF drafts created. Review the linked drafts before rate approval. Validate again for a cleaned export.'
                : 'Master records imported. Validate again to compare saved data or export the cleaned workbook.'
                : reviewed ? 'Validation passed. Review the records, then import or export cleaned Excel.' : 'No records were saved. Correct the highlighted records or export issues for staff completion.';
            renderEditor();
        } catch (error) {
            invalidate();
            status.textContent = error.message + ' Edits remain here. Review stored records and validate before retrying.';
        } finally { lock(false); }
    }
    form.addEventListener('submit', event => { event.preventDefault(); request(event.submitter === importButton, false); });
    exportButton.addEventListener('click', () => request(false, true));
    correctionsButton.addEventListener('click', () => request(false, true, true));
})();
