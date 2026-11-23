#!/usr/bin/env node

import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawn, execFileSync } from "node:child_process";

const DEFAULT_PORT = 5073;
const DEFAULT_OLLAMA_URL = process.env.OLLAMA_HOST || "http://localhost:11434";
const DEFAULT_MODEL = process.env.GUIDED_HANDS_MODEL || "qwen2.5:1.5b";

const BROWSER_CANDIDATES = [
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"
];

function printUsage() {
    console.log(`Usage: node .agents/skills/proof-verify/scripts/guided-hands.mjs [options]

Option 1 ("Guided Hands"): Brain provides a step plan; local CDP + Ollama executes clicks & realistic form fills.

Options:
  --url <route|url>        Starting route or URL (e.g. /MSAP/Billing/Index)
  --steps <json>           Inline JSON array of step objects
  --steps-file <path>      Path to JSON file containing step objects (or { url, steps })
  --playbook <name|path>   Replay a saved golden playbook (0ms for unchanged fields, diff-only Ollama for changes)
  --save-playbook <name>   Save/update a golden playbook under .agents/skills/proof-verify/playbooks/ on PASS
  --no-update-playbook     When using --playbook, do not auto-update the playbook file on PASS
  --override <Key=Value>   Repeatable field override for replay (e.g. --override PortId=SUBIC)
  --var <name=value>       Repeatable template variable (built-in: {today}, {tomorrow}, {rand})
  --step <shorthand>       Repeatable step shorthand:
                             "click: Add Billing"
                             "fill: valid PH shipping billing record"
                             "wait: 800" or "wait: .modal.show"
                             "assert: Saved successfully"
                             "screenshot: after-save"
                             "navigate: /MSAP/Other"
  --inspect                Open --url and dump visible clickables & form fields as JSON (0 LLM calls)
  --benchmark              Run a local Ollama speed + schema accuracy test (no browser/app required)
  --headed                 Show Chrome/Edge window with live HUD & highlight rings
  --devtools               Open Chrome DevTools automatically (implies --headed)
  --keep-open              Keep Chrome window open after the run finishes
  --slow-mo <ms>           Extra delay between steps in ms (default: 0 for full speed)
  --cdp-port <number>      Attach to an already-running Chrome remote debugging port
  --attach-chrome          Attach to your live Chrome via DevToolsActivePort (chrome://inspect/#remote-debugging)
  --extension              Drive your main Chrome via the zero-prompt Guided Hands extension (ws://127.0.0.1:17373)
  --background             With --extension, run in a background tab in the Guided Hands tab group (do not steal focus)
  --bridge-url <url>       Local extension bridge URL (default: http://127.0.0.1:17373)
  --no-autostart           Fail instead of auto-starting IBSWeb on --port when server is down
  --port <number>          Local IBSWeb port (default: 5073)
  --base-url <url>         Base URL override (default: http://localhost:<port>)
  --model <name>           Ollama model name (default: qwen2.5:1.5b)
  --ollama-url <url>       Ollama base URL (default: http://localhost:11434)
  --user <username>        Login username (or IBS_VERIFY_USER env var)
  --pass <password>        Login password (or IBS_VERIFY_PASS env var)
  -h, --help               Show this help message
`);
}

function parseShorthandStep(raw) {
    const idx = raw.indexOf(":");
    if (idx === -1) {
        return { action: "click", target: raw.trim() };
    }
    const kind = raw.slice(0, idx).trim().toLowerCase();
    const rest = raw.slice(idx + 1).trim();
    if (kind === "click") return { action: "click", target: rest };
    if (kind === "fill" || kind === "fill_form") return { action: "fill_form", scenario: rest };
    if (kind === "wait") {
        const ms = Number.parseInt(rest, 10);
        return Number.isFinite(ms) && String(ms) === rest
            ? { action: "wait", ms }
            : { action: "wait", selector: rest };
    }
    if (kind === "assert") return { action: "assert", text: rest };
    if (kind === "screenshot") return { action: "screenshot", name: rest || "step" };
    if (kind === "navigate" || kind === "goto") return { action: "navigate", url: rest };
    throw new Error(`Unsupported --step prefix "${kind}" in "${raw}"`);
}

function parseArgs(argv) {
    const opts = {
        url: "",
        steps: [],
        stepsFile: "",
        playbook: "",
        savePlaybook: "",
        noUpdatePlaybook: false,
        cliOverrides: {},
        vars: {},
        inspect: false,
        benchmark: false,
        headed: false,
        devtools: false,
        keepOpen: false,
        slowMo: 0,
        cdpPort: 0,
        attachChrome: false,
        extension: false,
        background: false,
        bridgeUrl: (process.env.GUIDED_HANDS_BRIDGE_URL || "http://127.0.0.1:17373").replace(/\/+$/, ""),
        noAutostart: false,
        port: DEFAULT_PORT,
        baseUrl: null,
        model: DEFAULT_MODEL,
        ollamaUrl: DEFAULT_OLLAMA_URL.replace(/\/+$/, ""),
        user: process.env.IBS_VERIFY_USER || "",
        pass: process.env.IBS_VERIFY_PASS || "",
        help: false
    };

    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        const next = () => {
            if (i + 1 >= argv.length) {
                throw new Error(`Missing value for ${arg}`);
            }
            return argv[++i];
        };

        if (arg === "-h" || arg === "--help") {
            opts.help = true;
        } else if (arg === "--url") {
            opts.url = next();
        } else if (arg.startsWith("--url=")) {
            opts.url = arg.slice("--url=".length);
        } else if (arg === "--steps") {
            const parsed = JSON.parse(next());
            if (!Array.isArray(parsed)) throw new Error("--steps must be a JSON array.");
            opts.steps.push(...parsed);
        } else if (arg === "--steps-file") {
            opts.stepsFile = next();
        } else if (arg === "--playbook") {
            opts.playbook = next();
        } else if (arg === "--save-playbook") {
            opts.savePlaybook = next();
        } else if (arg === "--no-update-playbook") {
            opts.noUpdatePlaybook = true;
        } else if (arg === "--override") {
            const raw = next();
            const eq = raw.indexOf("=");
            if (eq <= 0) throw new Error(`Invalid --override "${raw}" (expected Key=Value)`);
            opts.cliOverrides[raw.slice(0, eq).trim()] = raw.slice(eq + 1).trim();
        } else if (arg === "--var") {
            const raw = next();
            const eq = raw.indexOf("=");
            if (eq <= 0) throw new Error(`Invalid --var "${raw}" (expected name=value)`);
            opts.vars[raw.slice(0, eq).trim()] = raw.slice(eq + 1).trim();
        } else if (arg === "--step") {
            opts.steps.push(parseShorthandStep(next()));
        } else if (arg === "--inspect") {
            opts.inspect = true;
        } else if (arg === "--benchmark") {
            opts.benchmark = true;
        } else if (arg === "--headed") {
            opts.headed = true;
        } else if (arg === "--devtools") {
            opts.devtools = true;
            opts.headed = true;
        } else if (arg === "--keep-open") {
            opts.keepOpen = true;
        } else if (arg === "--slow-mo") {
            opts.slowMo = Number.parseInt(next(), 10) || 0;
        } else if (arg === "--cdp-port") {
            opts.cdpPort = Number.parseInt(next(), 10) || 0;
        } else if (arg === "--attach-chrome") {
            opts.attachChrome = true;
            opts.headed = true;
            opts.keepOpen = true;
        } else if (arg === "--extension") {
            opts.extension = true;
            opts.headed = true;
            opts.keepOpen = true;
        } else if (arg === "--background") {
            opts.background = true;
        } else if (arg === "--bridge-url") {
            opts.bridgeUrl = next().replace(/\/+$/, "");
        } else if (arg === "--no-autostart") {
            opts.noAutostart = true;
        } else if (arg === "--port") {
            opts.port = Number.parseInt(next(), 10);
        } else if (arg === "--base-url") {
            opts.baseUrl = next().replace(/\/+$/, "");
        } else if (arg === "--model") {
            opts.model = next();
        } else if (arg === "--ollama-url") {
            opts.ollamaUrl = next().replace(/\/+$/, "");
        } else if (arg === "--user") {
            opts.user = next();
        } else if (arg === "--pass") {
            opts.pass = next();
        } else {
            throw new Error(`Unknown option: ${arg}`);
        }
    }

    if (opts.stepsFile) {
        const loaded = JSON.parse(fs.readFileSync(opts.stepsFile, "utf8"));
        if (Array.isArray(loaded)) {
            opts.steps.push(...loaded);
        } else if (loaded && typeof loaded === "object") {
            if (!opts.url && loaded.url) opts.url = loaded.url;
            if (Array.isArray(loaded.steps)) opts.steps.push(...loaded.steps);
        } else {
            throw new Error("--steps-file must contain a JSON array or { url, steps } object.");
        }
    }

    if (!opts.baseUrl) {
        opts.baseUrl = `http://localhost:${opts.port}`;
    }
    return opts;
}

function getRepoRoot() {
    const scriptDir = path.dirname(fileURLToPath(import.meta.url));
    for (const cwd of [process.cwd(), scriptDir]) {
        try {
            const out = execFileSync("git", ["rev-parse", "--show-toplevel"], { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] });
            return path.resolve(out.trim());
        } catch {
            // Try fallback directory
        }
    }
    throw new Error("Could not locate git repository root.");
}

function runGit(repoRoot, args) {
    try {
        const stdout = execFileSync("git", args, { cwd: repoRoot, encoding: "utf8" });
        return { ok: true, stdout };
    } catch (err) {
        return { ok: false, stdout: err.stdout ? String(err.stdout) : "" };
    }
}

