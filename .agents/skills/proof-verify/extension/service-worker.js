const BRIDGE_WS_URL = "ws://127.0.0.1:17373/extension";
const RECONNECT_ALARM = "gh-reconnect";

let ws = null;
let pingTimer = null;

async function setBadge(text, color) {
    try {
        await chrome.action.setBadgeText({ text });
        if (color) {
            await chrome.action.setBadgeBackgroundColor({ color });
        }
    } catch {
        // Ignore badge errors during startup
    }
}

async function saveState(patch) {
    await chrome.storage.session.set(patch);
}

async function getState() {
    return chrome.storage.session.get({
        connected: false,
        managedTabId: null,
        managedGroupId: null,
        lastStatus: "Idle",
        focusOnRun: true
    });
}

function stopPing() {
    if (pingTimer) {
        clearInterval(pingTimer);
        pingTimer = null;
    }
}

async function navigateTabCleanly(tabId, url, shouldFocus, timeoutMs = 20000) {
    // Clear simple onbeforeunload handlers before navigating away from a dirty page
    await execInMain(tabId, () => { window.onbeforeunload = null; }).catch(() => {});
    const beforeTab = await chrome.tabs.get(tabId).catch(() => null);
    const updateOpts = { url };
    if (typeof shouldFocus === "boolean") updateOpts.active = shouldFocus;
    await chrome.tabs.update(tabId, updateOpts);

    // If a beforeunload listener blocks navigation, URL stays unchanged; detect quickly and open a clean tab in the same group
    const settled = await waitForTabComplete(tabId, 4500);
    const afterTab = await chrome.tabs.get(tabId).catch(() => null);
    if (!settled && beforeTab && afterTab && beforeTab.url === afterTab.url && url !== beforeTab.url) {
        const fresh = await chrome.tabs.create({ url, active: shouldFocus ?? true });
        try {
            const state = await getState();
            const groupId = state.managedGroupId
                ? await chrome.tabs.group({ groupId: state.managedGroupId, tabIds: [fresh.id] })
                : await chrome.tabs.group({ tabIds: [fresh.id] });
            await chrome.tabGroups.update(groupId, { title: "Guided Hands", color: "cyan" });
            await saveState({ managedTabId: fresh.id, managedGroupId: groupId });
        } catch {
            await saveState({ managedTabId: fresh.id });
        }
        await waitForTabComplete(fresh.id, timeoutMs);
        return fresh.id;
    }

    if (!settled) {
        await waitForTabComplete(tabId, Math.max(2000, timeoutMs - 4500));
    }
    return tabId;
}

async function ensureManagedTab(url, requestedActive) {
    const state = await getState();
    const prefs = await chrome.storage.local.get({ focusOnRun: true });
    const shouldFocus = typeof requestedActive === "boolean" ? requestedActive : Boolean(prefs.focusOnRun);

    let tab = null;
    if (state.managedTabId) {
        try {
            tab = await chrome.tabs.get(state.managedTabId);
        } catch {
            tab = null;
        }
    }

    if (!tab) {
        tab = await chrome.tabs.create({ url: url || "about:blank", active: shouldFocus });
        try {
            const groupId = await chrome.tabs.group({ tabIds: [tab.id] });
            await chrome.tabGroups.update(groupId, { title: "Guided Hands", color: "cyan" });
            await saveState({ managedTabId: tab.id, managedGroupId: groupId });
        } catch {
            await saveState({ managedTabId: tab.id, managedGroupId: null });
        }
        if (url && url !== "about:blank") {
            await waitForTabComplete(tab.id, 20000);
        }
        return tab.id;
    }

    if (url && url !== "about:blank") {
        return navigateTabCleanly(tab.id, url, shouldFocus, 20000);
    }
    await chrome.tabs.update(tab.id, { active: shouldFocus });
    return tab.id;
}

function waitForTabComplete(tabId, timeoutMs = 20000) {
    return new Promise((resolve) => {
        let settled = false;
        const done = (ok) => {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            chrome.tabs.onUpdated.removeListener(onUpdated);
            setTimeout(() => resolve(ok), 100);
        };
        const onUpdated = (updatedId, info) => {
            if (updatedId === tabId && info.status === "complete") {
                done(true);
            }
        };
        const timer = setTimeout(() => done(false), timeoutMs);
        chrome.tabs.onUpdated.addListener(onUpdated);
        chrome.tabs.get(tabId).then((t) => {
            if (t && t.status === "complete" && t.url && t.url !== "about:blank") {
                done(true);
            }
        }).catch(() => done(false));
    });
}

async function execInMain(tabId, func, args = []) {
    const results = await chrome.scripting.executeScript({
        target: { tabId },
        world: "MAIN",
        func,
        args
    });
    if (!results || results.length === 0) {
        return null;
    }
    return results[0].result;
}

// ---- Page-world functions (injected via chrome.scripting) ----

