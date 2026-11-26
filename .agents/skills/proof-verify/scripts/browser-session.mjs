import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright-core";

const BROWSER_CANDIDATES = [
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/usr/bin/microsoft-edge"
];

function findBrowserExecutable() {
    for (const candidate of BROWSER_CANDIDATES) {
        if (fs.existsSync(candidate)) {
            return candidate;
        }
    }
    return null;
}

export class BrowserSession {
    constructor(context, page, { channel = "chrome", tracing = false } = {}) {
        this.context = context;
        this.page = page;
        this.channel = channel;
        this.tracing = tracing;
        this._closed = false;
        this.context.once("close", () => {
            this._closed = true;
        });
    }

    get isClosed() {
        return this._closed;
    }

    async evaluate(expressionOrFn, arg) {
        if (arg !== undefined) {
            return this.page.evaluate(expressionOrFn, arg);
        }
        return this.page.evaluate(expressionOrFn);
    }

    async navigate(url, timeoutMs = 20000) {
        try {
            await this.page.goto(url, { waitUntil: "load", timeout: timeoutMs });
        } catch (err) {
            const msg = String(err?.message || "");
            if (!/Timeout|ERR_ABORTED/i.test(msg)) {
                throw err;
            }
        }
        await new Promise((r) => setTimeout(r, 400));
    }

    async waitForLoad(timeoutMs = 15000) {
        await this.page.waitForLoadState("load", { timeout: timeoutMs }).catch(() => {});
    }

    async setViewport({ width, height }) {
        await this.page.setViewportSize({ width, height });
    }

    async screenshot(filePath, options = {}) {
        fs.mkdirSync(path.dirname(filePath), { recursive: true });
        await this.page.screenshot({ path: filePath, type: "png", ...options });
        return filePath;
    }

    onPageError(handler) {
        this.page.on("pageerror", (err) => {
            handler(err?.stack || err?.message || String(err));
        });
    }

    onConsoleError(handler) {
        this.page.on("console", (msg) => {
            if (msg.type() === "error") {
                handler(msg.text() || "console.error");
            }
        });
    }

    onResponse(handler) {
        this.page.on("response", (resp) => {
            handler({ url: resp.url(), status: resp.status() });
        });
    }

    onRequestFailed(handler) {
        this.page.on("requestfailed", (req) => {
            const errorText = req.failure()?.errorText || "requestfailed";
            if (/ERR_ABORTED/i.test(errorText)) {
                return;
            }
            handler({ url: req.url(), status: 0, error: errorText });
        });
    }

    async stopTracing(tracePath) {
        if (!this.tracing) {
            return null;
        }
        this.tracing = false;
        fs.mkdirSync(path.dirname(tracePath), { recursive: true });
        await this.context.tracing.stop({ path: tracePath });
        return tracePath;
    }

    async waitForClose() {
        if (this._closed) {
            return;
        }
        await new Promise((resolve) => {
            this.context.once("close", resolve);
        });
    }

    async close() {
        if (this._closed) {
            return;
        }
        this._closed = true;
        try {
            await this.context.close();
        } catch {
            // Ignore close errors
        }
    }
}

function bringWindowsChromeToFront(titleMarker) {
    if (process.platform !== "win32") {
        return;
    }
    try {
        const ps = [
            "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class Win32Focus { [DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow); [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd); }'",
            `$proc = Get-Process chrome,msedge -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like '*${titleMarker.replace(/'/g, "''")}*' } | Select-Object -First 1`,
            "if ($proc) { [Win32Focus]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null; [Win32Focus]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null }"
        ].join("; ");
        execFileSync("powershell.exe", ["-NoProfile", "-Command", ps], { stdio: "ignore", timeout: 4000 });
    } catch {
        // Best-effort foreground activation
    }
}

export async function launchBrowserSession(profileDir, {
    headless = true,
    devtools = false,
    viewport = { width: 1440, height: 900 },
    slowMo = 0,
    trace = false
} = {}) {
    fs.mkdirSync(profileDir, { recursive: true });

    const args = [
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-networking"
    ];
    if (headless) {
        args.push("--mute-audio");
    } else {
        args.push("--window-position=80,80", `--window-size=${viewport?.width || 1360},${viewport?.height || 860}`);
        if (devtools) {
            args.push("--auto-open-devtools-for-tabs");
        }
    }

    const baseOpts = {
        headless,
        viewport: headless ? viewport : null,
        slowMo: slowMo > 0 ? slowMo : undefined,
        ignoreHTTPSErrors: true,
        args
    };

    const attempts = [
        { label: "chrome", opts: { ...baseOpts, channel: "chrome" } },
        { label: "msedge", opts: { ...baseOpts, channel: "msedge" } }
    ];
    const fallbackExe = findBrowserExecutable();
    if (fallbackExe) {
        attempts.push({ label: path.basename(fallbackExe), opts: { ...baseOpts, executablePath: fallbackExe } });
    }

    let lastErr = null;
    for (const attempt of attempts) {
        try {
            const context = await chromium.launchPersistentContext(profileDir, attempt.opts);
            const page = context.pages()[0] || await context.newPage();
            if (!headless) {
                const marker = `GuidedHands-${Date.now()}`;
                await page.evaluate((t) => { document.title = t; }, marker).catch(() => {});
                await page.bringToFront().catch(() => {});
                bringWindowsChromeToFront(marker);
            }
            let tracing = false;
            if (trace) {
                await context.tracing.start({ screenshots: true, snapshots: true, sources: false });
                tracing = true;
            }
            return new BrowserSession(context, page, { channel: attempt.label, tracing });
        } catch (err) {
            lastErr = err;
        }
    }

    throw new Error(
        `Could not launch Chrome or Microsoft Edge via playwright-core: ${lastErr?.message || "no supported browser found"}`
    );
}

export async function attemptAutoLogin(session, username, password) {
    if (!username || !password) {
        return false;
    }
    const submitted = await session.evaluate(`(() => {
        const userEl = document.querySelector("#username, input[name='Input.Username']");
        const passEl = document.querySelector("#password, input[name='Input.Password']");
        const remEl = document.querySelector("input[name='Input.RememberMe']");
        const submitEl = document.querySelector("#login-submit, button[type='submit']");
        if (!userEl || !passEl || !submitEl) return false;
        userEl.value = ${JSON.stringify(username)};
        passEl.value = ${JSON.stringify(password)};
        userEl.dispatchEvent(new Event("input", { bubbles: true }));
        passEl.dispatchEvent(new Event("input", { bubbles: true }));
        if (remEl) remEl.checked = true;
        submitEl.click();
        return true;
    })()`);

    if (!submitted) {
        return false;
    }
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        await new Promise((r) => setTimeout(r, 300));
        const pathname = await session.evaluate("location.pathname").catch(() => "");
        if (pathname && !pathname.toLowerCase().includes("/identity/account/login")) {
            return true;
        }
    }
    return false;
}