function killProcessTree(proc) {
    if (!proc || !proc.pid) return;
    try {
        if (process.platform === "win32") {
            execFileSync("taskkill", ["/PID", String(proc.pid), "/T", "/F"], { stdio: "ignore" });
        } else {
            proc.kill("SIGTERM");
        }
    } catch {
        // Ignore already exited process
    }
}

async function checkHealth(baseUrl, timeoutMs = 2000) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const resp = await fetch(`${baseUrl}/health`, { signal: controller.signal });
        return resp.ok;
    } catch {
        return false;
    } finally {
        clearTimeout(timer);
    }
}

async function ensureAppRunning(repoRoot, baseUrl, port, noAutostart) {
    if (await checkHealth(baseUrl, 2000)) {
        return { startedProc: null };
    }
    if (noAutostart) {
        throw new Error(`App is not healthy at ${baseUrl}/health (and --no-autostart was set).`);
    }
    console.log(`[guided-hands] Starting IBSWeb on http://localhost:${port}...`);
    const proc = spawn(
        "dotnet",
        ["run", "--project", "IBSWeb/IBSWeb.csproj", "--no-launch-profile", "--urls", `http://localhost:${port}`],
        {
            cwd: repoRoot,
            env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Development" },
            stdio: ["ignore", "pipe", "pipe"]
        }
    );
    const logs = [];
    const onLog = (chunk) => {
        for (const line of String(chunk).split(/\r?\n/).filter(Boolean)) {
            logs.push(line);
            if (logs.length > 60) logs.shift();
        }
    };
    proc.stdout.on("data", onLog);
    proc.stderr.on("data", onLog);

    let exited = false;
    proc.on("exit", () => { exited = true; });

    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        if (exited) {
            throw new Error(`IBSWeb exited early:\n${logs.slice(-20).join("\n")}`);
        }
        if (await checkHealth(baseUrl, 1500)) {
            return { startedProc: proc };
        }
        await new Promise((r) => setTimeout(r, 500));
    }
    killProcessTree(proc);
    throw new Error(`Timed out waiting for IBSWeb /health at ${baseUrl}.`);
}

function findBrowserExecutable() {
    for (const candidate of BROWSER_CANDIDATES) {
        if (fs.existsSync(candidate)) return candidate;
    }
    throw new Error("Could not find Chrome or Microsoft Edge executable.");
}

class CdpSession {
    constructor(wsUrl) {
        this.wsUrl = wsUrl;
        this.ws = null;
        this.sessionId = null;
        this.nextId = 1;
        this.pending = new Map();
        this.listeners = new Map();
    }

    async connect() {
        await new Promise((resolve, reject) => {
            const ws = new WebSocket(this.wsUrl);
            this.ws = ws;
            ws.addEventListener("open", () => resolve());
            ws.addEventListener("error", (err) => reject(new Error(`CDP WebSocket error: ${err.message || "failed"}`)));
            ws.addEventListener("message", (event) => {
                const msg = JSON.parse(String(event.data));
                if (msg.id) {
                    const entry = this.pending.get(msg.id);
                    if (entry) {
                        this.pending.delete(msg.id);
                        if (msg.error) entry.reject(new Error(`${entry.method}: ${msg.error.message}`));
                        else entry.resolve(msg.result);
                    }
                } else if (msg.method) {
                    if (this.sessionId && msg.sessionId && msg.sessionId !== this.sessionId) {
                        return;
                    }
                    const handlers = this.listeners.get(msg.method);
                    if (handlers) {
                        for (const fn of handlers) fn(msg.params || {});
                    }
                }
            });
        });
    }

    on(eventName, handler) {
        if (!this.listeners.has(eventName)) this.listeners.set(eventName, []);
        this.listeners.get(eventName).push(handler);
    }

    off(eventName, handler) {
        const handlers = this.listeners.get(eventName);
        if (!handlers) return;
        this.listeners.set(eventName, handlers.filter((fn) => fn !== handler));
    }

    send(method, params = {}, useBrowserSession = false) {
        const id = this.nextId++;
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                if (this.pending.has(id)) {
                    this.pending.delete(id);
                    reject(new Error(`CDP timeout waiting for ${method}`));
                }
            }, 25000);
            this.pending.set(id, {
                method,
                resolve: (res) => { clearTimeout(timer); resolve(res); },
                reject: (err) => { clearTimeout(timer); reject(err); }
            });
            const payload = { id, method, params };
            if (this.sessionId && !useBrowserSession) {
                payload.sessionId = this.sessionId;
            }
            this.ws.send(JSON.stringify(payload));
        });
    }

    async evaluate(expression) {
        const res = await this.send("Runtime.evaluate", {
            expression,
            returnByValue: true,
            awaitPromise: true
        });
        if (res.exceptionDetails) {
            throw new Error(res.exceptionDetails.exception?.description || res.exceptionDetails.text || "Runtime.evaluate exception");
        }
        return res.result ? res.result.value : undefined;
    }

    close() {
        try {
            this.ws?.close();
        } catch {
            // Ignore close errors
        }
    }
}

async function connectToCdpPort(devToolsPort) {
    let targets = [];
    for (let i = 0; i < 20; i++) {
        const list = await (await fetch(`http://127.0.0.1:${devToolsPort}/json/list`)).json();
        targets = list.filter((t) => t.type === "page" && t.webSocketDebuggerUrl);
        if (targets.length > 0) break;
        await new Promise((r) => setTimeout(r, 150));
    }
    if (targets.length === 0) {
        const created = await fetch(`http://127.0.0.1:${devToolsPort}/json/new?about:blank`, { method: "PUT" });
        targets = [await created.json()];
    }

    const cdp = new CdpSession(targets[0].webSocketDebuggerUrl);
    await cdp.connect();
    await cdp.send("Page.enable");
    await cdp.send("Runtime.enable");
    await cdp.send("Network.enable");
    return cdp;
}

function findFreePort() {
    return new Promise((resolve, reject) => {
        const srv = net.createServer();
        srv.listen(0, "127.0.0.1", () => {
            const addr = srv.address();
            const port = typeof addr === "object" && addr ? addr.port : 0;
            srv.close(() => resolve(port));
        });
        srv.on("error", reject);
    });
}

async function waitForCdpPort(devToolsPort, timeoutMs = 15000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        try {
            const resp = await fetch(`http://127.0.0.1:${devToolsPort}/json/version`);
            if (resp.ok) return;
        } catch {
            // Not ready yet
        }
        await new Promise((r) => setTimeout(r, 150));
    }
    throw new Error(`Timed out waiting for Chrome DevTools on http://127.0.0.1:${devToolsPort}`);
}

async function attachToUserChrome() {
    const localAppData = process.env.LOCALAPPDATA || path.join(process.env.USERPROFILE || "", "AppData", "Local");
    const portFile = path.join(localAppData, "Google", "Chrome", "User Data", "DevToolsActivePort");
    if (!fs.existsSync(portFile)) {
        throw new Error(`DevToolsActivePort not found at ${portFile}. Enable remote debugging at chrome://inspect/#remote-debugging.`);
    }
    const [portLine, wsPathLine] = fs.readFileSync(portFile, "utf8").trim().split(/\r?\n/);
    const port = Number.parseInt((portLine || "").trim(), 10);
    const wsPath = (wsPathLine || "").trim();
    if (!port || !wsPath) {
        throw new Error(`Invalid DevToolsActivePort contents at ${portFile}.`);
    }

    const browserWsUrl = `ws://127.0.0.1:${port}${wsPath}`;
    console.log(`[guided-hands] Attaching to live Chrome via DevToolsActivePort (port ${port})...`);
    const cdp = new CdpSession(browserWsUrl);
    await cdp.connect();
    const { targetId } = await cdp.send("Target.createTarget", { url: "about:blank" }, true);
    const { sessionId } = await cdp.send("Target.attachToTarget", { targetId, flatten: true }, true);
    cdp.sessionId = sessionId;
    await cdp.send("Page.bringToFront");
    await cdp.send("Page.enable");
    await cdp.send("Runtime.enable");
    await cdp.send("Network.enable");
    return { proc: null, cdp, devToolsPort: port, attached: true };
}