function pageSetIndicator(statusText, mode, highlightSelector) {
    const oldStyle = document.getElementById("__gh_ext_style");
    if (!oldStyle) {
        const style = document.createElement("style");
        style.id = "__gh_ext_style";
        style.textContent = `
            @keyframes __ghPulse {
                0%, 100% { box-shadow: inset 0 0 0 3px rgba(56, 189, 248, 0.95), inset 0 0 22px rgba(56, 189, 248, 0.35); }
                50% { box-shadow: inset 0 0 0 4px rgba(14, 165, 233, 1), inset 0 0 34px rgba(56, 189, 248, 0.55); }
            }
            #__gh_ext_frame {
                position: fixed;
                inset: 0;
                z-index: 2147483646;
                pointer-events: none;
                animation: __ghPulse 1.8s ease-in-out infinite;
                transition: opacity 0.25s ease;
            }
            #__gh_ext_hud {
                position: fixed;
                bottom: 14px;
                left: 14px;
                z-index: 2147483647;
                display: flex;
                align-items: center;
                gap: 8px;
                background: rgba(15, 23, 42, 0.94);
                color: #e0f2fe;
                border: 1.5px solid #38bdf8;
                border-radius: 999px;
                padding: 7px 14px;
                font-family: system-ui, -apple-system, Segoe UI, sans-serif;
                font-size: 12.5px;
                font-weight: 500;
                box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
                pointer-events: none;
            }
            #__gh_ext_dot {
                width: 9px;
                height: 9px;
                border-radius: 50%;
                background: #38bdf8;
                box-shadow: 0 0 8px #38bdf8;
                flex-shrink: 0;
            }
        `;
        (document.head || document.documentElement).appendChild(style);
    }

    let frame = document.getElementById("__gh_ext_frame");
    if (!frame) {
        frame = document.createElement("div");
        frame.id = "__gh_ext_frame";
        document.documentElement.appendChild(frame);
    }
    let hud = document.getElementById("__gh_ext_hud");
    if (!hud) {
        hud = document.createElement("div");
        hud.id = "__gh_ext_hud";
        hud.innerHTML = `<span id="__gh_ext_dot"></span><span id="__gh_ext_text"></span>`;
        document.documentElement.appendChild(hud);
    }

    const dot = document.getElementById("__gh_ext_dot");
    const txt = document.getElementById("__gh_ext_text");
    if (txt) txt.textContent = statusText || "Guided Hands active";

    if (mode === "done") {
        frame.style.animation = "none";
        frame.style.boxShadow = "inset 0 0 0 3px rgba(34, 197, 94, 0.85)";
        if (dot) {
            dot.style.background = "#22c55e";
            dot.style.boxShadow = "0 0 8px #22c55e";
        }
        hud.style.borderColor = "#22c55e";
    } else if (mode === "error") {
        frame.style.animation = "none";
        frame.style.boxShadow = "inset 0 0 0 3px rgba(239, 68, 68, 0.9)";
        if (dot) {
            dot.style.background = "#ef4444";
            dot.style.boxShadow = "0 0 8px #ef4444";
        }
        hud.style.borderColor = "#ef4444";
    } else if (mode === "clear") {
        frame.remove();
        hud.remove();
    } else {
        frame.style.animation = "__ghPulse 1.8s ease-in-out infinite";
        if (dot) {
            dot.style.background = "#38bdf8";
            dot.style.boxShadow = "0 0 8px #38bdf8";
        }
        hud.style.borderColor = "#38bdf8";
    }

    for (const old of document.querySelectorAll("[data-gh-hl]")) {
        old.style.outline = old.dataset.ghOldOutline || "";
        old.removeAttribute("data-gh-hl");
    }
    if (highlightSelector) {
        const el = document.querySelector(highlightSelector);
        if (el) {
            const visual = el.tagName === "SELECT" && el.nextElementSibling?.classList.contains("modern-select-container")
                ? el.nextElementSibling
                : el;
            visual.dataset.ghOldOutline = visual.style.outline || "";
            visual.setAttribute("data-gh-hl", "1");
            visual.style.outline = "3px solid #38bdf8";
            visual.scrollIntoView({ block: "center", behavior: "smooth" });
        }
    }
    return true;
}

function pageInstallTelemetry() {
    if (window.__ghTelemetryInstalled) return true;
    window.__ghTelemetryInstalled = true;
    window.__ghErrors = { pageErrors: [], consoleErrors: [], failedRequests: [] };
    window.addEventListener("error", (e) => {
        window.__ghErrors.pageErrors.push(String(e.message || "Uncaught error"));
    });
    window.addEventListener("unhandledrejection", (e) => {
        window.__ghErrors.pageErrors.push(String(e.reason?.message || e.reason || "Unhandled rejection"));
    });
    const origErr = console.error;
    console.error = (...args) => {
        window.__ghErrors.consoleErrors.push(args.map((a) => String(a)).join(" ").slice(0, 300));
        origErr.apply(console, args);
    };
    const origFetch = window.fetch;
    if (typeof origFetch === "function") {
        window.fetch = async (...args) => {
            const res = await origFetch.apply(window, args);
            if (res.status >= 400) {
                window.__ghErrors.failedRequests.push({ url: res.url || String(args[0]), status: res.status });
            }
            return res;
        };
    }
    return true;
}

