/**
 * ModernSelect Component
 * High-performance, searchable replacement for standard HTML selects.
 * Features: Fixed positioning, auto-flipping, and mutation observers.
 */
const ModernSelect = {
    init: function(selector) {
        $(selector).each(function() {
            ModernSelect.create($(this));
        });
    },

    create: function($select) {
        if ($select.data('modern-select-initialized')) return;

        const placeholder = $select.data('placeholder') || 'Select an option...';
        const isSearchable = $select.data('searchable') !== false;

        // Hide original select
        $select.hide();

        // Create modern select structure
        const selectId = $select.attr('id') || $select.attr('name') || '';
        const testId = $select.attr('data-testid') || (selectId ? `select-${selectId}` : '');
        const triggerTestId = testId ? `${testId}-trigger` : '';

        const $container = $('<div class="modern-select-container"></div>');
        const $trigger = $(`
            <div class="modern-select-trigger" tabindex="0">
                <span class="selected-text"></span>
                <span class="material-symbols-outlined">expand_more</span>
            </div>
        `);

        $trigger.find('.selected-text').text(placeholder);
        if (triggerTestId) {
            $trigger.attr('data-testid', triggerTestId);
        }

        const $dropdown = $('<div class="modern-select-dropdown"></div>');

        // Keep modal dropdowns inside the popup's focus boundary.
        const $popup = $select.closest('.swal2-popup');
        ($popup.length ? $popup : $('body')).append($dropdown);

        if (isSearchable) {
            const $search = $(`
                <div class="modern-select-search">
                    <span class="material-symbols-outlined">search</span>
                    <input type="text" placeholder="Search...">
                </div>
            `);
            $dropdown.append($search);
        }

        const $optionsContainer = $('<div class="modern-select-options"></div>');
        const $noResults = $('<div class="modern-select-no-results">No results found</div>');

        $dropdown.append($optionsContainer).append($noResults);
        $container.append($trigger);
        $select.after($container);

        const $validation = $('<span class="text-danger" role="alert" hidden></span>');
        $container.after($validation);
        $select.on('invalid', function(e) {
            e.preventDefault();
            $trigger.addClass('is-invalid').attr('aria-invalid', 'true');
            $validation.text(this.validationMessage).prop('hidden', false);
            $trigger[0].focus();
        });

        const syncDisabled = () => {
            const disabled = $select.is(':disabled');
            $trigger.toggleClass('disabled', disabled).attr('aria-disabled', String(disabled)).attr('tabindex', disabled ? '-1' : '0');
            if (disabled) {
                $dropdown.removeClass('show');
                $trigger.removeClass('active');
            }
        };
        syncDisabled();

        // Initial population
        ModernSelect.populate($select, $optionsContainer, $trigger, placeholder);

        // Helper to position dropdown
        const positionDropdown = () => {
            const rect = $trigger[0].getBoundingClientRect();
            $dropdown.css({
                'position': 'fixed',
                'top': (rect.bottom + 4) + 'px',
                'left': rect.left + 'px',
                'width': rect.width + 'px',
                'z-index': '9999'
            });

            // Flip up if hitting bottom of viewport
            const menuHeight = $dropdown.outerHeight();
            if (rect.bottom + menuHeight > window.innerHeight) {
                $dropdown.css('top', (rect.top - menuHeight - 4) + 'px');
            }
        };

        // Event Listeners
        let focusOpened = false;

        $trigger.on('focus', function() {
            if ($select.is(':disabled')) return;
            if (!$dropdown.hasClass('show')) {
                $('.modern-select-dropdown').not($dropdown).removeClass('show');
                $('.modern-select-trigger').not($trigger).removeClass('active');
                $trigger.addClass('active');
                $dropdown.addClass('show');
                focusOpened = true;
                positionDropdown();

                if (isSearchable) {
                    setTimeout(() => {
                        const $input = $dropdown.find('input');
                        $input.val('').trigger('input');
                        if (!('ontouchstart' in window)) $input.focus();
                    }, 10);
                }
            }
        });

        $trigger.on('click', function(e) {
            e.preventDefault();
            e.stopPropagation();
            if ($select.is(':disabled')) return;

            if (focusOpened) {
                focusOpened = false;
                return;
            }

            const isOpen = $dropdown.hasClass('show');

            $('.modern-select-dropdown').not($dropdown).removeClass('show');
            $('.modern-select-trigger').not($trigger).removeClass('active');

            if (!isOpen) {
                $trigger.addClass('active');
                $dropdown.addClass('show');
                positionDropdown();

                if (isSearchable) {
                    setTimeout(() => {
                        const $input = $dropdown.find('input');
                        $input.val('').trigger('input');
                        if (!('ontouchstart' in window)) $input.focus();
                    }, 10);
                }
            } else {
                $trigger.removeClass('active');
                $dropdown.removeClass('show');
            }
        });

        const selectOption = ($opt) => {
            if ($select.is(':disabled')) return;
            const val = $opt.attr('data-value');
            const text = $opt.text();

            // Find next visible focusable element in DOM order after the trigger
            const findNextFocusable = () => {
                const sel = 'input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])';
                const all = document.querySelectorAll(sel);
                let past = false;
                for (const el of all) {
                    if (past && el.offsetParent !== null) return el;
                    if (el === $trigger[0]) past = true;
                }
                return null;
            };
            const nextEl = findNextFocusable();

            $select.val(val);
            $select[0].dispatchEvent(new Event('change', { bubbles: true }));
            $trigger.find('.selected-text').text(text);
            $dropdown.removeClass('show');
            $trigger.removeClass('active');

            $optionsContainer.find('.modern-select-option').removeClass('selected');
            $opt.addClass('selected');

            if (nextEl) {
                setTimeout(() => { try { nextEl.focus(); } catch(e) {} }, 0);
            }
        };

        $dropdown.on('click', '.modern-select-option', function(e) {
            e.preventDefault();
            e.stopPropagation();
            selectOption($(this));
        });

        $dropdown.on('click', function(e) { e.stopPropagation(); });

        if (isSearchable) {
            const $input = $dropdown.find('input');

            $input.on('input', function() {
                const term = $(this).val().toLowerCase();
                let hasResults = false;

                $optionsContainer.find('.modern-select-option').each(function() {
                    const text = $(this).text().toLowerCase();
                    if (text.includes(term)) {
                        $(this).removeClass('hidden');
                        hasResults = true;
                    } else {
                        $(this).addClass('hidden');
                    }
                });

                $noResults.toggle(!hasResults);

                // Clear any manual keyboard highlight when user types
                $optionsContainer.find('.modern-select-option.highlighted').removeClass('highlighted');

                // Auto-highlight first visible option
                const $firstVisible = $optionsContainer.find('.modern-select-option:not(.hidden):first');
                if ($firstVisible.length) { $firstVisible.addClass('highlighted'); }
            });

            // Keyboard navigation (Arrow Up/Down, Enter/Space/Tab to select)
            $input.on('keydown', function(e) {
                const $visibleOptions = $optionsContainer.find('.modern-select-option:not(.hidden)');

                const scrollIntoView = ($opt) => {
                    const container = $optionsContainer[0];
                    const opt = $opt[0];
                    if (opt.offsetTop < container.scrollTop) {
                        container.scrollTop = opt.offsetTop;
                    } else if (opt.offsetTop + opt.offsetHeight > container.scrollTop + container.clientHeight) {
                        container.scrollTop = opt.offsetTop + opt.offsetHeight - container.clientHeight;
                    }
                };

                if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                    e.preventDefault();
                    if ($visibleOptions.length === 0) return;

                    const $current = $optionsContainer.find('.modern-select-option.highlighted');
                    let nextIndex;

                    if ($current.length) {
                        const currentIndex = $visibleOptions.index($current);
                        nextIndex = e.key === 'ArrowDown'
                            ? Math.min(currentIndex + 1, $visibleOptions.length - 1)
                            : Math.max(currentIndex - 1, 0);
                        $current.removeClass('highlighted');
                    } else {
                        nextIndex = e.key === 'ArrowDown' ? 0 : $visibleOptions.length - 1;
                    }

                    const $next = $visibleOptions.eq(nextIndex);
                    $next.addClass('highlighted');
                    scrollIntoView($next);
                } else if (e.key === 'Enter') {
                    e.preventDefault();
                    const $highlighted = $optionsContainer.find('.modern-select-option.highlighted');
                    if ($highlighted.length) {
                        selectOption($highlighted);
                    } else if ($visibleOptions.length === 1) {
                        selectOption($visibleOptions.first());
                    }
                } else if (e.key === 'Tab') {
                    const $highlighted = $optionsContainer.find('.modern-select-option.highlighted');
                    if ($highlighted.length) {
                        e.preventDefault();
                        selectOption($highlighted);
                    }
                    // otherwise let default Tab move focus naturally
                } else if (e.key === 'Escape') {
                    $dropdown.removeClass('show');
                    $trigger.removeClass('active');
                    $trigger.focus();
                }
            });

            // Clear highlighted class when mouse moves to prevent dual highlight
            $optionsContainer.on('mouseenter', '.modern-select-option', function() {
                $optionsContainer.find('.modern-select-option.highlighted').removeClass('highlighted');
            });
        }

        // Sync back if select changes externally (e.g., cascading)
        $select.on('change', function() {
            const val = $select.val();
            const $selectedOption = $optionsContainer.find('.modern-select-option').filter(function() {
                return $(this).attr('data-value') === val;
            });

            if ($selectedOption.length) {
                $trigger.find('.selected-text').text($selectedOption.text());
                $optionsContainer.find('.modern-select-option').removeClass('selected');
                $selectedOption.addClass('selected');
            } else if (!val) {
                $trigger.find('.selected-text').text(placeholder);
                $optionsContainer.find('.modern-select-option').removeClass('selected');
            }
            // sync validation state to trigger element
            const invalid = !$select[0].validity.valid;
            $trigger.toggleClass('is-invalid', invalid).attr('aria-invalid', String(invalid));
            $validation.text(invalid ? $select[0].validationMessage : '').prop('hidden', !invalid);
        });

        // Close on scroll to avoid disconnected dropdown
        $(window).on('scroll.modernSelect', function() {
            $('.modern-select-dropdown').removeClass('show');
            $('.modern-select-trigger').removeClass('active');
        });

        // Watch for mutations (if options are added dynamically)
        const observer = new MutationObserver(() => {
            syncDisabled();
            ModernSelect.populate($select, $optionsContainer, $trigger, placeholder);
        });
        observer.observe($select[0], { childList: true, attributes: true, attributeFilter: ['disabled'] });

        $select.data('modern-select-initialized', true);
        $select.data('modern-select-trigger', $trigger);
        $select.data('modern-select-options', $optionsContainer);
        $select.data('modern-select-placeholder', placeholder);
        $select.data('modern-select-dropdown', $dropdown);
    },

    populate: function($select, $optionsContainer, $trigger, placeholder) {
        $optionsContainer.empty();
        const currentVal = $select.val();
        let foundSelected = false;

        $select.find('option').each(function() {
            const val = $(this).val();
            const text = $(this).text();
            if (!val && !text.includes('--')) return; // Skip empty placeholder options if they aren't explicit

            const isSelected = val == currentVal;
            const $opt = $('<div class="modern-select-option"></div>')
                .toggleClass('selected', isSelected)
                .attr('data-value', val)
                .text(text);
            $optionsContainer.append($opt);

            if (isSelected) {
                $trigger.find('.selected-text').text(text);
                foundSelected = true;
            }
        });

        if (!foundSelected) {
            $trigger.find('.selected-text').text(placeholder);
        }
    }
};

window.refreshModernSelect = function ($select) {
    var $options = $select.data('modern-select-options');
    var $trigger = $select.data('modern-select-trigger');
    var placeholder = $select.data('modern-select-placeholder');
    if ($options && $trigger) {
        ModernSelect.populate($select, $options, $trigger, placeholder);
    }
};

$(document).on('click', function() {
    $('.modern-select-dropdown').removeClass('show');
    $('.modern-select-trigger').removeClass('active');
});

$(document).ready(function () {
    ModernSelect.init('.js-modern-select');
});