async function launchBrowserWithCdp(profileDir, { headless, devtools = false, keepOpen = false, cdpPort = 0, attachChrome = false }) {
    if (attachChrome) {
        return attachToUserChrome();
    }
    if (cdpPort > 0) {
        console.log(`[guided-hands] Attaching to existing Chrome CDP on http://127.0.0.1:${cdpPort}`);
        const cdp = await connectToCdpPort(cdpPort);
        return { proc: null, cdp, devToolsPort: cdpPort, attached: true };
    }

    fs.mkdirSync(profileDir, { recursive: true });
    const exe = findBrowserExecutable();

    // On Windows headed mode, launch via Start-Process -WindowStyle Normal so Chrome
    // actually displays a visible window even when invoked from a hidden agent shell.
    if (!headless && process.platform === "win32") {
        const chosenPort = await findFreePort();
        const chromeArgs = [
            `--remote-debugging-port=${chosenPort}`,
            `--user-data-dir=${profileDir}`,
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-networking",
            "--window-size=1440,900",
            "--new-window"
        ];
        if (devtools) chromeArgs.push("--auto-open-devtools-for-tabs");
        chromeArgs.push("about:blank");

        const psArray = chromeArgs.map((a) => `'${a.replace(/'/g, "''")}'`).join(", ");
        const psCmd = `$p = Start-Process -FilePath '${exe.replace(/'/g, "''")}' -ArgumentList @(${psArray}) -WindowStyle Normal -PassThru; Write-Output $p.Id`;
        const pidOut = execFileSync("powershell.exe", ["-NoProfile", "-Command", psCmd], { encoding: "utf8" }).trim();
        const pid = Number.parseInt(pidOut.split(/\r?\n/).pop() || "0", 10);

        await waitForCdpPort(chosenPort, 15000);
        console.log(`[guided-hands] Visible Chrome launched (PID ${pid}) | CDP: http://127.0.0.1:${chosenPort}`);
        const cdp = await connectToCdpPort(chosenPort);
        return { proc: pid > 0 ? { pid } : null, cdp, devToolsPort: chosenPort, attached: false };
    }

    const args = [
        "--remote-debugging-port=0",
        `--user-data-dir=${profileDir}`,
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-networking",
        "--mute-audio",
        "--window-size=1440,900"
    ];
    if (headless) {
        args.push("--headless=new", "--disable-gpu");
    } else {
        args.push("--new-window");
        if (devtools) args.push("--auto-open-devtools-for-tabs");
    }
    args.push("about:blank");

    const proc = spawn(exe, args, {
        detached: keepOpen,
        windowsHide: headless,
        stdio: ["ignore", "ignore", "pipe"]
    });
    const devToolsPort = await new Promise((resolve, reject) => {
        let stderr = "";
        const timer = setTimeout(() => reject(new Error(`Timed out waiting for DevTools port.\n${stderr}`)), 15000);
        proc.stderr.on("data", (chunk) => {
            stderr += String(chunk);
            const m = stderr.match(/DevTools listening on ws:\/\/127\.0\.0\.1:(\d+)\//);
            if (m) {
                clearTimeout(timer);
                resolve(Number.parseInt(m[1], 10));
            }
        });
        proc.on("exit", (code) => {
            clearTimeout(timer);
            reject(new Error(`Browser exited early (code ${code}).\n${stderr}`));
        });
    });

    console.log(`[guided-hands] Chrome DevTools CDP listening on http://127.0.0.1:${devToolsPort}`);
    const cdp = await connectToCdpPort(devToolsPort);
    return { proc, cdp, devToolsPort, attached: false };
}

async function updateVisualHud(cdp, statusText, selectorToHighlight = "") {
    await cdp.evaluate(`((status, sel) => {
        let hud = document.getElementById("__gh_hud");
        if (!hud) {
            hud = document.createElement("div");
            hud.id = "__gh_hud";
            Object.assign(hud.style, {
                position: "fixed",
                bottom: "14px",
                left: "14px",
                zIndex: "2147483647",
                background: "rgba(15, 23, 42, 0.94)",
                color: "#38bdf8",
                border: "1px solid #38bdf8",
                borderRadius: "8px",
                padding: "8px 14px",
                fontFamily: "Consolas, monospace",
                fontSize: "13px",
                boxShadow: "0 6px 20px rgba(0,0,0,0.45)",
                pointerEvents: "none"
            });
            document.body?.appendChild(hud);
        }
        hud.textContent = status;

        for (const old of document.querySelectorAll("[data-gh-hl]")) {
            old.style.outline = old.dataset.ghOldOutline || "";
            old.removeAttribute("data-gh-hl");
        }
        if (sel) {
            const el = document.querySelector(sel);
            if (el) {
                const visual = el.tagName === "SELECT" && el.nextElementSibling?.classList.contains("modern-select-container")
                    ? el.nextElementSibling
                    : el;
                visual.dataset.ghOldOutline = visual.style.outline || "";
                visual.setAttribute("data-gh-hl", "1");
                visual.style.outline = "3px solid #38bdf8";
                visual.scrollIntoView({ block: "center", behavior: "instant" });
            }
        }
    })(${JSON.stringify(statusText)}, ${JSON.stringify(selectorToHighlight)})`).catch(() => {});
}

async function navigateAndWait(cdp, url, timeoutMs = 20000) {
    let loaded = false;
    const onLoad = () => { loaded = true; };
    cdp.on("Page.loadEventFired", onLoad);
    try {
        await cdp.send("Page.navigate", { url });
        const start = Date.now();
        while (!loaded && Date.now() - start < timeoutMs) {
            await new Promise((r) => setTimeout(r, 100));
        }
        await new Promise((r) => setTimeout(r, 600));
    } finally {
        cdp.off("Page.loadEventFired", onLoad);
    }
}

async function attemptAutoLogin(cdp, username, password) {
    if (!username || !password) return false;
    const submitted = await cdp.evaluate(`(() => {
        const u = document.querySelector("#username, input[name='Input.Username']");
        const p = document.querySelector("#password, input[name='Input.Password']");
        const r = document.querySelector("input[name='Input.RememberMe']");
        const btn = document.querySelector("#login-submit, button[type='submit']");
        if (!u || !p || !btn) return false;
        u.value = ${JSON.stringify(username)};
        p.value = ${JSON.stringify(password)};
        if (r) r.checked = true;
        btn.click();
        return true;
    })()`);
    if (!submitted) return false;
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        await new Promise((r) => setTimeout(r, 300));
        const p = await cdp.evaluate("location.pathname").catch(() => "");
        if (p && !p.toLowerCase().includes("/identity/account/login")) return true;
    }
    return false;
}

// ============================================================================
// Ollama Structured Output Client (local worker brain-let)
// ============================================================================