function pageConsumeTelemetry() {
    const out = window.__ghErrors
        ? {
            pageErrors: [...window.__ghErrors.pageErrors],
            consoleErrors: [...window.__ghErrors.consoleErrors],
            failedRequests: [...window.__ghErrors.failedRequests]
        }
        : { pageErrors: [], consoleErrors: [], failedRequests: [] };
    if (window.__ghErrors) {
        window.__ghErrors.pageErrors.length = 0;
        window.__ghErrors.consoleErrors.length = 0;
        window.__ghErrors.failedRequests.length = 0;
    }
    return out;
}

function pageExtractClickables(explicitScope) {
    const isVisible = (el) => {
        if (!el) return false;
        const st = window.getComputedStyle(el);
        if (st.display === "none" || st.visibility === "hidden" || Number(st.opacity) === 0) return false;
        const r = el.getBoundingClientRect();
        return r.width > 1 && r.height > 1;
    };

    const swalEl = document.querySelector(".swal2-popup:not([style*='display: none']):not(.swal2-hide):not(.animate__fadeOutDown)");
    const modalEl = document.querySelector(".modal.show");
    const activePopup = explicitScope
        ? document.querySelector(explicitScope)
        : ((isVisible(swalEl) ? swalEl : null)
            || (isVisible(modalEl) ? modalEl : null)
            || document.body);
    if (explicitScope && !activePopup) {
        return { inModal: false, items: [] };
    }
    const root = activePopup || document.body;
    const inModal = root !== document.body;

    for (const old of document.querySelectorAll("[data-gh-click-id]")) {
        old.removeAttribute("data-gh-click-id");
    }

    const nodes = root.querySelectorAll("button, a[href], input[type='submit'], input[type='button'], [role='button'], [onclick]");
    const items = [];
    let id = 1;
    for (const el of nodes) {
        if (el.disabled || !isVisible(el)) continue;
        const text = (el.innerText || el.value || el.getAttribute("aria-label") || el.getAttribute("title") || "")
            .replace(/\s+/g, " ")
            .trim();
        if (!text && !el.id) continue;
        el.setAttribute("data-gh-click-id", String(id));
        items.push({
            id,
            tag: el.tagName.toLowerCase(),
            text: text.slice(0, 80),
            domId: el.id || "",
            href: el.tagName === "A" ? (el.getAttribute("href") || "").slice(0, 80) : ""
        });
        id++;
        if (items.length >= 60) break;
    }
    return { inModal, items };
}

function pageClickTarget(selector) {
    let el = null;
    try {
        el = document.querySelector(selector);
    } catch {
        return false;
    }
    if (!el) return false;
    el.scrollIntoView({ block: "center" });
    el.click();
    return true;
}

