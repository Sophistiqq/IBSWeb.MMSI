const initializeCosTicketEntry = () => {
    document.querySelectorAll('[data-cancel-service]').forEach(button => button.addEventListener('click', async () => {
        if (button.disabled) return;
        button.disabled = true;
        try {
            const result = await ModernAlert.prompt({
                title: 'Cancel unperformed service?',
                text: 'Use this only when the service did not happen. Its planned row and your reason will be retained.',
                placeholder: 'Reason for cancellation', confirmText: 'Cancel service', cancelText: 'Keep service',
                validator: value => !value?.trim() || value.trim().length > 500 ? 'Enter a reason of up to 500 characters.' : null
            });
            if (!result.isConfirmed) return;
            const body = new FormData();
            body.set('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
            body.set('id', button.dataset.cosId); body.set('entryId', button.dataset.cancelService); body.set('reason', result.value.trim());
            const response = await fetch(button.dataset.url, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Cancellation failed');
            const saved = await response.json();
            if (!saved.success) { await ModernAlert.error(saved.message || 'The service could not be cancelled.'); return; }
            await ModernAlert.success(saved.message);
            window.location.reload();
        } catch {
            await ModernAlert.error('Unable to cancel the service. Refresh the COS and try again.');
        } finally { button.disabled = false; }
    }));
    document.querySelectorAll('[data-void-ticket]').forEach(button => button.addEventListener('click', async () => {
        if (button.disabled) return;
        button.disabled = true;
        try {
            const isFinal = button.dataset.finalized === 'true';
            const dispatchNo = button.dataset.dispatchNumber || '';
            const text = isFinal
                ? 'Voiding this ticket removes it from billing calculations and tags this finalized-period record as Adjusted in the audit history.'
                : 'Voiding this ticket removes it from billing calculations and retains the voided row and your reason.';
            const result = await ModernAlert.promptAdjustment({
                title: dispatchNo ? `Void ticket #${dispatchNo}?` : 'Void ticket?',
                text,
                placeholder: 'Reason for voiding',
                confirmText: 'Void ticket',
                cancelText: 'Keep ticket',
                requireAttachment: isFinal
            });
            if (!result.isConfirmed) return;
            const body = new FormData();
            body.set('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
            body.set('id', button.dataset.voidTicket);
            body.set('customerOrderSlipId', button.dataset.cosId);
            body.set('reason', result.value.reason);
            if (result.value.file) {
                body.set('authorizationFile', result.value.file);
            }
            const response = await fetch(button.dataset.url, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Void failed');
            const saved = await response.json();
            if (!saved.success) { await ModernAlert.error(saved.message || 'The ticket could not be voided.'); return; }
            await ModernAlert.success(saved.message);
            window.location.href = button.dataset.cosUrl || `/MSAP/COS/Details/${encodeURIComponent(button.dataset.cosId)}`;
        } catch {
            await ModernAlert.error('Unable to void the ticket. Refresh the COS and try again.');
        } finally { button.disabled = false; }
    }));
    const ticketPanel = document.getElementById('actualTicketEntry');
    const ticketFormEl = document.getElementById('cosActualTicketForm') || document.getElementById('cosActualTicketDetails');
    if (ticketPanel && ticketFormEl) {
        initializeTicketFocusAndViewer(ticketPanel, ticketFormEl);
    }

    const form = document.getElementById('cosActualTicketForm');
    if (!form) return;

    const actual = document.getElementById('cosActualDuration');
    const billable = document.getElementById('cosBillableDuration');
    const times = () => {
        const startDate = form.elements.DateLeft.value;
        const endDate = form.elements.DateArrived.value;
        const startTime = form.elements.TimeLeft.value;
        const endTime = form.elements.TimeArrived.value;
        if (!startDate || !endDate || !startTime || !endTime) return null;
        return {
            start: Date.parse(`${startDate}T${startTime}+08:00`),
            end: Date.parse(`${endDate}T${endTime}+08:00`)
        };
    };
    const timeError = value => {
        if (!value) return 'Enter complete actual start and end dates/times.';
        if (!Number.isFinite(value.start) || !Number.isFinite(value.end)) return 'Enter valid actual start and end dates/times.';
        if (value.end <= value.start) return 'Actual end must be after actual start.';
        if (value.end > Date.now()) return 'Actual end is in the future. Enter a completed service time in Philippine time (UTC+8).';
        return null;
    };
    const preview = () => {
        const value = times();
        const error = timeError(value);
        if (error) {
            actual.textContent = error;
            billable.textContent = 'Enter valid actual times';
            return;
        }
        const minutes = (value.end - value.start) / 60000;
        actual.textContent = `${minutes.toFixed(0)} min · ${(minutes / 60).toFixed(2)} hours`;
        billable.textContent = `${Math.max(minutes / 60, 1).toFixed(2)} hours`;
    };
    ['DateLeft', 'TimeLeft', 'DateArrived', 'TimeArrived'].forEach(name => form.elements[name].addEventListener('input', preview));

    if (form.dataset.isNewTicket === 'true') {
        const ticketDateInput = form.elements.Date;
        const dateLeftInput = form.elements.DateLeft;
        const dateArrivedInput = form.elements.DateArrived;
        if (ticketDateInput && dateLeftInput && dateArrivedInput) {
            const hadDistinctPlannedStart = Boolean(dateLeftInput.value && ticketDateInput.value && dateLeftInput.value !== ticketDateInput.value);
            let dateLeftTouched = hadDistinctPlannedStart;
            let dateArrivedTouched = Boolean(dateArrivedInput.value && dateLeftInput.value && dateArrivedInput.value !== dateLeftInput.value);

            ticketDateInput.addEventListener('change', () => {
                if (!ticketDateInput.value) return;
                if (!dateLeftTouched) {
                    dateLeftInput.value = ticketDateInput.value;
                }
                if (!dateArrivedTouched) {
                    dateArrivedInput.value = dateLeftInput.value || ticketDateInput.value;
                }
                preview();
            });

            const onDateLeftChange = () => {
                dateLeftTouched = true;
                if (!dateLeftInput.value) return;
                if (!dateArrivedTouched || (dateArrivedInput.value && dateArrivedInput.value < dateLeftInput.value)) {
                    dateArrivedInput.value = dateLeftInput.value;
                }
                preview();
            };
            dateLeftInput.addEventListener('input', onDateLeftChange);
            dateLeftInput.addEventListener('change', onDateLeftChange);

            const onDateArrivedChange = () => {
                dateArrivedTouched = true;
            };
            dateArrivedInput.addEventListener('input', onDateArrivedChange);
            dateArrivedInput.addEventListener('change', onDateArrivedChange);
        }
    }

    form.noValidate = true;
    let submitting = false;
    form.addEventListener('submit', async event => {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (submitting) return;
        let message = timeError(times());
        if (!message && !form.elements.TugMasterId.value) message = 'Select the captain / tug master for this service.';
        if (!message && form.dataset.imageRequired === 'true' && !form.elements.imageFile?.files?.length && !form._fallbackImageFile) {
            message = 'Attach a photo of the dispatch ticket before saving.';
        }
        if (!message && form.elements.Tons) {
            const tons = Number.parseFloat(form.elements.Tons.value);
            if (!Number.isFinite(tons) || tons <= 0) message = 'Enter the watering tons (greater than 0).';
        }
        if (!message && form.elements.DispatchRate) {
            const rate = Number.parseFloat(form.elements.DispatchRate.value);
            if (!Number.isFinite(rate) || rate <= 0) message = 'Enter a watering rate greater than 0.';
        }
        preview();
        if (message || !form.reportValidity()) {
            if (message) ModernAlert.error(message);
            return;
        }
        submitting = true;
        const button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        const editing = Boolean(form.elements.DispatchTicketId?.value);
        try {
            const confirmation = await ModernAlert.confirm({
                title: editing ? 'Save ticket changes?' : 'Save actual ticket?',
                text: 'Save the ticket details and supporting files for this service.',
                icon: 'question',
                confirmText: editing ? 'Save changes' : 'Save ticket',
                cancelText: 'Keep editing'
            });
            if (!confirmation.isConfirmed) return;
            ModernAlert.showLoading(editing ? 'Saving ticket changes…' : 'Saving actual ticket…');
            const payload = new FormData(form);
            if (!form.elements.imageFile?.files?.length && form._fallbackImageFile) {
                payload.set('imageFile', form._fallbackImageFile);
            }
            const response = await fetch(form.action, {
                method: 'POST',
                body: payload,
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok || response.redirected) throw new Error('Save failed');
            const result = await response.json();
            if (!result.success || !result.redirectUrl) {
                await ModernAlert.error(result.message || 'Unable to save the ticket. Please try again.');
                return;
            }
            await ModernAlert.success(result.message || 'Ticket saved successfully.');
            window.location.href = result.redirectUrl;
        } catch {
            ModernAlert.error('Unable to save the ticket. Check your connection and access, then try again.');
        } finally {
            submitting = false;
            button.disabled = false;
        }
    }, true);
    preview();
};

const initializeTicketFocusAndViewer = (panel, formEl) => {
    const backdrop = document.getElementById('cosTicketBackdrop');
    const workspace = document.getElementById('cosEntryWorkspace');
    const entryMain = document.getElementById('cosEntryMain');
    const dropzone = document.getElementById('cosPhotoDropzone');
    const fileInput = document.getElementById('cosTicketImage');
    const cameraInput = document.getElementById('cosTicketCamera');
    const previewImg = document.getElementById('cosTicketImagePreview');
    const emptyState = document.getElementById('cosTicketImageEmpty');
    const statusLabel = document.getElementById('cosTicketImageStatus');
    const fileNameLabel = document.getElementById('cosTicketImageFileName');
    const stage = document.getElementById('cosTicketImageStage');
    const ocrContainer = document.getElementById('cosTicketNumberOcr');
    const ocrStatus = document.getElementById('cosTicketNumberOcrStatus');
    const ocrChip = document.getElementById('cosTicketNumberOcrChip');
    const ocrValueEl = document.getElementById('cosTicketNumberOcrValue');
    const ticketNumberInput = formEl.elements?.DispatchNumber || null;

    const isNewTicket = formEl.dataset.isNewTicket === 'true';
    const isReadOnly = formEl.dataset.isReadonly === 'true';
    const initialUrl = previewImg?.dataset.initialUrl || '';
    const closeUrl = panel.dataset.closeUrl || '';

    let blobUrl = null;
    let activeUrl = initialUrl;
    let scale = 1;
    let rotation = 0;
    let panX = 0;
    let panY = 0;
    let ocrScanToken = 0;
    let suggestedTicketNumber = '';

    const hideOcrSuggestion = () => {
        suggestedTicketNumber = '';
        if (ocrStatus) {
            ocrStatus.textContent = '';
            ocrStatus.hidden = true;
        }
        if (ocrChip) ocrChip.hidden = true;
        if (ocrContainer) ocrContainer.hidden = true;
    };

    const showOcrScanning = () => {
        if (!ocrContainer || !ocrStatus || !ocrChip) return;
        suggestedTicketNumber = '';
        ocrChip.hidden = true;
        ocrStatus.textContent = 'Reading ticket # from photo…';
        ocrStatus.hidden = false;
        ocrContainer.hidden = false;
    };

    const showOcrSuggestion = value => {
        if (!ocrContainer || !ocrStatus || !ocrChip || !ocrValueEl) return;
        suggestedTicketNumber = value;
        ocrValueEl.textContent = value;
        ocrStatus.hidden = true;
        ocrChip.hidden = false;
        ocrContainer.hidden = false;
    };

    const extractTicketNumberFromOcr = rawText => {
        const cleaned = String(rawText || '').replace(/[|]/g, ' ').replace(/\r/g, '\n');
        const explicitPatterns = [
            /(?:\bcontrol\s*no\.?|\bticket\s*no\.?|\bno\.?|\bn[°ºo¢0?]\.?)\s*[:#-]?\s*(?:[A-Z]{2,6}\s+){0,2}([0-9]{2,4}-[0-9]{3,6}|[0-9]{3,8})\b/i,
            /\b([0-9]{2}-[0-9]{3,6})\b/
        ];
        for (const rx of explicitPatterns) {
            const match = cleaned.match(rx);
            if (match && match[1]) {
                const candidate = match[1].trim();
                if (!/^(19|20)\d{2}$/.test(candidate)) return candidate;
            }
        }
        const skipLine = /tel|fax|date|form|rev|issue|unit|residences|bldg|building|floor|st\.|street|manila|pasig|malate|ortigas|ermita|boulevard|ave|center|inc\.?|corp/i;
        const lines = cleaned.split('\n').map(l => l.trim()).filter(l => l && !skipLine.test(l));
        const candidates = [];
        for (const line of lines) {
            const nums = line.match(/\b\d{3,7}\b/g);
            if (!nums) continue;
            for (const n of nums) {
                if (/^(19|20)\d{2}$/.test(n)) continue;
                candidates.push(n);
            }
        }
        return candidates.find(n => n.length >= 4) || candidates[0] || null;
    };

    const loadTesseractWorker = () => {
        if (window.__msapTicketOcrWorkerPromise) return window.__msapTicketOcrWorkerPromise;
        window.__msapTicketOcrWorkerPromise = new Promise((resolve, reject) => {
            const initWorker = async () => {
                if (!window.Tesseract?.createWorker) {
                    reject(new Error('Tesseract unavailable'));
                    return;
                }
                const worker = await window.Tesseract.createWorker('eng');
                resolve(worker);
            };
            if (window.Tesseract?.createWorker) {
                initWorker().catch(reject);
                return;
            }
            const script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/tesseract.js@5/dist/tesseract.min.js';
            script.async = true;
            script.onload = () => initWorker().catch(reject);
            script.onerror = () => reject(new Error('Failed to load Tesseract'));
            document.head.appendChild(script);
        }).catch(err => {
            window.__msapTicketOcrWorkerPromise = null;
            throw err;
        });
        return window.__msapTicketOcrWorkerPromise;
    };

    const buildTopRightCropCanvas = (url, angleDeg) => new Promise((resolve, reject) => {
        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.onload = () => {
            const rad = ((angleDeg % 360) * Math.PI) / 180;
            const swap = angleDeg % 180 !== 0;
            const fullW = swap ? img.naturalHeight : img.naturalWidth;
            const fullH = swap ? img.naturalWidth : img.naturalHeight;
            if (!fullW || !fullH) {
                reject(new Error('Empty image'));
                return;
            }
            const maxDim = 1400;
            const downscale = Math.min(1, maxDim / Math.max(fullW, fullH));
            const scaledW = Math.max(1, Math.round(fullW * downscale));
            const scaledH = Math.max(1, Math.round(fullH * downscale));

            const fullCanvas = document.createElement('canvas');
            fullCanvas.width = scaledW;
            fullCanvas.height = scaledH;
            const fullCtx = fullCanvas.getContext('2d');
            if (!fullCtx) {
                reject(new Error('Canvas unavailable'));
                return;
            }
            fullCtx.translate(scaledW / 2, scaledH / 2);
            fullCtx.rotate(rad);
            fullCtx.drawImage(
                img,
                -(img.naturalWidth * downscale) / 2,
                -(img.naturalHeight * downscale) / 2,
                img.naturalWidth * downscale,
                img.naturalHeight * downscale
            );

            const cropX = Math.floor(scaledW * 0.48);
            const cropY = 0;
            const cropW = Math.max(1, scaledW - cropX);
            const cropH = Math.max(1, Math.floor(scaledH * 0.30));

            const cropCanvas = document.createElement('canvas');
            cropCanvas.width = cropW;
            cropCanvas.height = cropH;
            const cropCtx = cropCanvas.getContext('2d');
            if (!cropCtx) {
                reject(new Error('Canvas unavailable'));
                return;
            }
            cropCtx.drawImage(fullCanvas, cropX, cropY, cropW, cropH, 0, 0, cropW, cropH);
            resolve(cropCanvas);
        };
        img.onerror = () => reject(new Error('Image load failed'));
        img.src = url;
    });

    const scanTicketNumberFromPhoto = async (url, angleDeg = 0) => {
        if (isReadOnly || !ticketNumberInput || !ocrContainer) return;
        if (!url || ticketNumberInput.value.trim()) {
            hideOcrSuggestion();
            return;
        }
        const token = ++ocrScanToken;
        showOcrScanning();
        try {
            const [cropCanvas, worker] = await Promise.all([
                buildTopRightCropCanvas(url, angleDeg),
                loadTesseractWorker()
            ]);
            if (token !== ocrScanToken || ticketNumberInput.value.trim()) return;
            const { data } = await worker.recognize(cropCanvas);
            if (token !== ocrScanToken || ticketNumberInput.value.trim()) return;
            const detected = extractTicketNumberFromOcr(data?.text || '');
            if (detected) {
                showOcrSuggestion(detected.slice(0, 20));
            } else {
                hideOcrSuggestion();
            }
        } catch {
            if (token === ocrScanToken) hideOcrSuggestion();
        }
    };

    if (ocrChip && ticketNumberInput) {
        ocrChip.addEventListener('click', () => {
            if (!suggestedTicketNumber) return;
            ticketNumberInput.value = suggestedTicketNumber;
            ticketNumberInput.dispatchEvent(new Event('input', { bubbles: true }));
            ticketNumberInput.dispatchEvent(new Event('change', { bubbles: true }));
            hideOcrSuggestion();
            ticketNumberInput.focus();
        });
    }

    if (ticketNumberInput) {
        ticketNumberInput.addEventListener('input', () => {
            if (ticketNumberInput.value.trim()) {
                ocrScanToken++;
                hideOcrSuggestion();
            }
        });
    }

    const closeModernSelects = () => {
        document.querySelectorAll('.modern-select-dropdown.show').forEach(el => el.classList.remove('show'));
        document.querySelectorAll('.modern-select-trigger.active').forEach(el => el.classList.remove('active'));
    };

    const applyTransform = () => {
        if (!previewImg) return;
        previewImg.style.transform = `translate(${panX}px, ${panY}px) scale(${scale}) rotate(${rotation}deg)`;
        if (stage) {
            stage.classList.toggle('is-zoomed', scale > 1);
        }
    };

    const resetTransform = () => {
        scale = 1;
        rotation = 0;
        panX = 0;
        panY = 0;
        applyTransform();
    };

    const setImageSource = (url, label) => {
        activeUrl = url || '';
        const hasImg = Boolean(activeUrl);
        if (workspace) {
            workspace.dataset.hasImage = hasImg ? 'true' : 'false';
        }
        if (previewImg) {
            if (hasImg) {
                previewImg.src = activeUrl;
            } else {
                previewImg.removeAttribute('src');
            }
            previewImg.hidden = !hasImg;
        }
        if (emptyState) {
            emptyState.hidden = hasImg;
        }
        if (statusLabel) {
            statusLabel.textContent = label || (hasImg ? 'Ticket photo' : 'No photo');
        }
        resetTransform();
    };

    const setStep = (step, { focusTicketNumber = false } = {}) => {
        panel.dataset.ticketStep = step;
        if (workspace) {
            const isUploadStep = step === 'upload';
            workspace.inert = isUploadStep;
            if (isUploadStep) {
                workspace.setAttribute('aria-hidden', 'true');
            } else {
                workspace.removeAttribute('aria-hidden');
            }
        }
        if (focusTicketNumber && !isReadOnly) {
            const ticketInput = formEl.elements.DispatchNumber;
            if (ticketInput) {
                setTimeout(() => {
                    try { ticketInput.focus(); } catch { /* ignore focus errors */ }
                }, 180);
            }
        }
    };

    const closeModal = () => {
        if (!closeUrl) return;
        window.location.href = closeUrl;
    };

    const applySelectedFile = file => {
        if (blobUrl) {
            URL.revokeObjectURL(blobUrl);
            blobUrl = null;
        }
        if (file) {
            blobUrl = URL.createObjectURL(file);
            setImageSource(blobUrl, 'Selected photo (unsaved)');
            if (fileNameLabel) fileNameLabel.textContent = file.name || 'Captured photo';
            if (workspace) {
                workspace.dataset.imageCollapsed = 'false';
                const toggleBtn = panel.querySelector('[data-image-toggle]');
                if (toggleBtn) {
                    toggleBtn.setAttribute('aria-expanded', 'true');
                    toggleBtn.title = 'Hide photo';
                    const icon = toggleBtn.querySelector('.material-symbols-outlined');
                    if (icon) icon.textContent = 'expand_less';
                }
            }
            if (panel.dataset.ticketStep === 'upload') {
                setStep('split', { focusTicketNumber: true });
            }
            scanTicketNumberFromPhoto(blobUrl, 0);
        } else if (initialUrl) {
            setImageSource(initialUrl, 'Saved photo');
            if (fileNameLabel) fileNameLabel.textContent = 'Saved photo';
            hideOcrSuggestion();
        } else {
            setImageSource('', 'No photo');
            if (fileNameLabel) fileNameLabel.textContent = 'No file selected';
            hideOcrSuggestion();
        }
    };

    const assignFileToInput = file => {
        if (!fileInput || !file) return false;
        if (!file.type || !file.type.startsWith('image/')) {
            ModernAlert.warning('Please choose an image file for the dispatch ticket photo.');
            return false;
        }
        try {
            const dt = new DataTransfer();
            dt.items.add(file);
            fileInput.files = dt.files;
            formEl._fallbackImageFile = null;
            fileInput.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        } catch {
            formEl._fallbackImageFile = file;
            applySelectedFile(file);
            return true;
        }
    };

    const handleFileSelected = () => {
        if (!fileInput) return;
        const file = fileInput.files && fileInput.files[0];
        if (file && (!file.type || !file.type.startsWith('image/'))) {
            fileInput.value = '';
            formEl._fallbackImageFile = null;
            applySelectedFile(null);
            ModernAlert.warning('Please select an image file for the dispatch ticket photo.');
            return;
        }
        if (file) {
            formEl._fallbackImageFile = null;
        }
        applySelectedFile(file || formEl._fallbackImageFile || null);
    };

    if (fileInput) {
        fileInput.addEventListener('change', handleFileSelected);
    }

    if (cameraInput) {
        cameraInput.addEventListener('change', () => {
            const captured = cameraInput.files && cameraInput.files[0];
            if (!captured) return;
            assignFileToInput(captured);
            cameraInput.value = '';
        });
    }

    panel.querySelectorAll('[data-trigger-photo-upload]').forEach(btn => {
        btn.addEventListener('click', event => {
            event.stopPropagation();
            if (fileInput) fileInput.click();
        });
    });

    panel.querySelectorAll('[data-trigger-camera-upload]').forEach(btn => {
        btn.addEventListener('click', event => {
            event.stopPropagation();
            if (cameraInput) {
                cameraInput.click();
            } else if (fileInput) {
                fileInput.click();
            }
        });
    });

    panel.querySelectorAll('[data-skip-photo]').forEach(btn => {
        btn.addEventListener('click', event => {
            event.stopPropagation();
            setStep('split', { focusTicketNumber: true });
        });
    });

    if (dropzone) {
        dropzone.addEventListener('click', event => {
            if (event.target.closest('button')) return;
            if (fileInput) fileInput.click();
        });
    }

    const bindDropTarget = target => {
        if (!target || isReadOnly) return;
        target.addEventListener('dragover', event => {
            event.preventDefault();
            target.classList.add('is-dragover');
        });
        target.addEventListener('dragleave', () => {
            target.classList.remove('is-dragover');
        });
        target.addEventListener('drop', event => {
            event.preventDefault();
            target.classList.remove('is-dragover');
            const dropped = event.dataTransfer?.files?.[0];
            if (dropped) assignFileToInput(dropped);
        });
    };
    bindDropTarget(dropzone);
    bindDropTarget(stage);

    if (!isReadOnly) {
        document.addEventListener('paste', event => {
            const items = event.clipboardData?.items;
            if (!items) return;
            for (const item of items) {
                if (item.type && item.type.startsWith('image/')) {
                    const pastedFile = item.getAsFile();
                    if (pastedFile) {
                        event.preventDefault();
                        assignFileToInput(pastedFile);
                    }
                    break;
                }
            }
        });
    }

    // Close on backdrop click or Escape when viewing or on the upload gate
    if (backdrop) {
        backdrop.addEventListener('click', () => {
            if (isReadOnly || panel.dataset.ticketStep === 'upload') {
                closeModal();
            }
        });
    }

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        if (document.querySelector('.swal2-container.swal2-shown')) return;
        if (document.querySelector('.modern-select-dropdown.show')) return;
        if (isReadOnly || panel.dataset.ticketStep === 'upload') {
            closeModal();
        }
    });

    // Keep ModernSelect dropdowns aligned if the lifted form scrolls
    if (entryMain) entryMain.addEventListener('scroll', closeModernSelects, { passive: true });
    formEl.addEventListener('scroll', closeModernSelects, { passive: true });

    // Image toolbar controls
    const rotateBtn = panel.querySelector('[data-image-rotate]');
    const resetBtn = panel.querySelector('[data-image-reset]');
    const fullscreenBtn = panel.querySelector('[data-image-fullscreen]');
    const collapseBtn = panel.querySelector('[data-image-toggle]');

    if (rotateBtn) {
        rotateBtn.addEventListener('click', () => {
            if (!activeUrl) return;
            rotation = (rotation + 90) % 360;
            applyTransform();
            if (!isReadOnly && ticketNumberInput && !ticketNumberInput.value.trim()) {
                scanTicketNumberFromPhoto(activeUrl, rotation);
            }
        });
    }
    if (resetBtn) {
        resetBtn.addEventListener('click', () => resetTransform());
    }
    if (fullscreenBtn) {
        fullscreenBtn.addEventListener('click', () => {
            if (!activeUrl) return;
            ModernAlert.image(activeUrl, 'Dispatch Ticket Image Preview', { rotation });
        });
    }
    if (collapseBtn && workspace) {
        collapseBtn.addEventListener('click', () => {
            const collapsed = workspace.dataset.imageCollapsed === 'true';
            const next = !collapsed;
            workspace.dataset.imageCollapsed = next ? 'true' : 'false';
            collapseBtn.setAttribute('aria-expanded', next ? 'false' : 'true');
            collapseBtn.title = next ? 'Show photo' : 'Hide photo';
            collapseBtn.setAttribute('aria-label', next ? 'Show photo' : 'Hide photo');
            const icon = collapseBtn.querySelector('.material-symbols-outlined');
            if (icon) icon.textContent = next ? 'expand_more' : 'expand_less';
        });
    }

    // Multi-touch pinch-to-zoom, pointer pan, wheel zoom, and double-tap/double-click zoom on stage
    if (stage) {
        const activePointers = new Map();
        let dragging = false;
        let pinching = false;
        let dragStartX = 0;
        let dragStartY = 0;
        let downX = 0;
        let downY = 0;
        let movedDuringTap = false;
        let pinchStartDist = 0;
        let pinchStartScale = 1;
        let pinchStartMidX = 0;
        let pinchStartMidY = 0;
        let pinchStartPanX = 0;
        let pinchStartPanY = 0;
        let lastTapTime = 0;
        let lastTapX = 0;
        let lastTapY = 0;

        const getPinchMetrics = () => {
            const pts = Array.from(activePointers.values());
            const dx = pts[1].x - pts[0].x;
            const dy = pts[1].y - pts[0].y;
            return {
                dist: Math.hypot(dx, dy) || 1,
                midX: (pts[0].x + pts[1].x) / 2,
                midY: (pts[0].y + pts[1].y) / 2
            };
        };

        const toggleDoubleZoom = () => {
            if (!activeUrl) return;
            if (scale > 1) {
                scale = 1;
                panX = 0;
                panY = 0;
            } else {
                scale = 2.25;
            }
            applyTransform();
        };

        stage.addEventListener('pointerdown', event => {
            if (!activeUrl) return;
            if (event.pointerType === 'mouse' && event.button !== 0) return;
            activePointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            try { stage.setPointerCapture(event.pointerId); } catch { /* ignore */ }

            if (activePointers.size === 2) {
                pinching = true;
                dragging = false;
                movedDuringTap = true;
                const metrics = getPinchMetrics();
                pinchStartDist = metrics.dist;
                pinchStartScale = scale;
                pinchStartMidX = metrics.midX;
                pinchStartMidY = metrics.midY;
                pinchStartPanX = panX;
                pinchStartPanY = panY;
                stage.classList.add('is-dragging');
            } else if (activePointers.size === 1) {
                downX = event.clientX;
                downY = event.clientY;
                movedDuringTap = false;
                if (scale > 1) {
                    dragging = true;
                    dragStartX = event.clientX - panX;
                    dragStartY = event.clientY - panY;
                    stage.classList.add('is-dragging');
                }
            }
        });

        stage.addEventListener('pointermove', event => {
            if (!activePointers.has(event.pointerId)) return;
            activePointers.set(event.pointerId, { x: event.clientX, y: event.clientY });

            if (pinching && activePointers.size >= 2) {
                const metrics = getPinchMetrics();
                scale = Math.min(4, Math.max(0.5, +(pinchStartScale * (metrics.dist / pinchStartDist)).toFixed(3)));
                if (scale <= 1) {
                    panX = 0;
                    panY = 0;
                } else {
                    panX = pinchStartPanX + (metrics.midX - pinchStartMidX);
                    panY = pinchStartPanY + (metrics.midY - pinchStartMidY);
                }
                applyTransform();
                return;
            }

            if (Math.hypot(event.clientX - downX, event.clientY - downY) > 10) {
                movedDuringTap = true;
            }

            if (dragging && activePointers.size === 1) {
                panX = event.clientX - dragStartX;
                panY = event.clientY - dragStartY;
                applyTransform();
            }
        });

        const stopPointer = event => {
            if (!activePointers.has(event.pointerId)) return;
            const wasSingleTouchTap = event.pointerType === 'touch'
                && activePointers.size === 1
                && !pinching
                && !movedDuringTap;

            activePointers.delete(event.pointerId);
            try { stage.releasePointerCapture(event.pointerId); } catch { /* ignore */ }

            if (activePointers.size < 2) {
                pinching = false;
            }

            if (activePointers.size === 1 && scale > 1) {
                const remaining = Array.from(activePointers.values())[0];
                dragging = true;
                dragStartX = remaining.x - panX;
                dragStartY = remaining.y - panY;
            } else if (activePointers.size === 0) {
                dragging = false;
                stage.classList.remove('is-dragging');
                if (scale <= 1) {
                    panX = 0;
                    panY = 0;
                    applyTransform();
                }
                if (wasSingleTouchTap) {
                    const now = Date.now();
                    if (now - lastTapTime < 300 && Math.hypot(event.clientX - lastTapX, event.clientY - lastTapY) < 32) {
                        lastTapTime = 0;
                        toggleDoubleZoom();
                    } else {
                        lastTapTime = now;
                        lastTapX = event.clientX;
                        lastTapY = event.clientY;
                    }
                }
            }
        };

        stage.addEventListener('pointerup', stopPointer);
        stage.addEventListener('pointercancel', stopPointer);

        stage.addEventListener('wheel', event => {
            if (!activeUrl) return;
            event.preventDefault();
            const delta = event.deltaY > 0 ? -0.15 : 0.15;
            scale = Math.min(4, Math.max(0.5, +(scale + delta).toFixed(2)));
            if (scale <= 1) {
                panX = 0;
                panY = 0;
            }
            applyTransform();
        }, { passive: false });

        stage.addEventListener('dblclick', () => toggleDoubleZoom());
    }

    window.addEventListener('beforeunload', () => {
        if (blobUrl) {
            URL.revokeObjectURL(blobUrl);
            blobUrl = null;
        }
    });

    // Lock body scroll and initialize modal step
    document.body.classList.add('cos-ticket-focus-open');
    const initialStep = isNewTicket && !activeUrl ? 'upload' : 'split';
    setStep(initialStep);
};

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeCosTicketEntry);
} else {
    initializeCosTicketEntry();
}