async function callOllamaJson({ ollamaUrl, model, system, prompt, schema, temperature = 0.2, numCtx = 2048 }) {
    const start = Date.now();
    const body = {
        model,
        stream: false,
        format: schema,
        keep_alive: "2m",
        options: {
            temperature,
            num_ctx: numCtx
        },
        messages: [
            { role: "system", content: system },
            { role: "user", content: prompt }
        ]
    };
    if (/^qwen3/i.test(model)) {
        body.think = false;
    }

    const resp = await fetch(`${ollamaUrl}/api/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
    });
    if (!resp.ok) {
        const errText = await resp.text().catch(() => "");
        throw new Error(`Ollama HTTP ${resp.status}: ${errText.slice(0, 300)}`);
    }
    const data = await resp.json();
    const raw = data?.message?.content || "";
    const parsed = JSON.parse(raw);
    return {
        data: parsed,
        stats: {
            model,
            durationMs: Date.now() - start,
            promptTokens: data.prompt_eval_count || 0,
            outputTokens: data.eval_count || 0
        }
    };
}

// ============================================================================
// DOM Inspection & Action Execution (inside browser via CDP)
// ============================================================================

async function extractClickables(cdp, scopeSelector = "") {
    return cdp.evaluate(`(() => {
        const explicitScope = ${JSON.stringify(scopeSelector)};
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

        // Tag elements transiently with data-gh-click-id for deterministic clicking
        for (const old of document.querySelectorAll("[data-gh-click-id]")) {
            old.removeAttribute("data-gh-click-id");
        }

        const nodes = root.querySelectorAll("button, a[href], input[type='submit'], input[type='button'], [role='button'], [onclick]");
        const items = [];
        let id = 1;
        for (const el of nodes) {
            if (el.disabled || !isVisible(el)) continue;
            const text = (el.innerText || el.value || el.getAttribute("aria-label") || el.getAttribute("title") || "")
                .replace(/\\s+/g, " ")
                .trim();
            if (!text && !el.id) continue;
            el.setAttribute("data-gh-click-id", String(id));
            items.push({
                id,
                tag: el.tagName.toLowerCase(),
                text: text.slice(0, 80),
                domId: el.id || "",
                href: el.tagName === "A" ? (el.getAttribute("href") || "").slice(0, 60) : ""
            });
            id++;
            if (items.length >= 45) break;
        }
        return { inModal, items };
    })()`);
}

async function extractFormFields(cdp, scopeSelector = "", onlyEmpty = false) {
    return cdp.evaluate(`(() => {
        const root = ${JSON.stringify(scopeSelector)}
            ? document.querySelector(${JSON.stringify(scopeSelector)})
            : (document.querySelector(".modal.show form")
                || document.querySelector(".modal.show")
                || document.querySelector("main form")
                || document.querySelector("form")
                || document.body);
        if (!root) return { inModal: false, fields: [] };

        const onlyEmptyFlag = ${Boolean(onlyEmpty)};

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
                const lbl = document.querySelector(\`label[for="\${CSS.escape(el.id)}"]\`);
                if (lbl && lbl.innerText.trim()) return lbl.innerText.replace(/\\s+/g, " ").trim();
            }
            const wrap = el.closest(".ui-field, .schedule-field, .mb-3, .form-group, .form-floating, .col, [class*='col-'], td, div");
            const nearLabel = wrap?.querySelector("label");
            if (nearLabel && nearLabel.innerText.trim()) return nearLabel.innerText.replace(/\\s+/g, " ").trim();
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

            let baseKey = (el.id || el.name || getLabel(el) || \`field_\${fields.length + 1}\`)
                .replace(/[^a-zA-Z0-9_.-]/g, "_")
                .replace(/^_+|_+$/g, "")
                .slice(0, 48) || \`field_\${fields.length + 1}\`;
            let key = baseKey;
            let suffix = 2;
            while (usedKeys.has(key)) key = \`\${baseKey}_\${suffix++}\`;
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
                    const txt = (opt.textContent || "").replace(/\\s+/g, " ").trim();
                    if (val === "" && /select|choose|--/i.test(txt)) continue;
                    if (opt.disabled) continue;
                    opts.push({ value: val, text: txt.slice(0, 60) });
                }
                field.options = opts;
                if (opts.length === 0) continue; // Skip unloaded cascading selects until options populate
            }
            fields.push(field);
            if (fields.length >= 30) break;
        }

        return { inModal: Boolean(document.querySelector(".modal.show")), fields };
    })()`);
}

function buildFormJsonSchema(fields) {
    const properties = {};
    const required = [];

    for (const f of fields) {
        if (f.type === "checkbox" || f.type === "radio") {
            properties[f.key] = { type: "boolean" };
        } else if (f.tag === "select" && Array.isArray(f.options) && f.options.length > 0) {
            properties[f.key] = {
                type: "string",
                enum: f.options.slice(0, 10).map((o) => o.value)
            };
        } else if (f.type === "number" || f.inputMode === "decimal" || f.inputMode === "numeric") {
            properties[f.key] = { type: "number" };
        } else {
            properties[f.key] = { type: "string" };
        }
        required.push(f.key);
    }

    return {
        type: "object",
        properties,
        required,
        additionalProperties: false
    };
}

async function resolveClickTarget(clickables, targetText, ollamaOpts, optional = false) {
    const norm = targetText.trim().toLowerCase();

    // 1. Zero-latency deterministic exact match
    const exact = clickables.filter((c) => c.text.toLowerCase() === norm || c.domId.toLowerCase() === norm);
    if (exact.length === 1) {
        return { matched: exact[0], via: "exact", llmStats: null };
    }

    // 2. Zero-latency unambiguous substring match
    const sub = clickables.filter((c) => c.text.toLowerCase().includes(norm));
    if (sub.length === 1) {
        return { matched: sub[0], via: "substring", llmStats: null };
    }

    // Optional clicks (e.g. conditional confirmation popups) must never fuzzy-click an unrelated button
    if (optional) {
        return { matched: null, via: "optional-skip", reason: "No exact/substring match for optional target", llmStats: null };
    }

    // 3. Small local Ollama model picks from the tiny numbered list
    const menu = clickables
        .map((c) => `[${c.id}] <${c.tag}> "${c.text}"${c.domId ? ` (#${c.domId})` : ""}`)
        .join("\n");

    const schema = {
        type: "object",
        properties: {
            elementId: { type: "integer" },
            reason: { type: "string" }
        },
        required: ["elementId", "reason"],
        additionalProperties: false
    };

    const res = await callOllamaJson({
        ...ollamaOpts,
        temperature: 0,
        system: "You match a requested UI action target to ONE numbered element from the provided list. Return elementId=0 if no element matches.",
        prompt: `Target to click: "${targetText}"\n\nVisible elements:\n${menu}\n\nReturn JSON with the single best matching elementId.`,
        schema
    });

    const chosen = clickables.find((c) => c.id === res.data.elementId) || null;
    return { matched: chosen, via: "ollama", reason: res.data.reason, llmStats: res.stats };
}

function resolveOverrides(fields, overrides) {
    const resolved = {};
    if (!overrides) return resolved;

    if (Array.isArray(overrides)) {
        // Supports [{ match: "Service", index: 0, value: "Docking" }, ...]
        for (const rule of overrides) {
            const needle = String(rule.match || rule.key || rule.label || "").trim().toLowerCase();
            const matches = fields.filter((f) =>
                f.key.toLowerCase() === needle
                || f.label.toLowerCase().includes(needle)
                || f.key.toLowerCase().includes(needle)
            );
            const idx = Number.isInteger(rule.index) ? rule.index : 0;
            const targetField = matches[idx];
            if (targetField) {
                resolved[targetField.key] = rule.value;
            }
        }
        return resolved;
    }

    for (const [k, v] of Object.entries(overrides)) {
        const needle = k.trim().toLowerCase();
        const found = fields.find((f) => f.key.toLowerCase() === needle)
            || fields.find((f) => f.label.toLowerCase().replace(/\*/g, "").trim() === needle)
            || fields.find((f) => f.label.toLowerCase().includes(needle));
        if (found) {
            resolved[found.key] = v;
        } else {
            resolved[k] = v;
        }
    }
    return resolved;
}

function getPhilippineNowText() {
    const now = new Date();
    const ph = new Date(now.getTime() + 8 * 60 * 60 * 1000);
    return ph.toISOString().slice(0, 16).replace("T", " ") + " (UTC+8)";
}

function buildTemplateVars(userVars = {}) {
    const now = new Date();
    const phToday = new Date(now.getTime() + 8 * 60 * 60 * 1000);
    const phTomorrow = new Date(phToday.getTime() + 24 * 60 * 60 * 1000);
    const rand = String(Math.floor(1000 + Math.random() * 9000));
    return {
        today: phToday.toISOString().slice(0, 10),
        tomorrow: phTomorrow.toISOString().slice(0, 10),
        rand,
        ...userVars
    };
}

function expandTemplate(value, vars) {
    if (typeof value !== "string") return value;
    return value.replace(/\{([a-zA-Z0-9_]+)\}/g, (m, key) => (key in vars ? String(vars[key]) : m));
}

function expandTemplatesDeep(obj, vars) {
    if (typeof obj === "string") return expandTemplate(obj, vars);
    if (Array.isArray(obj)) return obj.map((item) => expandTemplatesDeep(item, vars));
    if (obj && typeof obj === "object") {
        const out = {};
        for (const [k, v] of Object.entries(obj)) {
            out[k] = expandTemplatesDeep(v, vars);
        }
        return out;
    }
    return obj;
}

function resolvePlaybookPath(repoRoot, nameOrPath) {
    if (!nameOrPath) return "";
    if (nameOrPath.includes("/") || nameOrPath.includes("\\") || nameOrPath.endsWith(".json")) {
        return path.resolve(nameOrPath);
    }
    const safe = nameOrPath.replace(/[^a-zA-Z0-9_-]+/g, "-").toLowerCase();
    return path.join(repoRoot, ".agents", "skills", "proof-verify", "playbooks", `${safe}.json`);
}

function isDynamicFieldKey(key) {
    return /_\d{10,}/.test(key) || /tug_temp_|srv_tug_/i.test(key);
}

function selectHasOption(field, value) {
    if (field.tag !== "select" || !Array.isArray(field.options)) return true;
    const raw = String(value);
    const norm = raw.trim().toLowerCase();
    return field.options.some(
        (o) => o.value === raw || o.text.trim().toLowerCase() === norm || o.text.trim().toLowerCase().includes(norm)
    );
}

function matchKnownFields(fields, knownFields, vars) {
    const reused = {};
    const drifted = [];
    if (!knownFields || typeof knownFields !== "object") {
        return { reused, drifted: fields };
    }
    for (const f of fields) {
        if (isDynamicFieldKey(f.key)) {
            drifted.push(f);
            continue;
        }
        const saved = knownFields[f.key];
        if (!saved || saved.value === undefined || saved.value === null || saved.value === "") {
            drifted.push(f);
            continue;
        }
        const expanded = expandTemplate(saved.value, vars);
        if (!selectHasOption(f, expanded)) {
            drifted.push(f);
            continue;
        }
        reused[f.key] = expanded;
    }
    return { reused, drifted };
}

function snapshotKnownFields(fields, appliedValues, existingKnown = {}, stepOverrides = null) {
    const next = { ...(existingKnown || {}) };
    const overrideMap = stepOverrides && !Array.isArray(stepOverrides) ? stepOverrides : {};
    for (const f of fields) {
        if (isDynamicFieldKey(f.key)) continue;
        if (!(f.key in appliedValues)) continue;
        // Preserve template strings from existing knownFields or step overrides when present
        const templateVal = typeof overrideMap[f.key] === "string" && /\{[a-zA-Z0-9_]+\}/.test(overrideMap[f.key])
            ? overrideMap[f.key]
            : (typeof existingKnown?.[f.key]?.value === "string" && /\{[a-zA-Z0-9_]+\}/.test(existingKnown[f.key].value)
                ? existingKnown[f.key].value
                : appliedValues[f.key]);
        next[f.key] = {
            label: f.label,
            type: f.type,
            value: templateVal
        };
    }
    return next;
}

function enforceDateTimeGuards(fields, values) {
    const out = { ...values };
    const byKey = new Map(fields.map((f) => [f.key, f]));
    const nowPhMs = Date.now() + 8 * 60 * 60 * 1000;
    const fmtDate = (ms) => new Date(ms).toISOString().slice(0, 10);
    const fmtTime = (ms) => new Date(ms).toISOString().slice(11, 16);
    const fmtDateTime = (ms) => new Date(ms).toISOString().slice(0, 16);

    // 1. Completed actual ticket dates/times (DateLeft + TimeLeft -> DateArrived + TimeArrived <= now UTC+8)
    if (byKey.has("DateLeft") && byKey.has("TimeLeft") && byKey.has("DateArrived") && byKey.has("TimeArrived")) {
        const parsePh = (d, t) => Date.parse(`${d}T${t}:00Z`);
        const startMs = parsePh(out.DateLeft, out.TimeLeft);
        const endMs = parsePh(out.DateArrived, out.TimeArrived);
        const safeEndLimit = nowPhMs - 5 * 60 * 1000; // at least 5 minutes in the past
        if (!Number.isFinite(startMs) || !Number.isFinite(endMs) || endMs <= startMs || endMs > safeEndLimit) {
            const fixedEnd = nowPhMs - 60 * 60 * 1000;
            const fixedStart = fixedEnd - 90 * 60 * 1000;
            out.DateLeft = fmtDate(fixedStart);
            out.TimeLeft = fmtTime(fixedStart);
            out.DateArrived = fmtDate(fixedEnd);
            out.TimeArrived = fmtTime(fixedEnd);
            if (byKey.has("Date")) {
                out.Date = out.DateLeft;
            }
            console.log(`  [guard] Normalized actual service window to ${out.DateLeft} ${out.TimeLeft} -> ${out.DateArrived} ${out.TimeArrived} (UTC+8)`);
        }
    }

    // 2. Planned datetime-local window (PlannedStart < PlannedEnd)
    if (byKey.has("PlannedStart") && byKey.has("PlannedEnd") && out.PlannedStart && out.PlannedEnd) {
        const sMs = Date.parse(`${out.PlannedStart}:00Z`);
        const eMs = Date.parse(`${out.PlannedEnd}:00Z`);
        if (Number.isFinite(sMs) && (!Number.isFinite(eMs) || eMs <= sMs)) {
            out.PlannedEnd = fmtDateTime(sMs + 2 * 60 * 60 * 1000);
            console.log(`  [guard] Adjusted PlannedEnd to ${out.PlannedEnd} (+2h after PlannedStart)`);
        }
    }

    return out;
}

async function generateFormValues(fields, scenario, overrides, ollamaOpts, repairContext = null, knownFields = null, vars = {}) {
    const expandedOverrides = expandTemplatesDeep(overrides, vars);
    const resolvedOverrides = repairContext ? {} : resolveOverrides(fields, expandedOverrides);
    const afterOverrides = fields.filter((f) => !(f.key in resolvedOverrides));

    let reusedKnown = {};
    let remainingFields = afterOverrides;
    if (!repairContext && knownFields) {
        const matched = matchKnownFields(afterOverrides, knownFields, vars);
        reusedKnown = matched.reused;
        remainingFields = matched.drifted;
    }

    if (remainingFields.length === 0) {
        const combined = enforceDateTimeGuards(fields, { ...reusedKnown, ...resolvedOverrides });
        return {
            values: combined,
            reusedCount: Object.keys(reusedKnown).length,
            generatedCount: 0,
            llmStats: null
        };
    }

    const schema = buildFormJsonSchema(remainingFields);
    const fieldLines = remainingFields.map((f) => {
        if (f.tag === "select" && f.options?.length) {
            const sample = f.options.slice(0, 10).map((o) => `${o.value}=${o.text}`).join(" | ");
            return `- ${f.key} (${f.label}, select): ${sample}`;
        }
        const cleanPlaceholder = f.placeholder && !/^(enter|add|select|choose|search|type|0\.00 or)\b/i.test(f.placeholder)
            ? `example:"${f.placeholder}"`
            : "";
        const meta = [f.type, f.inputMode === "decimal" ? "decimal number" : "", cleanPlaceholder, f.maxLength ? `max:${f.maxLength}` : ""]
            .filter(Boolean)
            .join(", ");
        return `- ${f.key} (${f.label}, ${meta})`;
    });

    const phNow = getPhilippineNowText();
    const promptBody = repairContext
        ? `Scenario: ${scenario || "valid realistic business test data"}\n\nPrevious values you submitted:\n${JSON.stringify(repairContext.previousValues, null, 2)}\n\nVALIDATION ERROR FROM THE APP:\n${repairContext.errorMessage}\n\nFix the invalid field(s) to satisfy the error above while keeping valid fields intact.\n\nFields:\n${fieldLines.join("\n")}`
        : `Scenario: ${scenario || "valid realistic business test data (Philippines)"}\n\nFields:\n${fieldLines.join("\n")}`;

    const res = await callOllamaJson({
        ...ollamaOpts,
        temperature: repairContext ? 0.1 : 0.3,
        system: `Generate valid test values for web form fields. Current Philippine time is ${phNow}. Respect max lengths strictly, use exact option IDs for selects, YYYY-MM-DD for dates, HH:MM (24-hour) for times, and never copy placeholders verbatim. Return ONLY JSON.`,
        prompt: promptBody,
        schema
    });

    const combined = enforceDateTimeGuards(fields, { ...reusedKnown, ...res.data, ...resolvedOverrides });
    return {
        values: combined,
        reusedCount: Object.keys(reusedKnown).length,
        generatedCount: remainingFields.length,
        llmStats: res.stats
    };
}

async function applyFormValues(cdp, values) {
    return cdp.evaluate(`((vals) => {
        const applied = [];
        const nativeInputSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
        const nativeTextAreaSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set;

        for (const [key, rawVal] of Object.entries(vals)) {
            const el = document.querySelector(\`[data-gh-field-key="\${CSS.escape(key)}"]\`);
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
                    } catch {}
                }
            } else if (tag === "textarea") {
                if (nativeTextAreaSetter) nativeTextAreaSetter.call(el, String(rawVal));
                else el.value = String(rawVal);
                el.dispatchEvent(new Event("input", { bubbles: true }));
                el.dispatchEvent(new Event("change", { bubbles: true }));
            } else {
                const strVal = String(rawVal);
                if (nativeInputSetter) nativeInputSetter.call(el, strVal);
                else el.value = strVal;
                el.dispatchEvent(new Event("input", { bubbles: true }));
                el.dispatchEvent(new Event("change", { bubbles: true }));
            }
            applied.push({ key, value: el.tagName === "SELECT" ? el.value : rawVal });
        }
        return applied;
    })(${JSON.stringify(values)})`);
}

async function readPageFeedback(cdp) {
    return cdp.evaluate(`(() => {
        const validationErrors = [...document.querySelectorAll(".field-validation-error, .validation-summary-errors li, .invalid-feedback")]
            .map((el) => (el.innerText || "").trim())
            .filter(Boolean)
            .slice(0, 10);
        const swalTitle = document.querySelector(".swal2-popup .swal2-title")?.innerText?.trim() || "";
        const swalText = document.querySelector(".swal2-popup .swal2-html-container")?.innerText?.trim() || "";
        const toastText = [...document.querySelectorAll(".toast-body, .toast-message, .alert")]
            .map((el) => (el.innerText || "").replace(/\\s+/g, " ").trim())
            .filter(Boolean)
            .slice(0, 5);
        const bodySample = (document.body?.innerText || "").slice(0, 1200);
        const hasServerError = /An unhandled exception occurred|HTTP ERROR 500|Server Error in/i.test(bodySample);
        return {
            url: location.href,
            title: document.title || "",
            hasServerError,
            validationErrors,
            swal: (swalTitle || swalText) ? { title: swalTitle, text: swalText } : null,
            alerts: toastText
        };
    })()`);
}

// ============================================================================
// Standalone Local Ollama Benchmark (--benchmark)
// ============================================================================

async function runBenchmark(opts) {
    console.log(`[benchmark] Testing Option 1 Guided Hands primitives with Ollama (${opts.model} @ ${opts.ollamaUrl})...\n`);
    const ollamaOpts = { ollamaUrl: opts.ollamaUrl, model: opts.model };

    // Test 1: Fuzzy button selection (when exact match didn't hit)
    const sampleButtons = [
        { id: 1, tag: "a", text: "Dashboard", domId: "navDash" },
        { id: 2, tag: "button", text: "Export Excel", domId: "btnExport" },
        { id: 3, tag: "button", text: "New Service Billing", domId: "btnCreateBilling" },
        { id: 4, tag: "button", text: "Reset Filters", domId: "btnReset" },
        { id: 5, tag: "button", text: "Archive Selected", domId: "btnArchive" }
    ];
    const clickPick = await resolveClickTarget(sampleButtons, "add a billing entry", ollamaOpts);
    console.log("1) Fuzzy Click Match:");
    console.log(`   Target: "add a billing entry" -> picked [${clickPick.matched?.id}] "${clickPick.matched?.text}" (via=${clickPick.via})`);
    if (clickPick.llmStats) {
        console.log(`   Latency: ${clickPick.llmStats.durationMs}ms | prompt=${clickPick.llmStats.promptTokens} tok | out=${clickPick.llmStats.outputTokens} tok\n`);
    }

    // Test 2: Full form mock data generation with strict JSON Schema + select enums
    const sampleFields = [
        { key: "CustomerName", tag: "input", type: "text", label: "Customer / Shipping Line", required: true, maxLength: 80 },
        { key: "VesselName", tag: "input", type: "text", label: "Vessel Name", required: true, maxLength: 50 },
        {
            key: "PortId",
            tag: "select",
            type: "select",
            label: "Port of Operation",
            required: true,
            options: [
                { value: "1", text: "Batangas Port" },
                { value: "2", text: "Subic Bay Freeport" },
                { value: "3", text: "Manila South Harbor" }
            ]
        },
        { key: "ServiceDate", tag: "input", type: "date", label: "Service Date", required: true },
        { key: "Amount", tag: "input", type: "number", label: "Total Amount (PHP)", required: true },
        { key: "ContactEmail", tag: "input", type: "email", label: "Billing Email", required: true },
        { key: "IsVatExempt", tag: "input", type: "checkbox", label: "VAT Exempt", required: false },
        { key: "Remarks", tag: "textarea", type: "textarea", label: "Remarks / Notes", required: false, maxLength: 150 }
    ];

    const formGen = await generateFormValues(
        sampleFields,
        "valid realistic Philippine maritime tugboat service billing record",
        null,
        ollamaOpts
    );
    console.log("2) Schema-Locked Form Fill Generation:");
    console.log(JSON.stringify(formGen.values, null, 2));
    console.log(`   Latency: ${formGen.llmStats.durationMs}ms | prompt=${formGen.llmStats.promptTokens} tok | out=${formGen.llmStats.outputTokens} tok`);
}

// ============================================================================
// Chrome Extension Bridge Driver (--extension, zero-prompt Codex-style tab)
// ============================================================================

class ExtensionDriver {
    constructor(bridgeUrl) {
        this.bridgeUrl = bridgeUrl;
        this.tabId = null;
    }

    async rpc(action, params = {}, timeoutMs = 30000) {
        const resp = await fetch(`${this.bridgeUrl}/rpc`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ action, params: { ...params, ...(this.tabId ? { tabId: this.tabId } : {}) }, timeoutMs })
        });
        const data = await resp.json();
        if (!resp.ok || !data.ok) {
            throw new Error(data.error || `Extension bridge HTTP ${resp.status}`);
        }
        return data.result;
    }

    async ensureBridgeRunning() {
        const fetchStatus = async () => {
            try {
                const resp = await fetch(`${this.bridgeUrl}/status`);
                return resp.ok ? await resp.json() : null;
            } catch {
                return null;
            }
        };

        let status = await fetchStatus();
        if (!status) {
            const bridgeScript = path.join(path.dirname(fileURLToPath(import.meta.url)), "guided-hands-bridge.mjs");
            if (fs.existsSync(bridgeScript)) {
                console.log("[guided-hands] Bridge not running; auto-starting guided-hands-bridge.mjs...");
                const proc = spawn(process.execPath, [bridgeScript], {
                    detached: true,
                    windowsHide: true,
                    stdio: "ignore"
                });
                proc.unref();
                const startDeadline = Date.now() + 4000;
                while (Date.now() < startDeadline) {
                    await new Promise((r) => setTimeout(r, 250));
                    status = await fetchStatus();
                    if (status) break;
                }
            }
        }
        if (!status) {
            throw new Error(`Guided Hands bridge is not running at ${this.bridgeUrl}. Start it with: node .agents/skills/proof-verify/scripts/guided-hands-bridge.mjs`);
        }
        if (!status.extensionConnected) {
            // Give the extension a few seconds to auto-reconnect if the bridge just started
            const extDeadline = Date.now() + 5000;
            while (Date.now() < extDeadline) {
                await new Promise((r) => setTimeout(r, 400));
                status = await fetchStatus();
                if (status?.extensionConnected) break;
            }
        }
        if (!status?.extensionConnected) {
            throw new Error("Guided Hands Chrome extension is not connected to the local bridge. Click the Guided Hands extension icon in Chrome -> Reconnect (or load .agents/skills/proof-verify/extension in chrome://extensions).");
        }
    }

    async open(url, active) {
        await this.ensureBridgeRunning();
        const res = await this.rpc("open_tab", { url, active, status: "Guided Hands starting..." });
        this.tabId = res.tabId;
    }

    async navigate(url) {
        const res = await this.rpc("navigate", { url });
        if (res.tabId) this.tabId = res.tabId;
        return res.url;
    }

    async waitLoad() {
        return this.rpc("wait_load");
    }

    async extractClickables(scopeSelector = "") {
        return this.rpc("extract_clickables", { scopeSelector });
    }

    async clickSelector(selector) {
        const res = await this.rpc("click", { selector });
        return Boolean(res?.clicked);
    }

    async extractFields(scopeSelector = "", onlyEmpty = false) {
        return this.rpc("extract_fields", { scopeSelector, onlyEmpty });
    }

    async applyValues(values) {
        return this.rpc("apply_values", { values });
    }

    async readFeedback() {
        return this.rpc("read_feedback");
    }

    async consumeTelemetry() {
        return this.rpc("consume_telemetry");
    }

    async checkSelector(selector) {
        const res = await this.rpc("check_selector", { selector });
        return Boolean(res?.found);
    }

    async autoLogin(username, password) {
        const res = await this.rpc("auto_login", { username, password });
        return Boolean(res?.submitted);
    }

    async setIndicator(text, mode = "active", highlight = "") {
        await this.rpc("set_indicator", { text, mode, highlight }).catch(() => {});
    }

    async captureScreenshot(filePath) {
        const res = await this.rpc("screenshot");
        if (res?.dataUrl && res.dataUrl.startsWith("data:image/png;base64,")) {
            fs.writeFileSync(filePath, Buffer.from(res.dataUrl.slice("data:image/png;base64,".length), "base64"));
            return true;
        }
        return false;
    }
}

// ============================================================================
// Main Runner
// ============================================================================

async function main() {
    const opts = parseArgs(process.argv.slice(2));
    if (opts.help) {
        printUsage();
        process.exit(0);
    }

    if (opts.benchmark) {
        await runBenchmark(opts);
        return;
    }

    const repoRoot = getRepoRoot();
    const vars = buildTemplateVars(opts.vars);
    let loadedPlaybook = null;
    const playbookPath = opts.playbook ? resolvePlaybookPath(repoRoot, opts.playbook) : "";
    const savePlaybookPath = opts.savePlaybook
        ? resolvePlaybookPath(repoRoot, opts.savePlaybook)
        : (playbookPath && !opts.noUpdatePlaybook ? playbookPath : "");

    if (playbookPath) {
        if (!fs.existsSync(playbookPath)) {
            throw new Error(`Playbook not found: ${playbookPath}`);
        }
        loadedPlaybook = JSON.parse(fs.readFileSync(playbookPath, "utf8"));
        if (!opts.url && loadedPlaybook.url) opts.url = loadedPlaybook.url;
        if (opts.steps.length === 0 && Array.isArray(loadedPlaybook.steps)) {
            opts.steps = JSON.parse(JSON.stringify(loadedPlaybook.steps));
        }
        console.log(`[guided-hands] Loaded playbook: ${path.basename(playbookPath)} (${opts.steps.length} step(s))`);
    }

    if (!opts.url) {
        throw new Error("Missing --url (or use --playbook / --benchmark).");
    }

    const ignoreCheck = runGit(repoRoot, ["check-ignore", "-q", "artifacts/proof-verify"]);
    if (!ignoreCheck.ok) {
        throw new Error("Security gate: artifacts/proof-verify is not gitignored; refusing to persist browser cookies.");
    }

    const runId = new Date().toISOString().replace(/[:.]/g, "-");
    const runDir = path.join(repoRoot, "artifacts", "proof-verify", "guided-hands", runId);
    fs.mkdirSync(runDir, { recursive: true });
    const userSlug = opts.user ? opts.user.replace(/[^a-zA-Z0-9_-]+/g, "_").toLowerCase() : "";
    const profileDir = path.join(
        repoRoot,
        "artifacts",
        "proof-verify",
        userSlug ? `.browser-profile-${userSlug}` : ".browser-profile"
    );

    const ollamaOpts = { ollamaUrl: opts.ollamaUrl, model: opts.model };
    const expandedStartUrl = expandTemplate(opts.url, vars);
    const startUrl = expandedStartUrl.startsWith("http")
        ? expandedStartUrl
        : `${opts.baseUrl}${expandedStartUrl.startsWith("/") ? "" : "/"}${expandedStartUrl}`;

    let startedProc = null;
    let browserProc = null;
    let cdp = null;
    let ext = null;

    const report = {
        timestamp: new Date().toISOString(),
        model: opts.model,
        transport: opts.extension ? "chrome-extension" : "cdp",
        startUrl,
        passed: true,
        steps: [],
        screenshots: [],
        errors: []
    };

    try {
        const srv = await ensureAppRunning(repoRoot, opts.baseUrl, opts.port, opts.noAutostart);
        startedProc = srv.startedProc;

        const pageErrors = [];
        const consoleErrors = [];
        const failedRequests = [];

        if (opts.extension) {
            ext = new ExtensionDriver(opts.bridgeUrl);
            await ext.open(startUrl, !opts.background);
        } else {
            const browser = await launchBrowserWithCdp(profileDir, {
                headless: !opts.headed,
                devtools: opts.devtools,
                keepOpen: opts.keepOpen,
                cdpPort: opts.cdpPort,
                attachChrome: opts.attachChrome
            });
            browserProc = browser.proc;
            cdp = browser.cdp;

            cdp.on("Runtime.exceptionThrown", (p) => {
                pageErrors.push(p.exceptionDetails?.exception?.description || p.exceptionDetails?.text || "Uncaught exception");
            });
            cdp.on("Runtime.consoleAPICalled", (p) => {
                if (p.type === "error") {
                    consoleErrors.push((p.args || []).map((a) => a.value ?? a.description ?? "").join(" ") || "console.error");
                }
            });
            cdp.on("Network.responseReceived", (p) => {
                const url = p.response?.url || "";
                const status = p.response?.status || 0;
                if (status >= 400 && !url.includes("favicon") && (url.startsWith(opts.baseUrl) || status >= 500)) {
                    failedRequests.push({ url, status });
                }
            });

            await navigateAndWait(cdp, startUrl);
        }

        const getFeedback = () => (ext ? ext.readFeedback() : readPageFeedback(cdp));
        const doNavigate = (url) => (ext ? ext.navigate(url) : navigateAndWait(cdp, url));
        const doExtractClickables = (scope) => (ext ? ext.extractClickables(scope) : extractClickables(cdp, scope));
        const doExtractFields = (scope, onlyEmpty) => (ext ? ext.extractFields(scope, onlyEmpty) : extractFormFields(cdp, scope, onlyEmpty));
        const doApplyValues = (vals) => (ext ? ext.applyValues(vals) : applyFormValues(cdp, vals));
        const doCheckSelector = (sel) => (ext
            ? ext.checkSelector(sel)
            : cdp.evaluate(`Boolean(document.querySelector(${JSON.stringify(sel)}))`));
        const doClickSelector = (sel) => (ext
            ? ext.clickSelector(sel)
            : cdp.evaluate(`(() => {
                const el = document.querySelector(${JSON.stringify(sel)});
                if (!el) return false;
                el.scrollIntoView({ block: "center" });
                el.click();
                return true;
            })()`));
        const doHud = (text, mode = "active", highlight = "") => {
            if (ext) return ext.setIndicator(text, mode, highlight);
            if (opts.headed && cdp) return updateVisualHud(cdp, text, highlight);
            return Promise.resolve();
        };
        const doScreenshot = async (shotPath) => {
            if (ext) {
                const saved = await ext.captureScreenshot(shotPath);
                if (saved) report.screenshots.push(shotPath);
                return saved ? shotPath : null;
            }
            const img = await cdp.send("Page.captureScreenshot", { format: "png" });
            fs.writeFileSync(shotPath, Buffer.from(img.data, "base64"));
            report.screenshots.push(shotPath);
            return shotPath;
        };
        const dismissInfoSwalIfBlocking = async () => {
            const fb = await getFeedback();
            if (!fb.swal || fb.swal.isError) return false;
            const { items } = await doExtractClickables(".swal2-popup:not([style*='display: none'])");
            if (items.length === 1 && /^ok$/i.test(items[0].text.trim())) {
                await doClickSelector(".swal2-confirm").catch(() => {});
                await new Promise((r) => setTimeout(r, 250));
                return true;
            }
            return false;
        };

        const initFb = await getFeedback();
        const curPath = initFb.pathname || new URL(initFb.url).pathname;
        if (!opts.url.toLowerCase().includes("/identity/account/login") && String(curPath).toLowerCase().includes("/identity/account/login")) {
            const loggedIn = ext
                ? await ext.autoLogin(opts.user, opts.pass)
                : await attemptAutoLogin(cdp, opts.user, opts.pass);
            if (!loggedIn) {
                throw new Error("AUTH_REQUIRED: redirected to Login. Run proof-verify --interactive-login first or set IBS_VERIFY_USER & IBS_VERIFY_PASS.");
            }
            await doNavigate(startUrl);
        }

        if (opts.inspect) {
            const clickables = await doExtractClickables("");
            const form = await doExtractFields("", false);
            const fb = await getFeedback();
            console.log(JSON.stringify({ url: fb.url, clickables, form }, null, 2));
            await doHud("Guided Hands inspect complete", "done");
            return;
        }

        const runStartMs = Date.now();
        for (let i = 0; i < opts.steps.length; i++) {
            const step = opts.steps[i];
            const stepNum = i + 1;
            const errStart = { page: pageErrors.length, console: consoleErrors.length, net: failedRequests.length };
            const stepResult = { step: stepNum, action: step.action, ok: true, detail: {} };

            const stepLabel = `[Step ${stepNum}/${opts.steps.length}] ${step.action} ${step.target || step.scenario || step.url || step.text || ""}`;
            console.log(stepLabel);
            const elapsed = ((Date.now() - runStartMs) / 1000).toFixed(1);
            await doHud(`Guided Hands (${elapsed}s) · ${stepLabel.slice(0, 88)}`, "active");

            if (step.action === "navigate") {
                const rawUrl = expandTemplate(step.url, vars);
                const dest = rawUrl.startsWith("http") ? rawUrl : `${opts.baseUrl}${rawUrl.startsWith("/") ? "" : "/"}${rawUrl}`;
                await doNavigate(dest);
                stepResult.detail.url = (await getFeedback()).url;
            } else if (step.action === "login") {
                const loginUser = step.username || opts.user;
                const loginPass = step.password || opts.pass;
                const ok = ext
                    ? await ext.autoLogin(loginUser, loginPass)
                    : await attemptAutoLogin(cdp, loginUser, loginPass);
                if (!ok) throw new Error("Login step failed: login form not found or authentication did not redirect.");
                stepResult.detail = { username: loginUser, url: (await getFeedback()).url };
            } else if (step.action === "wait") {
                if (step.selector) {
                    const deadline = Date.now() + (step.timeoutMs || 10000);
                    let found = false;
                    while (Date.now() < deadline) {
                        found = await doCheckSelector(step.selector);
                        if (found) break;
                        await new Promise((r) => setTimeout(r, 200));
                    }
                    if (!found) throw new Error(`Timed out waiting for selector: ${step.selector}`);
                    stepResult.detail.selector = step.selector;
                } else {
                    await new Promise((r) => setTimeout(r, step.ms || 800));
                    stepResult.detail.ms = step.ms || 800;
                }
            } else if (step.action === "click") {
                const targetingSwal = Boolean(step.scope && step.scope.includes("swal2"));
                if (!targetingSwal) {
                    await dismissInfoSwalIfBlocking();
                }
                if (step.selector) {
                    if (opts.headed) {
                        await doHud(`Guided Hands (${elapsed}s) · Clicking ${step.selector}`, "active", step.selector);
                        await new Promise((r) => setTimeout(r, Math.max(200, opts.slowMo)));
                    }
                    const clicked = await doClickSelector(step.selector);
                    if (!clicked && !step.optional) throw new Error(`Selector not found: ${step.selector}`);
                    stepResult.detail = { selector: step.selector, via: "selector", skipped: !clicked };
                } else {
                    let { inModal, items } = await doExtractClickables(step.scope || "");
                    // Briefly wait for late-rendering SweetAlert2 fade-in when explicitly targeting a popup
                    if (targetingSwal && items.length === 0) {
                        const waitDeadline = Date.now() + (step.waitForPopupMs ?? 700);
                        while (items.length === 0 && Date.now() < waitDeadline) {
                            await new Promise((r) => setTimeout(r, 175));
                            ({ inModal, items } = await doExtractClickables(step.scope || ""));
                        }
                    }
                    if (items.length === 0) {
                        if (step.optional) {
                            stepResult.detail = { target: step.target, skipped: true };
                        } else {
                            throw new Error(`No visible clickable elements found for "${step.target}".`);
                        }
                    } else {
                        const pick = await resolveClickTarget(items, step.target, ollamaOpts, Boolean(step.optional));
                        if (!pick.matched) {
                            if (step.optional) {
                                stepResult.detail = { target: step.target, skipped: true };
                            } else {
                                throw new Error(`Could not match click target "${step.target}" among ${items.length} elements (${pick.reason || "no match"}).`);
                            }
                        } else {
                            const clickSel = `[data-gh-click-id="${pick.matched.id}"]`;
                            if (opts.headed) {
                                const nowSec = ((Date.now() - runStartMs) / 1000).toFixed(1);
                                await doHud(`Guided Hands (${nowSec}s) · Clicking "${pick.matched.text}" (${pick.via})`, "active", clickSel);
                                await new Promise((r) => setTimeout(r, Math.max(250, opts.slowMo)));
                            }
                            const clicked = await doClickSelector(clickSel);
                            if (!clicked) throw new Error("Matched element disappeared before click.");
                            stepResult.detail = {
                                inModal,
                                target: step.target,
                                matched: pick.matched,
                                via: pick.via,
                                llmStats: pick.llmStats
                            };
                        }
                    }
                }
                if (!stepResult.detail.skipped) {
                    await new Promise((r) => setTimeout(r, step.waitAfterMs || 700));
                    if (ext && step.waitForNavigation) {
                        await ext.waitLoad();
                    }
                }
            } else if (step.action === "fill_form" || step.action === "fill_and_submit") {
                const maxRetries = step.action === "fill_and_submit" ? (step.maxRetries ?? 2) : 0;
                const attempts = [];
                let repairContext = null;
                let succeeded = false;

                for (let attempt = 0; attempt <= maxRetries; attempt++) {
                    const { inModal, fields } = await doExtractFields(step.scope || "", Boolean(step.onlyEmpty) && attempt === 0);
                    if (fields.length === 0) {
                        if (step.optional && attempt === 0) {
                            attempts.push({ attempt: 1, skipped: true, fieldCount: 0 });
                            succeeded = true;
                            break;
                        }
                        throw new Error("No visible editable form fields found.");
                    }
                    if (attempt > 0) {
                        const nowSec = ((Date.now() - runStartMs) / 1000).toFixed(1);
                        await doHud(
                            `Guided Hands (${nowSec}s) · Self-healing retry ${attempt}/${maxRetries}: ${repairContext.errorMessage.slice(0, 55)}`,
                            "active"
                        );
                    }
                    const mergedOverrides = Array.isArray(step.overrides)
                        ? step.overrides
                        : { ...(step.overrides || {}), ...opts.cliOverrides };
                    const gen = await generateFormValues(
                        fields,
                        step.scenario,
                        mergedOverrides,
                        ollamaOpts,
                        repairContext,
                        step.knownFields || null,
                        vars
                    );
                    const applied = await doApplyValues(gen.values);
                    const shot = await doScreenshot(path.join(runDir, `step-${stepNum}-attempt-${attempt + 1}-filled.png`));
                    const attemptRecord = {
                        attempt: attempt + 1,
                        inModal,
                        fieldCount: fields.length,
                        appliedCount: applied.length,
                        reusedFromPlaybook: gen.reusedCount || 0,
                        generatedByOllama: gen.generatedCount || 0,
                        values: gen.values,
                        llmStats: gen.llmStats,
                        screenshot: shot
                    };
                    if (gen.reusedCount > 0 || gen.generatedCount === 0) {
                        console.log(`  [playbook] Reused ${gen.reusedCount}/${fields.length} field(s), Ollama generated ${gen.generatedCount}`);
                    }
                    step._capturedKnownFields = snapshotKnownFields(fields, gen.values, step.knownFields, step.overrides);

                    if (step.action === "fill_form") {
                        attempts.push(attemptRecord);
                        succeeded = true;
                        await new Promise((r) => setTimeout(r, step.waitAfterMs || 600));
                        break;
                    }

                    // Submit the form and check for real-time validation / SweetAlert errors
                    const submitSel = step.submitSelector || "button[type='submit']";
                    await doHud(`Guided Hands · Submitting (${attempt + 1}/${maxRetries + 1})`, "active", submitSel);
                    await new Promise((r) => setTimeout(r, Math.max(200, opts.slowMo)));
                    const clicked = await doClickSelector(submitSel);
                    if (!clicked) throw new Error(`Submit button not found: ${submitSel}`);
                    await new Promise((r) => setTimeout(r, step.waitAfterMs || 1800));

                    const fb = await getFeedback();
                    const errParts = [];
                    if (fb.swal && fb.swal.isError !== false && /error|invalid|required|failed|could not|cannot|future|must be/i.test(`${fb.swal.title} ${fb.swal.text}`)) {
                        errParts.push(`${fb.swal.title}: ${fb.swal.text}`.trim());
                    }
                    const realValidationErrors = (fb.validationErrors || []).filter(
                        (msg) => !/^(cancel\s+cancel\s+service|delete_forever\s+void\s+ticket)$/i.test(msg.trim())
                    );
                    if (realValidationErrors.length) {
                        errParts.push(...realValidationErrors);
                    }

                    if (errParts.length === 0) {
                        // Confirm initial dialog (if any) and handle async loading -> success/error follow-up popup
                        if (fb.swal) {
                            await doClickSelector(".swal2-confirm").catch(() => {});
                            const pollStart = Date.now();
                            while (Date.now() - pollStart < 6000) {
                                await new Promise((r) => setTimeout(r, 250));
                                const fb2 = await getFeedback().catch(() => null);
                                if (!fb2?.swal) {
                                    if (Date.now() - pollStart >= 1200) break;
                                    continue;
                                }
                                const { items: swalButtons } = await doExtractClickables(".swal2-popup:not([style*='display: none'])").catch(() => ({ items: [] }));
                                if (swalButtons.length === 0) {
                                    continue; // Loading spinner popup (ModernAlert.showLoading)
                                }
                                if (fb2.swal.isError !== false && /error|invalid|required|failed|could not|cannot|future|must be/i.test(`${fb2.swal.title} ${fb2.swal.text}`)) {
                                    errParts.push(`${fb2.swal.title}: ${fb2.swal.text}`.trim());
                                    break;
                                }
                                await doClickSelector(".swal2-confirm").catch(() => {});
                                await new Promise((r) => setTimeout(r, 600));
                                break;
                            }
                        }
                        if (errParts.length === 0) {
                            if (ext && step.waitForNavigation) {
                                await ext.waitLoad();
                            }
                            attemptRecord.submitted = true;
                            attempts.push(attemptRecord);
                            succeeded = true;
                            break;
                        }
                    }

                    const errorMessage = errParts.join(" | ");
                    console.log(`  [self-heal] Attempt ${attempt + 1} rejected: ${errorMessage}`);
                    attemptRecord.rejectedWith = errorMessage;
                    attempts.push(attemptRecord);

                    // Dismiss error SweetAlert so the form can be corrected
                    if (fb.swal) {
                        await doClickSelector(".swal2-confirm").catch(() => {});
                        await new Promise((r) => setTimeout(r, 400));
                    }
                    repairContext = { previousValues: gen.values, errorMessage };
                }

                stepResult.detail = step.action === "fill_form" ? attempts[0] : { attempts, retries: attempts.length - 1 };
                if (!succeeded) {
                    stepResult.ok = false;
                    report.steps.push(stepResult);
                    const lastErr = attempts[attempts.length - 1]?.rejectedWith || "Form submission rejected";
                    throw new Error(`Form failed after ${attempts.length} attempt(s): ${lastErr}`);
                }
            } else if (step.action === "capture_url") {
                const feedback = await getFeedback();
                const regex = new RegExp(step.pattern, "i");
                const match = feedback.url.match(regex);
                if (!match || !match[1]) {
                    if (step.optional) {
                        stepResult.detail = { url: feedback.url, var: step.var, captured: null, skipped: true };
                    } else {
                        throw new Error(`Step ${stepNum} capture_url failed: URL "${feedback.url}" did not match /${step.pattern}/i.`);
                    }
                } else {
                    vars[step.var] = match[1];
                    console.log(`  [capture] {${step.var}} = "${match[1]}" (from ${feedback.url})`);
                    stepResult.detail = { url: feedback.url, var: step.var, captured: match[1] };
                }
            } else if (step.action === "assert") {
                const feedback = await getFeedback();
                const bodyText = ext ? (feedback.bodySample || "") : await cdp.evaluate("(document.body?.innerText || '')");
                const expectedUrl = expandTemplate(step.urlIncludes || "", vars);
                const expectedText = expandTemplate(step.text || "", vars);
                if (expectedUrl && !feedback.url.toLowerCase().includes(expectedUrl.toLowerCase())) {
                    stepResult.ok = false;
                    report.errors.push(`Step ${stepNum} assert failed: URL "${feedback.url}" does not include "${expectedUrl}".`);
                }
                if (expectedText && !bodyText.toLowerCase().includes(expectedText.toLowerCase())) {
                    stepResult.ok = false;
                    report.errors.push(`Step ${stepNum} assert failed: text "${expectedText}" not found on page.`);
                }
                if (step.noValidationErrors && feedback.validationErrors.length > 0) {
                    stepResult.ok = false;
                    report.errors.push(`Step ${stepNum} assert failed: validation errors present: ${feedback.validationErrors.join("; ")}`);
                }
                stepResult.detail = feedback;
            } else if (step.action === "screenshot") {
                const safeName = (step.name || `step-${stepNum}`).replace(/[^a-zA-Z0-9_-]+/g, "-").toLowerCase();
                stepResult.detail.screenshot = await doScreenshot(path.join(runDir, `${safeName}.png`));
            } else {
                throw new Error(`Unknown step action: ${step.action}`);
            }

            let newPageErrs;
            let newConsoleErrs;
            let newNetErrs;
            if (ext) {
                const tel = await ext.consumeTelemetry().catch(() => ({ pageErrors: [], consoleErrors: [], failedRequests: [] }));
                newPageErrs = tel.pageErrors || [];
                newConsoleErrs = tel.consoleErrors || [];
                newNetErrs = tel.failedRequests || [];
            } else {
                newPageErrs = pageErrors.slice(errStart.page);
                newConsoleErrs = consoleErrors.slice(errStart.console);
                newNetErrs = failedRequests.slice(errStart.net);
            }
            if (newPageErrs.length || newNetErrs.length) {
                stepResult.ok = false;
                if (newPageErrs.length) report.errors.push(`Step ${stepNum} page error: ${newPageErrs[0]}`);
                if (newNetErrs.length) report.errors.push(`Step ${stepNum} failed HTTP: ${newNetErrs[0].status} ${newNetErrs[0].url}`);
            }
            stepResult.telemetry = {
                pageErrors: newPageErrs,
                consoleErrors: newConsoleErrs,
                failedRequests: newNetErrs
            };
            report.steps.push(stepResult);
            if (opts.slowMo > 0) {
                await new Promise((r) => setTimeout(r, opts.slowMo));
            }
        }

        const totalSec = ((Date.now() - runStartMs) / 1000).toFixed(1);
        await doHud(
            `Guided Hands DONE in ${totalSec}s · ${report.steps.length} steps, ${report.errors.length} errors`,
            report.errors.length === 0 ? "done" : "error"
        );

        await doScreenshot(path.join(runDir, "final.png"));
        report.finalFeedback = await getFeedback();
        if (report.finalFeedback.hasServerError) {
            report.errors.push("Final page shows an ASP.NET server error.");
        }
    } catch (err) {
        report.errors.push(err.message);
        if (ext) {
            await ext.setIndicator(`Guided Hands ERROR · ${err.message.slice(0, 70)}`, "error").catch(() => {});
        }
    } finally {
        cdp?.close();
        if (!opts.keepOpen) {
            killProcessTree(browserProc);
        } else if (browserProc) {
            browserProc.stderr?.destroy();
            browserProc.unref?.();
            console.log("[guided-hands] --keep-open set: leaving Chrome open for inspection.");
        }
        if (startedProc) killProcessTree(startedProc);
    }

    report.passed = report.errors.length === 0;

    if (report.passed && savePlaybookPath) {
        fs.mkdirSync(path.dirname(savePlaybookPath), { recursive: true });
        const updatedSteps = opts.steps.map((s) => {
            const clean = { ...s };
            if (clean._capturedKnownFields) {
                clean.knownFields = clean._capturedKnownFields;
            }
            delete clean._capturedKnownFields;
            return clean;
        });
        const playbookDoc = {
            name: loadedPlaybook?.name || path.basename(savePlaybookPath, ".json"),
            description: loadedPlaybook?.description || `Auto-recorded Guided Hands playbook for ${opts.url}`,
            url: loadedPlaybook?.url || opts.url,
            notes: loadedPlaybook?.notes || [],
            lastPassedAt: report.timestamp,
            steps: updatedSteps
        };
        fs.writeFileSync(savePlaybookPath, JSON.stringify(playbookDoc, null, 2) + "\n", "utf8");
        console.log(`[guided-hands] Playbook saved/updated: ${savePlaybookPath}`);
    }

    const latestPath = path.join(repoRoot, "artifacts", "proof-verify", "latest-guided-hands.json");
    const runPath = path.join(runDir, "report.json");
    const jsonOut = JSON.stringify(report, null, 2) + "\n";
    fs.writeFileSync(runPath, jsonOut, "utf8");
    fs.writeFileSync(latestPath, jsonOut, "utf8");

    console.log(`\n[guided-hands] OVERALL: ${report.passed ? "PASS" : "FAIL"} (${report.steps.length} step(s), ${report.errors.length} error(s))`);
    console.log(`[guided-hands] Report: ${latestPath}`);
    if (report.errors.length > 0) {
        for (const e of report.errors) console.error(`  - ERROR: ${e}`);
    }
    process.exit(report.passed ? 0 : 1);
}

main().catch((err) => {
    console.error(`[guided-hands] Fatal: ${err.stack || err.message}`);
    process.exit(1);
});