function pageExtractFields(scopeSelector, onlyEmptyFlag) {
    const isVisible = (el) => {
        const st = window.getComputedStyle(el);
        if (st.display === "none" || st.visibility === "hidden") return false;
        const r = el.getBoundingClientRect();
        return r.width > 1 && r.height > 1;
    };

    const isEnhancedSelectVisible = (sel) => {
        if (sel.tagName !== "SELECT") return false;
        const next = sel.nextElementSibling;
        if (next && (next.classList.contains("modern-select-container") || next.classList.contains("select2-container"))) {
            return isVisible(next);
        }
        const wrapped = sel.parentElement?.querySelector(".modern-select-container, .select2-container");
        return wrapped ? isVisible(wrapped) : false;
    };

    const isLogoutForm = (form) => /logout|signout/i.test(form.getAttribute("action") || form.id || "");
    const pickNonLogoutForm = (container) => {
        for (const f of container.querySelectorAll("form")) {
            if (!isLogoutForm(f) && isVisible(f)) return f;
        }
        return null;
    };

    const modalEl = document.querySelector(".modal.show");
    const mainArea = document.querySelector("main, .main-content, #content") || document.body;
    let root = scopeSelector
        ? document.querySelector(scopeSelector)
        : (document.querySelector(".modal.show form")
            || modalEl
            || document.querySelector("#actualTicketEntry form")
            || pickNonLogoutForm(mainArea)
            || pickNonLogoutForm(document)
            || mainArea);
    if (!root) return { inModal: false, fields: [] };

    const getLabel = (el) => {
        if (el.id) {
            const lbl = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
            if (lbl && lbl.innerText.trim()) return lbl.innerText.replace(/\s+/g, " ").trim();
        }
        const wrap = el.closest(".ui-field, .schedule-field, .mb-3, .form-group, .form-floating, .col, [class*='col-'], td, div");
        const nearLabel = wrap?.querySelector("label");
        if (nearLabel && nearLabel.innerText.trim()) return nearLabel.innerText.replace(/\s+/g, " ").trim();
        return el.getAttribute("aria-label") || el.getAttribute("data-placeholder") || el.getAttribute("placeholder") || el.name || el.id || "";
    };

    const collectFromRoot = (targetRoot) => {
        for (const old of document.querySelectorAll("[data-gh-field-key]")) {
            old.removeAttribute("data-gh-field-key");
        }

        const controls = targetRoot.querySelectorAll("input, select, textarea");
        const fields = [];
        const usedKeys = new Set();

        for (const el of controls) {
            const tag = el.tagName.toLowerCase();
            const type = tag === "input" ? (el.getAttribute("type") || "text").toLowerCase() : tag;
            if (["hidden", "submit", "button", "reset", "file", "image"].includes(type)) continue;
            if (el.disabled || el.readOnly) continue;
            if ((el.name || "").includes("__RequestVerificationToken")) continue;
            if (el.closest(".modern-select-search, .select2-search")) continue;
            if (el.closest("form") && isLogoutForm(el.closest("form"))) continue;

            const visible = isVisible(el) || isEnhancedSelectVisible(el);
            if (!visible) continue;

            const currentValue = (type === "checkbox" || type === "radio") ? el.checked : (el.value || "");
            if (onlyEmptyFlag && typeof currentValue === "string" && currentValue.trim() !== "") {
                continue;
            }

            const baseKey = (el.id || el.name || getLabel(el) || `field_${fields.length + 1}`)
                .replace(/[^a-zA-Z0-9_.-]/g, "_")
                .replace(/^_+|_+$/g, "")
                .slice(0, 48) || `field_${fields.length + 1}`;
            let key = baseKey;
            let suffix = 2;
            while (usedKeys.has(key)) key = `${baseKey}_${suffix++}`;
            usedKeys.add(key);

            el.setAttribute("data-gh-field-key", key);

            const field = {
                key,
                tag,
                type,
                inputMode: (el.inputMode || el.getAttribute("inputmode") || "").toLowerCase(),
                label: getLabel(el).slice(0, 80),
                placeholder: (el.getAttribute("placeholder") || el.getAttribute("data-placeholder") || "").slice(0, 60),
                required: Boolean(el.required || el.getAttribute("data-val-required")),
                currentValue,
                maxLength: el.maxLength > 0 && el.maxLength < 10000 ? el.maxLength : null,
                min: el.getAttribute("min") || null,
                max: el.getAttribute("max") || null
            };

            if (tag === "select") {
                const opts = [];
                for (const opt of el.options) {
                    const val = opt.value;
                    const txt = (opt.textContent || "").replace(/\s+/g, " ").trim();
                    if (val === "" && /select|choose|--/i.test(txt)) continue;
                    if (opt.disabled) continue;
                    opts.push({ value: val, text: txt.slice(0, 60) });
                }
                field.options = opts;
                if (opts.length === 0) continue;
            }
            fields.push(field);
            if (fields.length >= 30) break;
        }
        return fields;
    };

    let fields = collectFromRoot(root);
    // Fallback to mainArea / document.body if an auto-picked form had no visible editable fields
    if (fields.length === 0 && !scopeSelector && !modalEl && root !== mainArea) {
        fields = collectFromRoot(mainArea);
    }
    if (fields.length === 0 && !scopeSelector && !modalEl && mainArea !== document.body) {
        fields = collectFromRoot(document.body);
    }

    return { inModal: Boolean(modalEl), fields };
}

function pageApplyValues(vals) {
    const applied = [];
    const nativeInputSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    const nativeTextAreaSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set;

    for (const [key, rawVal] of Object.entries(vals)) {
        const el = document.querySelector(`[data-gh-field-key="${CSS.escape(key)}"]`);
        if (!el) continue;
        const tag = el.tagName.toLowerCase();
        const type = tag === "input" ? (el.getAttribute("type") || "text").toLowerCase() : tag;

        if (type === "checkbox" || type === "radio") {
            el.checked = Boolean(rawVal);
            el.dispatchEvent(new Event("change", { bubbles: true }));
        } else if (tag === "select") {
            let targetVal = String(rawVal);
            const opts = [...el.options];
            if (!opts.some((o) => o.value === targetVal)) {
                const norm = targetVal.trim().toLowerCase();
                const byText = opts.find((o) => (o.textContent || "").trim().toLowerCase() === norm)
                    || opts.find((o) => (o.textContent || "").trim().toLowerCase().includes(norm));
                if (byText) targetVal = byText.value;
            }
            el.value = targetVal;
            el.dispatchEvent(new Event("change", { bubbles: true }));
            if (window.jQuery) {
                try {
                    const $el = window.jQuery(el);
                    $el.val(targetVal).trigger("change");
                    if (typeof window.refreshModernSelect === "function") {
                        window.refreshModernSelect($el);
                    }
                } catch {
                    // Ignore jQuery errors
                }
            }
        } else if (tag === "textarea") {
            const maxLen = el.maxLength > 0 && el.maxLength < 10000 ? el.maxLength : 0;
            const strVal = maxLen ? String(rawVal).slice(0, maxLen) : String(rawVal);
            if (nativeTextAreaSetter) nativeTextAreaSetter.call(el, strVal);
            else el.value = strVal;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
        } else {
            const maxLen = el.maxLength > 0 && el.maxLength < 10000 ? el.maxLength : 0;
            const strVal = maxLen ? String(rawVal).slice(0, maxLen) : String(rawVal);
            if (nativeInputSetter) nativeInputSetter.call(el, strVal);
            else el.value = strVal;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
        }
        applied.push({ key, value: el.tagName === "SELECT" ? el.value : rawVal });
    }
    return applied;
}

