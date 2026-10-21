/**
 * ModernAlert Utility
 * A custom UI wrapper for SweetAlert2 that replicates the user-provided "Success Notification" design.
 * Now using explicit CSS classes from modern-ui.css to avoid Tailwind dependencies.
 */

const ModernAlert = {
    // Helper to get the hero icon HTML
    _getHeroIcon: function(type) {
        const iconConfigs = {
            success: { icon: 'check_circle', color: 'icon-success', bg: 'icon-bg-success', pulse: 'pulse-success' },
            error: { icon: 'error', color: 'icon-error', bg: 'icon-bg-error', pulse: 'pulse-error' },
            warning: { icon: 'warning', color: 'icon-warning', bg: 'icon-bg-warning', pulse: 'pulse-warning' },
            info: { icon: 'info', color: 'icon-info', bg: 'icon-bg-info', pulse: 'pulse-info' },
            question: { icon: 'help', color: 'icon-question', bg: 'icon-bg-question', pulse: 'pulse-question' }
        };
        const config = iconConfigs[type] || iconConfigs.info;

        return `
            <div class="success-icon-container ${config.bg} mx-auto">
                <div class="absolute inset-0 rounded-full ${config.pulse} animate-ping opacity-75"></div>
                <span class="material-symbols-outlined ${config.color} icon-size-hero" style="font-variation-settings: 'FILL' 1;">${config.icon}</span>
            </div>
        `;
    },

    // Base configuration for the custom design
    _baseConfig: {
        customClass: {
            popup: 'modern-swal-popup',
            title: 'modern-swal-title',
            htmlContainer: 'modern-swal-html',
            confirmButton: 'modern-swal-confirm swal2-confirm-btn',
            cancelButton: 'modern-swal-cancel swal2-cancel-btn',
            actions: 'modern-swal-actions'
        },
        didOpen: function(popup) {
            $(popup).find('.swal2-confirm').attr('data-testid', 'swal-confirm-btn');
            $(popup).find('.swal2-cancel').attr('data-testid', 'swal-cancel-btn');
        },
        buttonsStyling: false,
        showCloseButton: false,
        allowOutsideClick: true,
        width: '384px',
        padding: '0',
        showClass: {
            popup: 'animate__animated animate__fadeInUp animate__faster'
        },
        hideClass: {
            popup: 'animate__animated animate__fadeOutDown animate__faster'
        }
    },

    /**
     * Show a success alert
     */
    success: function(message, title = 'Success') {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                ${this._getHeroIcon('success')}
                <p class="modern-value-md text-center max-w-[240px] mx-auto leading-relaxed mb-4" style="color: var(--on-surface-variant)">${message}</p>
                <div class="mt-6 flex items-center justify-center gap-2 opacity-40">
                    <div class="h-[1px] w-8" style="background-color: var(--outline-variant)"></div>
                    <span class="modern-label" style="font-size: 10px; margin-bottom: 0">System Verified</span>
                    <div class="h-[1px] w-8" style="background-color: var(--outline-variant)"></div>
                </div>
            `,
            confirmButtonText: 'OK'
        });
    },

    /**
     * Show an error alert
     */
    error: function(message, title = 'Error') {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                ${this._getHeroIcon('error')}
                <p class="modern-value-md text-center max-w-[240px] mx-auto leading-relaxed mb-4" style="color: var(--on-surface-variant)">${message}</p>
            `,
            confirmButtonText: 'OK'
        });
    },

    /**
     * Show a warning alert
     */
    warning: function(message, title = 'Warning') {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                ${this._getHeroIcon('warning')}
                <p class="modern-value-md text-center max-w-[240px] mx-auto leading-relaxed mb-4" style="color: var(--on-surface-variant)">${message}</p>
            `,
            confirmButtonText: 'OK'
        });
    },

    /**
     * Show an info alert
     */
    info: function(message, title = 'Info') {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                ${this._getHeroIcon('info')}
                <p class="modern-value-md text-center max-w-[240px] mx-auto leading-relaxed mb-4" style="color: var(--on-surface-variant)">${message}</p>
            `,
            confirmButtonText: 'OK',
            timer: 2000
        });
    },

    /**
     * Show a confirmation dialog
     */
    confirm: function({
        title = 'Confirmation',
        text = "Are you sure you want to proceed?",
        confirmText = 'OK',
        cancelText = 'Cancel',
        icon = 'question',
        didOpen,
        preConfirm
    } = {}) {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                ${this._getHeroIcon(icon)}
                <div class="modern-value-md text-center max-w-[240px] mx-auto leading-relaxed mb-4" style="color: var(--on-surface-variant)">${text}</div>
            `,
            showCancelButton: true,
            confirmButtonText: confirmText,
            cancelButtonText: cancelText,
            didOpen: popup => { this._baseConfig.didOpen?.(popup); didOpen?.(popup); },
            preConfirm
        });
    },

    /**
     * Show a simple alert
     */
    alert: function(title, text, icon = 'info') {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                ${this._getHeroIcon(icon)}
                <p class="modern-value-md text-center max-w-[240px] mx-auto leading-relaxed mb-4" style="color: var(--on-surface-variant)">${text}</p>
            `,
            confirmButtonText: 'OK'
        });
    },

    /**
     * Show a loading state
     */
    showLoading: function(title = 'Processing...') {
        Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                <div class="flex flex-col items-center py-4">
                    <div class="modern-loader-inline mb-4"></div>
                    <p class="modern-label">Please wait while we process your request.</p>
                </div>
            `,
            allowOutsideClick: false,
            showConfirmButton: false,
            didOpen: () => {
                Swal.showLoading();
            }
        });
    },

    /**
     * Show an image preview with zoom and pan controls
     */
    image: function(imageUrl, title = 'Image Preview') {
        const btnStyle = 'background: rgba(255,255,255,0.2); border: none; color: white; border-radius: 4px; width: 36px; height: 36px; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: background 0.2s; backdrop-filter: blur(4px);';

        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                <div class="modern-image-viewer-container" style="position: relative; overflow: hidden; height: 75vh; background: #000; border-radius: var(--radius-xl); margin-top: 10px;">
                    <div class="modern-image-viewer-toolbar" style="position: absolute; top: 15px; right: 15px; z-index: 100; display: flex; gap: 8px;">
                        <button class="zoom-in" style="${btnStyle}" title="Zoom In"><span class="material-symbols-outlined">zoom_in</span></button>
                        <button class="zoom-out" style="${btnStyle}" title="Zoom Out"><span class="material-symbols-outlined">zoom_out</span></button>
                        <button class="reset" style="${btnStyle}" title="Reset"><span class="material-symbols-outlined">restart_alt</span></button>
                        <button class="close-viewer" style="${btnStyle}; background: rgba(239, 68, 68, 0.4);" title="Close"><span class="material-symbols-outlined">close</span></button>
                    </div>
                    <div class="modern-image-viewer-wrapper" style="width: 100%; height: 100%; display: flex; align-items: center; justify-content: center; cursor: grab; user-select: none;">
                        <img src="${imageUrl}" class="modern-image-viewer-img" style="max-width: 95%; max-height: 95%; transition: transform 0.15s ease-out; pointer-events: none;" />
                    </div>
                    <div style="position: absolute; bottom: 15px; left: 50%; transform: translateX(-50%); color: rgba(255,255,255,0.6); font-size: 11px; pointer-events: none; text-transform: uppercase; letter-spacing: 0.05em;">
                        Scroll to Zoom • Drag to Pan • Close to Exit
                    </div>
                </div>
            `,
            showConfirmButton: false,
            showCloseButton: true,
            width: '90%',
            padding: '0',
            background: 'var(--surface-container-lowest)',
            didOpen: () => {
                const img = document.querySelector('.modern-image-viewer-img');
                const wrapper = document.querySelector('.modern-image-viewer-wrapper');
                const container = document.querySelector('.modern-image-viewer-container');
                let scale = 1;
                let isDragging = false;
                let startX, startY, translateX = 0, translateY = 0;

                const updateTransform = () => {
                    img.style.transform = `translate(${translateX}px, ${translateY}px) scale(${scale})`;
                };

                // Zoom controls
                document.querySelector('.zoom-in').onclick = (e) => { e.stopPropagation(); scale += 0.3; updateTransform(); };
                document.querySelector('.zoom-out').onclick = (e) => { e.stopPropagation(); if (scale > 0.4) scale -= 0.3; updateTransform(); };
                document.querySelector('.reset').onclick = (e) => { e.stopPropagation(); scale = 1; translateX = 0; translateY = 0; updateTransform(); };
                document.querySelector('.close-viewer').onclick = (e) => { e.stopPropagation(); Swal.close(); };

                // Hover effects for custom buttons
                container.querySelectorAll('button').forEach(btn => {
                    if (btn.classList.contains('close-viewer')) {
                        btn.onmouseenter = () => btn.style.background = 'rgba(239, 68, 68, 0.6)';
                        btn.onmouseleave = () => btn.style.background = 'rgba(239, 68, 68, 0.4)';
                    } else {
                        btn.onmouseenter = () => btn.style.background = 'rgba(255,255,255,0.4)';
                        btn.onmouseleave = () => btn.style.background = 'rgba(255,255,255,0.2)';
                    }
                });

                // Pan logic
                wrapper.onmousedown = (e) => {
                    if (e.button !== 0) return; // Only left click
                    isDragging = true;
                    startX = e.clientX - translateX;
                    startY = e.clientY - translateY;
                    wrapper.style.cursor = 'grabbing';
                };

                const onMouseMove = (e) => {
                    if (!isDragging) return;
                    translateX = e.clientX - startX;
                    translateY = e.clientY - startY;
                    updateTransform();
                };

                const onMouseUp = () => {
                    isDragging = false;
                    wrapper.style.cursor = 'grab';
                };

                window.addEventListener('mousemove', onMouseMove);
                window.addEventListener('mouseup', onMouseUp);

                // Wheel zoom
                wrapper.onwheel = (e) => {
                    e.preventDefault();
                    const delta = e.deltaY > 0 ? -0.15 : 0.15;
                    const newScale = Math.min(Math.max(0.3, scale + delta), 8);

                    scale = newScale;
                    updateTransform();
                };

                // Clean up event listeners when Swal is closed
                Swal.getPopup().addEventListener('remove', () => {
                    window.removeEventListener('mousemove', onMouseMove);
                    window.removeEventListener('mouseup', onMouseUp);
                });
            }
        });
    },

    /**
     * Show a video preview
     */
    video: function(videoUrl, title = 'Video Preview') {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                <div class="p-2">
                    <video width="100%" height="auto" controls autoplay class="rounded-xl shadow-lg">
                        <source src="${videoUrl}" type="video/mp4">
                        Your browser does not support the video tag.
                    </video>
                </div>
            `,
            showConfirmButton: false,
            showCloseButton: true,
            background: 'var(--surface-container-lowest)',
            width: '500px',
            padding: '10px'
        });
    },

    /**
     * Show a prompt (input) dialog
     */
    prompt: function({
        title = 'Input Required',
        text = 'Please enter a value:',
        placeholder = '',
        confirmText = 'Submit',
        cancelText = 'Cancel',
        icon = 'question',
        validator = (value) => value ? null : 'Input is required'
    } = {}) {
        return Swal.fire({
            ...this._baseConfig,
            title: title,
            html: `
                ${this._getHeroIcon(icon)}
                <div class="modern-value-md text-center max-w-[240px] mx-auto leading-relaxed mb-4" style="color: var(--on-surface-variant)">${text}</div>
            `,
            input: 'text',
            inputPlaceholder: placeholder,
            inputAttributes: {
                class: 'form-control mx-auto w-full mb-4',
                style: 'max-width: 300px; border: 1px solid var(--outline-variant); border-radius: var(--radius-xl); padding: 12px 16px;'
            },
            showCancelButton: true,
            confirmButtonText: confirmText,
            cancelButtonText: cancelText,
            inputValidator: validator
        });
    },

    /**
     * Hide current SweetAlert
     */
    close: function() {
        Swal.close();
    }
};

/**
 * Global Compatibility Layer
 */
window.toast = {
    success: (msg) => ModernAlert.success(msg),
    error: (msg) => ModernAlert.error(msg),
    warning: (msg) => ModernAlert.warning(msg),
    info: (msg) => ModernAlert.info(msg)
};
window.toastr = window.toast;
window.swal = (title, text, icon) => (typeof title === 'object') ? Swal.fire(title) : ModernAlert.alert(title, text, icon);
window.swalConfirm = (title, text, callback) => {
    ModernAlert.confirm({ title, text }).then(result => {
        if (result.isConfirmed && typeof callback === 'function') callback();
    });
};
window.modernAlert = ModernAlert;
