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
            setTimeout(() => resolve(ok), 450);
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
    const activePopup = explicitScope
        ? document.querySelector(explicitScope)
        : (document.querySelector(".swal2-popup:not([style*='display: none'])")
            || document.querySelector(".modal.show")
            || document.body);
    if (explicitScope && !activePopup) {
        return { inModal: false, items: [] };
    }
    const root = activePopup || document.body;
    const inModal = root !== document.body;

    const isVisible = (el) => {
        const st = window.getComputedStyle(el);
        if (st.display === "none" || st.visibility === "hidden" || Number(st.opacity) === 0) return false;
        const r = el.getBoundingClientRect();
        return r.width > 1 && r.height > 1;
    };

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
    const el = document.querySelector(selector);
    if (!el) return false;
    el.scrollIntoView({ block: "center" });
    el.click();
    return true;
}

function pageExtractFields(scopeSelector, onlyEmptyFlag) {
    const root = scopeSelector
        ? document.querySelector(scopeSelector)
        : (document.querySelector(".modal.show form")
            || document.querySelector(".modal.show")
            || document.querySelector("#actualTicketEntry form")
            || document.querySelector("main form")
            || document.querySelector("form")
            || document.body);
    if (!root) return { inModal: false, fields: [] };

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

    for (const old of document.querySelectorAll("[data-gh-field-key]")) {
        old.removeAttribute("data-gh-field-key");
    }

    const controls = root.querySelectorAll("input, select, textarea");
    const fields = [];
    const usedKeys = new Set();

    for (const el of controls) {
        const tag = el.tagName.toLowerCase();
        const type = tag === "input" ? (el.getAttribute("type") || "text").toLowerCase() : tag;
        if (["hidden", "submit", "button", "reset", "file", "image"].includes(type)) continue;
        if (el.disabled || el.readOnly) continue;
        if ((el.name || "").includes("__RequestVerificationToken")) continue;
        if (el.closest(".modern-select-search, .select2-search")) continue;

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

    return { inModal: Boolean(document.querySelector(".modal.show")), fields };
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
    const validationErrors = [...document.querySelectorAll(".field-validation-error, .validation-summary-errors li, .invalid-feedback, span.text-error, div.text-error")]
        .filter((el) => !el.closest("button, a"))
        .map((el) => (el.innerText || "").trim())
        .filter(Boolean)
        .slice(0, 10);
    const swalPopup = document.querySelector(".swal2-popup:not([style*='display: none'])");
    const swalTitle = swalPopup?.querySelector(".swal2-title")?.innerText?.trim() || "";
    const swalText = swalPopup?.querySelector(".swal2-html-container")?.innerText?.trim() || "";
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
        swal: (swalTitle || swalText) ? { title: swalTitle, text: swalText, isError: swalIsError } : null,
        alerts: toastText,
        bodySample: bodyText.slice(0, 6000)
    };
}

function pageCheckSelector(selector) {
    return Boolean(document.querySelector(selector));
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

    if (action === "auto_login") {
        const submitted = await execInMain(tabId, pageAutoLogin, [params.username || "", params.password || ""]);
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