function pageReadFeedback() {
    const isVisible = (el) => {
        if (!el) return false;
        const st = window.getComputedStyle(el);
        if (st.display === "none" || st.visibility === "hidden" || Number(st.opacity) === 0) return false;
        const r = el.getBoundingClientRect();
        return r.width > 1 && r.height > 1;
    };
    const validationErrors = [...document.querySelectorAll(".field-validation-error, .validation-summary-errors li, .invalid-feedback, span.text-error, div.text-error")]
        .filter((el) => !el.closest("button, a"))
        .map((el) => (el.innerText || "").trim())
        .filter(Boolean)
        .slice(0, 10);
    const swalCandidate = document.querySelector(".swal2-popup:not([style*='display: none']):not(.swal2-hide):not(.animate__fadeOutDown)");
    const swalPopup = isVisible(swalCandidate) ? swalCandidate : null;
    const swalTitle = swalPopup?.querySelector(".swal2-title")?.innerText?.trim() || "";
    const swalText = swalPopup?.querySelector(".swal2-html-container")?.innerText?.trim() || "";
    const swalConfirmBtn = swalPopup?.querySelector(".swal2-confirm") || null;
    const swalCancelBtn = swalPopup?.querySelector(".swal2-cancel") || null;
    const swalConfirmVisible = Boolean(swalConfirmBtn && !swalConfirmBtn.disabled && isVisible(swalConfirmBtn));
    const swalCancelVisible = Boolean(swalCancelBtn && !swalCancelBtn.disabled && isVisible(swalCancelBtn));
    const swalIsLoading = Boolean(
        swalPopup && (swalPopup.classList.contains("swal2-loading")
            || swalPopup.querySelector(".swal2-loading, .modern-loader-inline")
            || (!swalConfirmVisible && /processing|saving|loading|please wait/i.test(`${swalTitle} ${swalText}`)))
    );
    const swalIsError = Boolean(swalPopup?.querySelector(".swal2-error, .swal2-warning, .modern-alert-icon--error, .modern-alert-icon--warning")
        || /error|invalid|required|failed|could not|cannot|future|must be/i.test(`${swalTitle} ${swalText}`));
    const toastText = [...document.querySelectorAll(".toast-body, .toast-message, .alert")]
        .map((el) => (el.innerText || "").replace(/\s+/g, " ").trim())
        .filter(Boolean)
        .slice(0, 5);
    const bodyText = (document.body?.innerText || "");
    const hasServerError = /An unhandled exception occurred|HTTP ERROR 500|Server Error in/i.test(bodyText.slice(0, 1500));
    return {
        url: location.href,
        pathname: location.pathname + location.search,
        title: document.title || "",
        hasServerError,
        validationErrors,
        swal: (swalPopup && (swalTitle || swalText || swalIsLoading || swalConfirmVisible))
            ? {
                title: swalTitle,
                text: swalText,
                isError: swalIsError,
                isLoading: swalIsLoading,
                confirmVisible: swalConfirmVisible,
                confirmText: (swalConfirmBtn?.innerText || "").trim(),
                cancelVisible: swalCancelVisible
            }
            : null,
        alerts: toastText,
        bodySample: bodyText.slice(0, 6000)
    };
}

function pageCheckSelector(selector) {
    try {
        return Boolean(document.querySelector(selector));
    } catch {
        return false;
    }
}

function pageAutoLogin(username, password) {
    const u = document.querySelector("#username, input[name='Input.Username']");
    const p = document.querySelector("#password, input[name='Input.Password']");
    const r = document.querySelector("input[name='Input.RememberMe']");
    const btn = document.querySelector("#login-submit, button[type='submit']");
    if (!u || !p || !btn) return false;
    u.value = username;
    u.dispatchEvent(new Event("input", { bubbles: true }));
    p.value = password;
    p.dispatchEvent(new Event("input", { bubbles: true }));
    if (r) r.checked = true;
    btn.click();
    return true;
}

function pageAutoLogout() {
    const form = document.querySelector("form#mnav-logout-form, form#logoutForm, form[action*='Logout']");
    if (!form) return false;
    form.submit();
    return true;
}

