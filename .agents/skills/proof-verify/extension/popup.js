async function refresh() {
    const state = await chrome.runtime.sendMessage({ type: "get_status" });
    const dot = document.getElementById("dot");
    const status = document.getElementById("status");
    const focusPref = document.getElementById("focusPref");

    dot.classList.toggle("on", Boolean(state?.connected));
    status.textContent = state?.connected
        ? `Connected · ${state.lastStatus || "Ready"}`
        : "Waiting for local bridge (ws://127.0.0.1:17373)";
    focusPref.checked = Boolean(state?.focusOnRun);
}

document.getElementById("reconnectBtn").addEventListener("click", async () => {
    await chrome.runtime.sendMessage({ type: "reconnect" });
    setTimeout(refresh, 400);
});

document.getElementById("focusBtn").addEventListener("click", async () => {
    await chrome.runtime.sendMessage({ type: "focus_tab" });
});

document.getElementById("focusPref").addEventListener("change", async (e) => {
    await chrome.runtime.sendMessage({ type: "set_focus_pref", value: e.target.checked });
});

refresh();