function pageSelectOption(selectorOrTarget, valueOrText, scopeSelector) {
    const root = scopeSelector
        ? document.querySelector(scopeSelector)
        : (document.querySelector(".modal.show") || document);
    if (!root) return { ok: false, reason: `Scope not found: ${scopeSelector}` };

    const getLabel = (el) => {
        if (el.id) {
            const lbl = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
            if (lbl && lbl.innerText.trim()) return lbl.innerText.replace(/\s+/g, " ").trim();
        }
        const wrap = el.closest(".ui-field, .schedule-field, .mb-3, .form-group, .form-floating, .col, [class*='col-'], td, div");
        const nearLabel = wrap?.querySelector("label");
        if (nearLabel && nearLabel.innerText.trim()) return nearLabel.innerText.replace(/\s+/g, " ").trim();
        return el.getAttribute("aria-label") || el.getAttribute("data-placeholder") || el.getAttribute("placeholder") || el.name || el.id || "";
    };

    let selectEl = null;
    const rawTarget = String(selectorOrTarget || "").trim();
    if (rawTarget) {
        if (/^[#.\[]|^select\b/i.test(rawTarget)) {
            try {
                const candidate = root.querySelector(rawTarget);
                if (candidate && candidate.tagName === "SELECT") selectEl = candidate;
                else if (candidate) selectEl = candidate.querySelector("select") || candidate.parentElement?.querySelector("select");
            } catch {
                // Fall back to semantic match
            }
        }
        if (!selectEl) {
            const needle = rawTarget.toLowerCase();
            const selects = [...root.querySelectorAll("select")].filter((s) => !s.disabled);
            selectEl = selects.find((s) => (s.id || "").toLowerCase() === needle || (s.name || "").toLowerCase() === needle)
                || selects.find((s) => getLabel(s).toLowerCase().replace(/\*/g, "").trim() === needle)
                || selects.find((s) => getLabel(s).toLowerCase().includes(needle) || (s.id || "").toLowerCase().includes(needle) || (s.name || "").toLowerCase().includes(needle));
        }
    } else {
        selectEl = [...root.querySelectorAll("select")].find((s) => !s.disabled) || null;
    }

    if (!selectEl) {
        return { ok: false, reason: `Select element not found for "${rawTarget}"` };
    }

    const opts = [...selectEl.options].filter((o) => !o.disabled);
    const nonPlaceholder = opts.filter((o) => !(o.value === "" && /select|choose|--/i.test((o.textContent || "").trim())));
    if (nonPlaceholder.length === 0) {
        return { ok: false, waitingForOptions: true, reason: `Select "${selectEl.id || selectEl.name || rawTarget}" has no populated options yet` };
    }

    const wanted = String(valueOrText ?? "").trim();
    const wantedLower = wanted.toLowerCase();
    const matchedOpt = opts.find((o) => o.value === wanted)
        || opts.find((o) => (o.textContent || "").replace(/\s+/g, " ").trim().toLowerCase() === wantedLower)
        || opts.find((o) => (o.textContent || "").replace(/\s+/g, " ").trim().toLowerCase().includes(wantedLower));

    if (!matchedOpt) {
        return {
            ok: false,
            waitingForOptions: true,
            reason: `Option "${wanted}" not found in "${selectEl.id || selectEl.name || rawTarget}" (${nonPlaceholder.length} options)`,
            availableSample: nonPlaceholder.slice(0, 8).map((o) => `${o.value}=${(o.textContent || "").trim()}`)
        };
    }

    // Prefer clicking ModernSelect option when enhanced wrapper is present so custom listeners fire identically
    const modernContainer = selectEl.nextElementSibling?.classList.contains("modern-select-container")
        ? selectEl.nextElementSibling
        : selectEl.parentElement?.querySelector(".modern-select-container");
    let usedModernClick = false;
    if (modernContainer) {
        const trigger = modernContainer.querySelector(".modern-select-trigger");
        if (trigger) {
            trigger.scrollIntoView({ block: "center" });
            trigger.click();
            const optNode = document.querySelector(`.modern-select-dropdown.show .modern-select-option[data-value="${CSS.escape(matchedOpt.value)}"]`)
                || modernContainer.querySelector(`.modern-select-option[data-value="${CSS.escape(matchedOpt.value)}"]`);
            if (optNode) {
                optNode.click();
                usedModernClick = true;
            }
        }
    }

    if (!usedModernClick || selectEl.value !== matchedOpt.value) {
        selectEl.value = matchedOpt.value;
        selectEl.dispatchEvent(new Event("input", { bubbles: true }));
        selectEl.dispatchEvent(new Event("change", { bubbles: true }));
        if (window.jQuery) {
            try {
                const $el = window.jQuery(selectEl);
                $el.val(matchedOpt.value).trigger("change");
                if (typeof window.refreshModernSelect === "function") {
                    window.refreshModernSelect($el);
                }
            } catch {
                // Ignore jQuery errors
            }
        }
    }

    return {
        ok: true,
        matchedKey: selectEl.id || selectEl.name || getLabel(selectEl),
        selectedValue: selectEl.value,
        selectedText: (matchedOpt.textContent || "").replace(/\s+/g, " ").trim(),
        usedModernClick
    };
}

function pageUploadFile(selector, fileName, mimeType, base64Content, scopeSelector) {
    const root = scopeSelector
        ? document.querySelector(scopeSelector)
        : (document.querySelector(".modal.show") || document);
    if (!root) return { ok: false, reason: `Scope not found: ${scopeSelector}` };

    const inputEl = selector
        ? (root.querySelector(selector) || document.querySelector(selector))
        : (root.querySelector("input[type='file']:not([disabled])") || document.querySelector("input[type='file']:not([disabled])"));
    if (!inputEl) {
        return { ok: false, reason: `File input not found: ${selector || "input[type='file']"}` };
    }

    const bin = atob(base64Content);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) {
        bytes[i] = bin.charCodeAt(i);
    }
    const file = new File([bytes], fileName, { type: mimeType || "application/octet-stream" });
    const dt = new DataTransfer();
    dt.items.add(file);
    inputEl.files = dt.files;
    inputEl.dispatchEvent(new Event("input", { bubbles: true }));
    inputEl.dispatchEvent(new Event("change", { bubbles: true }));
    return {
        ok: true,
        selectorUsed: inputEl.id ? `#${inputEl.id}` : (inputEl.name ? `input[name='${inputEl.name}']` : "input[type='file']"),
        fileName,
        size: bytes.length
    };
}

// ---- Command Dispatcher ----

async function handleBridgeCommand(msg) {
    const { action, params = {} } = msg;

    if (action === "open_tab") {
        await setBadge("RUN", "#f59e0b");
        const tabId = await ensureManagedTab(params.url, params.active);
        await execInMain(tabId, pageInstallTelemetry);
        await execInMain(tabId, pageSetIndicator, [params.status || "Guided Hands active", "active", ""]);
        await saveState({ lastStatus: `Opened ${params.url}` });
        return { tabId };
    }

    if (action === "close_tab") {
        const st = await getState();
        const targetTabId = params.tabId || st.managedTabId;
        if (targetTabId) {
            try {
                await chrome.tabs.remove(targetTabId);
            } catch {
                // Tab may already be closed
            }
        }
        await saveState({ managedTabId: null, managedGroupId: null, lastStatus: "Tab closed" });
        await setBadge("ON", "#0ea5e9");
        return { closed: true, tabId: targetTabId || null };
    }

    const state = await getState();
    const tabId = params.tabId || state.managedTabId;
    if (!tabId) {
        throw new Error("No active Guided Hands tab. Call open_tab first.");
    }

    if (action === "navigate") {
        const activeTabId = await navigateTabCleanly(tabId, params.url, undefined, params.timeoutMs || 20000);
        await execInMain(activeTabId, pageInstallTelemetry).catch(() => {});
        return { tabId: activeTabId, url: (await chrome.tabs.get(activeTabId)).url };
    }

    if (action === "wait_load") {
        await waitForTabComplete(tabId, params.timeoutMs || 12000);
        await execInMain(tabId, pageInstallTelemetry).catch(() => {});
        return { url: (await chrome.tabs.get(tabId)).url };
    }

    if (action === "set_indicator") {
        if (params.mode === "done" || params.mode === "clear") {
            await setBadge("ON", "#0ea5e9");
        } else if (params.mode === "error") {
            await setBadge("ERR", "#ef4444");
        } else {
            await setBadge("RUN", "#f59e0b");
        }
        await saveState({ lastStatus: params.text || "Idle" });
        await execInMain(tabId, pageSetIndicator, [params.text || "", params.mode || "active", params.highlight || ""]).catch(() => {});
        return { ok: true };
    }

    if (action === "extract_clickables") {
        return execInMain(tabId, pageExtractClickables, [params.scopeSelector || ""]);
    }

    if (action === "click") {
        const clicked = await execInMain(tabId, pageClickTarget, [params.selector]);
        return { clicked };
    }

    if (action === "extract_fields") {
        return execInMain(tabId, pageExtractFields, [params.scopeSelector || "", Boolean(params.onlyEmpty)]);
    }

    if (action === "apply_values") {
        return execInMain(tabId, pageApplyValues, [params.values || {}]);
    }

    if (action === "read_feedback") {
        return execInMain(tabId, pageReadFeedback);
    }

    if (action === "consume_telemetry") {
        return execInMain(tabId, pageConsumeTelemetry);
    }

    if (action === "check_selector") {
        const found = await execInMain(tabId, pageCheckSelector, [params.selector]);
        return { found };
    }

    if (action === "select_option") {
        return execInMain(tabId, pageSelectOption, [
            params.target || params.selector || "",
            params.value ?? params.text ?? "",
            params.scopeSelector || ""
        ]);
    }

    if (action === "upload_file") {
        return execInMain(tabId, pageUploadFile, [
            params.selector || "",
            params.fileName || "sample.pdf",
            params.mimeType || "application/pdf",
            params.base64Content || "",
            params.scopeSelector || ""
        ]);
    }

    if (action === "capture_snapshot") {
        const scopeSel = params.scopeSelector || "";
        const fb = await execInMain(tabId, pageReadFeedback);
        const clickRes = await execInMain(tabId, pageExtractClickables, [scopeSel]).catch(() => ({ inModal: false, items: [] }));
        const fieldRes = await execInMain(tabId, pageExtractFields, [scopeSel, false]).catch(() => ({ inModal: false, fields: [] }));
        const pathLower = String(fb?.pathname || "").toLowerCase();
        return {
            ...(fb || {}),
            isLoginPage: pathLower.includes("/identity/account/login"),
            isAccessDenied: pathLower.includes("/identity/account/accessdenied"),
            inModal: Boolean(clickRes?.inModal || fieldRes?.inModal),
            clickables: (clickRes?.items || []).slice(0, 25),
            fields: (fieldRes?.fields || []).slice(0, 25).map((f) => ({
                key: f.key,
                tag: f.tag,
                type: f.type,
                label: f.label,
                required: f.required,
                currentValue: f.currentValue,
                options: Array.isArray(f.options) ? f.options.slice(0, 10) : undefined
            })),
            bodyExcerpt: (fb?.bodySample || "").slice(0, 1800)
        };
    }

    if (action === "step_preflight") {
        if (params.statusText) {
            await setBadge(params.mode === "error" ? "ERR" : (params.mode === "done" ? "ON" : "RUN"),
                params.mode === "error" ? "#ef4444" : (params.mode === "done" ? "#0ea5e9" : "#f59e0b"));
            await saveState({ lastStatus: params.statusText });
            await execInMain(tabId, pageSetIndicator, [params.statusText, params.mode || "active", params.highlight || ""]).catch(() => {});
        }
        const feedback = await execInMain(tabId, pageReadFeedback);
        const out = { feedback };
        if (params.includeClickables) {
            out.clickables = await execInMain(tabId, pageExtractClickables, [params.scopeSelector || ""]);
        }
        if (params.includeFields) {
            out.fields = await execInMain(tabId, pageExtractFields, [params.scopeSelector || "", Boolean(params.onlyEmpty)]);
        }
        return out;
    }

    if (action === "auto_login") {
        const submitted = await execInMain(tabId, pageAutoLogin, [params.username || "", params.password || ""]);
        if (submitted) {
            await waitForTabComplete(tabId, 15000);
            await execInMain(tabId, pageInstallTelemetry).catch(() => {});
        }
        return { submitted, url: (await chrome.tabs.get(tabId)).url };
    }

    if (action === "auto_logout") {
        const submitted = await execInMain(tabId, pageAutoLogout);
        if (submitted) {
            await waitForTabComplete(tabId, 15000);
            await execInMain(tabId, pageInstallTelemetry).catch(() => {});
        }
        return { submitted, url: (await chrome.tabs.get(tabId)).url };
    }

    if (action === "screenshot") {
        try {
            const tab = await chrome.tabs.get(tabId);
            if (!tab.active) {
                return { dataUrl: null, skipped: true };
            }
            const dataUrl = await chrome.tabs.captureVisibleTab(tab.windowId, { format: "png" });
            return { dataUrl, skipped: false };
        } catch {
            return { dataUrl: null, skipped: true };
        }
    }

    throw new Error(`Unknown extension command: ${action}`);
}

// ---- WebSocket Bridge Connection ----

function connectBridge() {
    if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
        return;
    }

    try {
        const socket = new WebSocket(BRIDGE_WS_URL);
        ws = socket;

        socket.addEventListener("open", async () => {
            await saveState({ connected: true, lastStatus: "Connected to localhost:17373" });
            await setBadge("ON", "#0ea5e9");
            stopPing();
            pingTimer = setInterval(() => {
                if (socket.readyState === WebSocket.OPEN) {
                    socket.send(JSON.stringify({ type: "ping" }));
                }
            }, 20000);
        });

        socket.addEventListener("message", async (event) => {
            let msg;
            try {
                msg = JSON.parse(String(event.data));
            } catch {
                return;
            }
            if (msg.type === "pong") return;
            if (!msg.id) return;

            try {
                const result = await handleBridgeCommand(msg);
                if (socket.readyState === WebSocket.OPEN) {
                    socket.send(JSON.stringify({ id: msg.id, ok: true, result }));
                }
            } catch (err) {
                if (socket.readyState === WebSocket.OPEN) {
                    socket.send(JSON.stringify({ id: msg.id, ok: false, error: err?.message || String(err) }));
                }
            }
        });

        socket.addEventListener("close", async () => {
            stopPing();
            if (ws === socket) ws = null;
            await saveState({ connected: false, lastStatus: "Waiting for local bridge" });
            await setBadge("", "#64748b");
        });

        socket.addEventListener("error", () => {
            try {
                socket.close();
            } catch {
                // Ignore
            }
        });
    } catch {
        ws = null;
    }
}

// ---- Top-level MV3 Event Listeners ----

chrome.runtime.onInstalled.addListener(async () => {
    await chrome.alarms.create(RECONNECT_ALARM, { periodInMinutes: 0.5 });
    connectBridge();
});

chrome.runtime.onStartup.addListener(async () => {
    await chrome.alarms.create(RECONNECT_ALARM, { periodInMinutes: 0.5 });
    connectBridge();
});

chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === RECONNECT_ALARM) {
        connectBridge();
    }
});

chrome.tabs.onRemoved.addListener(async (tabId) => {
    const state = await getState();
    if (state.managedTabId === tabId) {
        await saveState({ managedTabId: null, managedGroupId: null });
    }
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    (async () => {
        if (message?.type === "get_status") {
            const state = await getState();
            const prefs = await chrome.storage.local.get({ focusOnRun: true });
            sendResponse({ ...state, focusOnRun: Boolean(prefs.focusOnRun) });
            return;
        }
        if (message?.type === "reconnect") {
            connectBridge();
            sendResponse({ ok: true });
            return;
        }
        if (message?.type === "focus_tab") {
            const state = await getState();
            if (state.managedTabId) {
                try {
                    const t = await chrome.tabs.update(state.managedTabId, { active: true });
                    await chrome.windows.update(t.windowId, { focused: true });
                    sendResponse({ ok: true });
                    return;
                } catch {
                    await saveState({ managedTabId: null });
                }
            }
            sendResponse({ ok: false });
            return;
        }
        if (message?.type === "set_focus_pref") {
            await chrome.storage.local.set({ focusOnRun: Boolean(message.value) });
            sendResponse({ ok: true });
        }
    })();
    return true;
});

connectBridge();
