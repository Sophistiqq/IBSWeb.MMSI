#!/usr/bin/env node

import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawn, execFileSync } from "node:child_process";

const DEFAULT_PORT = 5073;
const DEFAULT_OLLAMA_URL = process.env.OLLAMA_HOST || "http://localhost:11434";
const DEFAULT_MODEL = process.env.GUIDED_HANDS_MODEL || "qwen2.5-coder:3b";
const DEFAULT_OPENCODE_MODELS = [
    "opencode/deepseek-v4.1-flash-free",
    "opencode/nemotron-3.5-lightning-free",
    "opencode/mimo-v2.6-flash-free",
    "opencode/flint-40b-free"
];
const DESTRUCTIVE_TARGET_RE = /\b(delete|void|reverse|finalize|cancel\s+service|remove|destroy|purge|drop|reset\s+all)\b/i;

const BROWSER_CANDIDATES = [
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"
];

function printUsage() {
    console.log(`Usage: node .agents/skills/proof-verify/scripts/guided-hands.mjs [options]

Guided Hands: 3-tier self-healing browser runner (Tier 0 deterministic + Tier 1 local Ollama + Tier 2 free OpenCode).

Options:
  --url <route|url>        Starting route or URL (e.g. /MSAP/Billing/Index)
  --steps <json>           Inline JSON array of step objects
  --steps-file <path>      Path to JSON file containing step objects (or { url, steps })
  --playbook <a,b,c>       Replay one or more comma-separated module playbooks (e.g. schedule/create,rates/approve,schedule/approve) or a preset name
  --chain <a,b,c>          Alias for --playbook (comma-separated module chain)
  --preset <name>          Expand a named flow preset from .agents/skills/proof-verify/playbooks/presets.json
  --save-playbook <name>   Save/update a golden playbook under .agents/skills/proof-verify/playbooks/ on PASS
  --no-update-playbook     When using --playbook, do not auto-update or auto-patch the playbook file on PASS
  --resume                 Resume the last run from artifacts/proof-verify/last-checkpoint.json (restoring vars & URL)
  --from-step <number>     Start execution at 1-based step N (reuses matching checkpoint vars if available)
  --heal-engine <mode>     Self-healing mode: auto | ollama | opencode | off (default: auto)
  --opencode-model <id>    Primary free OpenCode model for Tier 2 recovery / --goal mode
  --goal <text>            Autonomous Observe->Think->Act mode driven by free OpenCode (compiles to --save-playbook)
  --max-turns <number>     Max turns for --goal mode (default: 12)
  --override <Key=Value>   Repeatable field override for replay (e.g. --override PortId=SUBIC)
  --var <name=value>       Repeatable template variable (built-in: {today}, {tomorrow}, {rand})
  --step <shorthand>       Repeatable step shorthand:
                             "click: Add Billing"
                             "select: Job Order=14"
                             "upload: pdf" or "upload: path/to/file.pdf"
                             "login: FINANCE"
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
  --model <name>           Ollama model name (default: qwen2.5-coder:3b)
  --ollama-url <url>       Ollama base URL (default: http://localhost:11434)
  --account <name>         Load login credentials from gitignored artifacts/proof-verify/test-accounts.local.json
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
    if (kind === "select") {
        const eq = rest.indexOf("=");
        if (eq <= 0) throw new Error(`Invalid select shorthand "${raw}" (expected "select: Target=Value")`);
        return { action: "select", target: rest.slice(0, eq).trim(), value: rest.slice(eq + 1).trim() };
    }
    if (kind === "upload") {
        const lower = rest.toLowerCase();
        if (lower === "pdf" || lower === "png") return { action: "upload", fixture: lower };
        return { action: "upload", file: rest };
    }
    if (kind === "login") return { action: "login", account: rest };
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
        playbooks: [],
        preset: "",
        savePlaybook: "",
        noUpdatePlaybook: false,
        resume: false,
        fromStep: 1,
        healEngine: "auto",
        opencodeModel: DEFAULT_OPENCODE_MODELS[0],
        goal: "",
        maxTurns: 12,
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
        account: "",
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
        } else if (arg === "--playbook" || arg === "--chain") {
            const items = next().split(",").map((s) => s.trim()).filter(Boolean);
            opts.playbooks.push(...items);
            opts.playbook = opts.playbooks.join(",");
        } else if (arg.startsWith("--playbook=") || arg.startsWith("--chain=")) {
            const raw = arg.slice(arg.indexOf("=") + 1);
            const items = raw.split(",").map((s) => s.trim()).filter(Boolean);
            opts.playbooks.push(...items);
            opts.playbook = opts.playbooks.join(",");
        } else if (arg === "--preset") {
            opts.preset = next().trim();
        } else if (arg.startsWith("--preset=")) {
            opts.preset = arg.slice("--preset=".length).trim();
        } else if (arg === "--save-playbook") {
            opts.savePlaybook = next();
        } else if (arg === "--no-update-playbook") {
            opts.noUpdatePlaybook = true;
        } else if (arg === "--resume") {
            opts.resume = true;
        } else if (arg === "--from-step") {
            opts.fromStep = Math.max(1, Number.parseInt(next(), 10) || 1);
        } else if (arg.startsWith("--from-step=")) {
            opts.fromStep = Math.max(1, Number.parseInt(arg.slice("--from-step=".length), 10) || 1);
        } else if (arg === "--heal-engine") {
            opts.healEngine = next().trim().toLowerCase();
        } else if (arg.startsWith("--heal-engine=")) {
            opts.healEngine = arg.slice("--heal-engine=".length).trim().toLowerCase();
        } else if (arg === "--opencode-model") {
            opts.opencodeModel = next().trim();
        } else if (arg.startsWith("--opencode-model=")) {
            opts.opencodeModel = arg.slice("--opencode-model=".length).trim();
        } else if (arg === "--goal") {
            opts.goal = next().trim();
        } else if (arg.startsWith("--goal=")) {
            opts.goal = arg.slice("--goal=".length).trim();
        } else if (arg === "--max-turns") {
            opts.maxTurns = Math.max(1, Number.parseInt(next(), 10) || 12);
        } else if (arg.startsWith("--max-turns=")) {
            opts.maxTurns = Math.max(1, Number.parseInt(arg.slice("--max-turns=".length), 10) || 12);
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
        } else if (arg === "--account") {
            opts.account = next().trim();
        } else if (arg.startsWith("--account=")) {
            opts.account = arg.slice("--account=".length).trim();
        } else if (arg === "--user") {
            opts.user = next();
        } else if (arg === "--pass") {
            opts.pass = next();
        } else {
            throw new Error(`Unknown option: ${arg}`);
        }
    }

    if (!["auto", "ollama", "opencode", "off"].includes(opts.healEngine)) {
        throw new Error(`Invalid --heal-engine "${opts.healEngine}" (expected auto | ollama | opencode | off).`);
    }

    if (opts.stepsFile) {
        const loaded = readJsonFile(opts.stepsFile);
        if (Array.isArray(loaded)) {
            opts.steps.push(...loaded);
        } else if (loaded && typeof loaded === "object") {
            if (!opts.url && loaded.url) opts.url = loaded.url;
            if (!opts.account && loaded.account) opts.account = loaded.account;
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

function readJsonFile(filePath) {
    return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
}

function loadAccountFromRegistry(repoRoot, accountName) {
    if (!accountName) return null;
    const regPath = path.join(repoRoot, "artifacts", "proof-verify", "test-accounts.local.json");
    if (!fs.existsSync(regPath)) {
        throw new Error(`Account registry not found: ${regPath}`);
    }
    const reg = readJsonFile(regPath);
    const needle = String(accountName).trim().toLowerCase();
    const accounts = Array.isArray(reg.accounts) ? reg.accounts : [];
    const entry = accounts.find(
        (a) => String(a.username || "").toLowerCase() === needle
            || String(a.role || "").toLowerCase() === needle
            || String(a.displayName || "").toLowerCase() === needle
    );
    if (!entry) {
        const known = accounts.map((a) => a.username).filter(Boolean).join(", ");
        throw new Error(`Account "${accountName}" not found in ${regPath}. Known accounts: ${known || "(none)"}`);
    }
    const password = entry.password || reg.passwordAllCoreAccounts || "";
    if (!password) {
        throw new Error(`Account "${entry.username}" has no password configured in ${regPath}.`);
    }
    return { username: entry.username, password };
}

function ensureFixturePayload(runDir, step) {
    let fixture = String(step.fixture || "").trim().toLowerCase();
    if (!fixture && !step.file) {
        const hint = `${step.selector || ""} ${step.fileName || ""}`.toLowerCase();
        fixture = /pdf|doc|memo/.test(hint) ? "pdf" : "png";
    }
    if (fixture === "pdf") {
        const pdfText = [
            "%PDF-1.4",
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
            "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj",
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >> endobj",
            "xref",
            "0 4",
            "0000000000 65535 f ",
            "0000000009 00000 n ",
            "0000000058 00000 n ",
            "0000000115 00000 n ",
            "trailer << /Size 4 /Root 1 0 R >>",
            "startxref",
            "190",
            "%%EOF"
        ].join("\n") + "\n";
        const buf = Buffer.from(pdfText, "utf8");
        const fileName = step.fileName || "guided-hands-memo.pdf";
        const filePath = path.join(runDir, fileName);
        fs.writeFileSync(filePath, buf);
        return { fileName, mimeType: "application/pdf", filePath, base64Content: buf.toString("base64") };
    }
    if (fixture === "png") {
        const pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        const buf = Buffer.from(pngBase64, "base64");
        const fileName = step.fileName || "guided-hands-proof.png";
        const filePath = path.join(runDir, fileName);
        fs.writeFileSync(filePath, buf);
        return { fileName, mimeType: "image/png", filePath, base64Content: pngBase64 };
    }
    if (step.file) {
        const filePath = path.resolve(step.file);
        if (!fs.existsSync(filePath)) {
            throw new Error(`Upload file not found: ${filePath}`);
        }
        const buf = fs.readFileSync(filePath);
        const extName = path.extname(filePath).toLowerCase();
        const mimeType = step.mimeType
            || (extName === ".pdf" ? "application/pdf"
                : extName === ".png" ? "image/png"
                    : extName === ".jpg" || extName === ".jpeg" ? "image/jpeg"
                        : "application/octet-stream");
        return { fileName: path.basename(filePath), mimeType, filePath, base64Content: buf.toString("base64") };
    }
    throw new Error("Upload step requires either \"fixture\": \"pdf\"|\"png\" or \"file\": \"<path>\".");
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
            await new Promise((r) => setTimeout(r, 80));
        }
        await new Promise((r) => setTimeout(r, 150));
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

async function callOllamaJson({ ollamaUrl, model, system, prompt, schema, temperature = 0.2, numCtx = 2048, numPredict = 192 }) {
    const start = Date.now();
    const body = {
        model,
        stream: false,
        format: schema,
        keep_alive: "15m",
        options: {
            temperature,
            num_ctx: numCtx,
            num_predict: numPredict
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
// Free OpenCode Reasoning Client (Tier 2 Recovery + --goal Planner + Circuit Breaker)
// ============================================================================

let opencodeCircuitBroken = false;
let opencodeCircuitReason = "";

function resolveOpencodeBinary() {
    if (process.platform === "win32") {
        try {
            const whereOut = execFileSync("where.exe", ["opencode"], { encoding: "utf8" });
            for (const line of whereOut.split(/\r?\n/).map((s) => s.trim()).filter(Boolean)) {
                const exeCandidate = path.join(path.dirname(line), "node_modules", "@opencode", "cli", "bin", "opencode.exe");
                if (fs.existsSync(exeCandidate)) {
                    return { cmd: exeCandidate, prefixArgs: [] };
                }
            }
        } catch {
            // Fall through to cmd.exe wrapper
        }
        return { cmd: "cmd.exe", prefixArgs: ["/c", "opencode"] };
    }
    return { cmd: "opencode", prefixArgs: [] };
}

function extractJsonObject(rawText) {
    if (!rawText) return null;
    const trimmed = String(rawText).trim();
    try {
        return JSON.parse(trimmed);
    } catch {
        // Try fenced ```json block or outermost {...}
    }
    const fenced = trimmed.match(/```(?:json)?\s*([\s\S]*?)```/i);
    if (fenced && fenced[1]) {
        try {
            return JSON.parse(fenced[1].trim());
        } catch {
            // Fall through
        }
    }
    const first = trimmed.indexOf("{");
    const last = trimmed.lastIndexOf("}");
    if (first >= 0 && last > first) {
        try {
            return JSON.parse(trimmed.slice(first, last + 1));
        } catch {
            return null;
        }
    }
    return null;
}

function runOpencodeSingle(repoRoot, model, prompt, fileAttachments = [], timeoutMs = 45000) {
    const { cmd, prefixArgs } = resolveOpencodeBinary();
    const args = [...prefixArgs, "run", "-m", model];
    for (const filePath of fileAttachments) {
        args.push("-f", filePath);
    }
    args.push(prompt);

    return new Promise((resolve) => {
        const start = Date.now();
        const proc = spawn(cmd, args, {
            cwd: repoRoot,
            stdio: ["ignore", "pipe", "pipe"]
        });

        let stdout = "";
        let stderr = "";
        let timedOut = false;

        const timer = setTimeout(() => {
            timedOut = true;
            killProcessTree(proc);
        }, timeoutMs);

        proc.stdout.on("data", (d) => { stdout += String(d); });
        proc.stderr.on("data", (d) => { stderr += String(d); });

        proc.on("close", (code) => {
            clearTimeout(timer);
            resolve({
                ok: !timedOut && code === 0 && stdout.trim().length > 0,
                timedOut,
                code,
                durationMs: Date.now() - start,
                output: stdout.trim() || stderr.trim()
            });
        });

        proc.on("error", (err) => {
            clearTimeout(timer);
            resolve({ ok: false, timedOut: false, code: 1, durationMs: Date.now() - start, output: err.message });
        });
    });
}

async function callOpencodeJson({ repoRoot, primaryModel, prompt, fileAttachments = [], timeoutMs = 20000 }) {
    if (opencodeCircuitBroken) {
        return { ok: false, circuitBroken: true, error: `Circuit open (${opencodeCircuitReason})`, data: null, stats: null };
    }
    const modelChain = [primaryModel, ...DEFAULT_OPENCODE_MODELS.filter((m) => m !== primaryModel)].filter(Boolean);
    let lastError = "No OpenCode models available";
    for (const model of modelChain) {
        const res = await runOpencodeSingle(repoRoot, model, prompt, fileAttachments, timeoutMs);
        const outLower = String(res.output || "").toLowerCase();
        const isQuotaError = /rate.?limit|quota|429|too many requests|exhausted|exceeded|insufficient/i.test(outLower);
        if (res.ok && !isQuotaError) {
            const parsed = extractJsonObject(res.output);
            if (parsed && typeof parsed === "object") {
                return {
                    ok: true,
                    data: parsed,
                    stats: { model, durationMs: res.durationMs }
                };
            }
            lastError = `Invalid JSON returned by ${model}`;
        } else {
            lastError = `${model}: ${isQuotaError ? "rate-limited/quota" : (res.timedOut ? "timeout" : `code ${res.code}`)}`;
            if (isQuotaError) {
                opencodeCircuitBroken = true;
                opencodeCircuitReason = lastError;
                console.warn(`  [opencode] Circuit breaker tripped (${lastError}) — falling back to local Ollama for remaining steps.`);
                break;
            }
        }
    }
    if (!opencodeCircuitBroken) {
        opencodeCircuitBroken = true;
        opencodeCircuitReason = lastError;
        console.warn(`  [opencode] All OpenCode models failed (${lastError}) — tripping circuit breaker for remaining steps.`);
    }
    return { ok: false, circuitBroken: opencodeCircuitBroken, error: lastError, data: null, stats: null };
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

        const explicitScope = ${JSON.stringify(scopeSelector)};
        const modalEl = document.querySelector(".modal.show");
        const mainArea = document.querySelector("main, .main-content, #content") || document.body;
        const root = explicitScope
            ? document.querySelector(explicitScope)
            : (document.querySelector(".modal.show form")
                || modalEl
                || document.querySelector("#actualTicketEntry form")
                || pickNonLogoutForm(mainArea)
                || pickNonLogoutForm(document)
                || mainArea);
        if (!root) return { inModal: false, fields: [] };

        const onlyEmptyFlag = ${Boolean(onlyEmpty)};

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
                    if (opts.length === 0) continue;
                }
                fields.push(field);
                if (fields.length >= 30) break;
            }
            return fields;
        };

        let fields = collectFromRoot(root);
        if (fields.length === 0 && !explicitScope && !modalEl && root !== mainArea) {
            fields = collectFromRoot(mainArea);
        }
        if (fields.length === 0 && !explicitScope && !modalEl && mainArea !== document.body) {
            fields = collectFromRoot(document.body);
        }

        return { inModal: Boolean(modalEl), fields };
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

    // 3. Small local Ollama model picks from the tiny numbered list (compact schema: integer only for ~3x faster CPU output)
    const menu = clickables
        .map((c) => `[${c.id}] <${c.tag}> "${c.text}"${c.domId ? ` (#${c.domId})` : ""}`)
        .join("\n");

    const schema = {
        type: "object",
        properties: {
            elementId: { type: "integer" }
        },
        required: ["elementId"],
        additionalProperties: false
    };

    const res = await callOllamaJson({
        ...ollamaOpts,
        temperature: 0,
        numPredict: 24,
        system: "You match a requested UI action target to ONE numbered element from the provided list. Return elementId=0 if no element matches. Return ONLY JSON.",
        prompt: `Target to click: "${targetText}"\n\nVisible elements:\n${menu}\n\nReturn JSON with the single best matching elementId.`,
        schema
    });

    const chosen = clickables.find((c) => c.id === res.data.elementId) || null;
    return { matched: chosen, via: "ollama", reason: chosen ? `matched [${chosen.id}]` : "no match", llmStats: res.stats };
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
        if (k.includes("*")) {
            const regex = new RegExp(`^${k.trim().replace(/[.+?^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*")}$`, "i");
            for (const f of fields) {
                if (regex.test(f.key) || regex.test(f.label || "")) {
                    resolved[f.key] = v;
                }
            }
            continue;
        }
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
    // Match MSAP BafCycleStart: Tuesday-through-Monday weekly cycle in UTC+8
    const daysSinceTuesday = (phToday.getUTCDay() - 2 + 7) % 7;
    const phWeekStart = new Date(phToday.getTime() - daysSinceTuesday * 24 * 60 * 60 * 1000);
    const rand = String(Math.floor(1000 + Math.random() * 9000));
    return {
        today: phToday.toISOString().slice(0, 10),
        tomorrow: phTomorrow.toISOString().slice(0, 10),
        weekStart: phWeekStart.toISOString().slice(0, 10),
        rand,
        ...userVars
    };
}

function expandTemplate(value, vars) {
    if (typeof value !== "string") return value;
    let out = value;
    for (let pass = 0; pass < 3; pass++) {
        const next = out.replace(/\{([a-zA-Z0-9_]+)\}/g, (m, key) => (key in vars ? String(vars[key]) : m));
        if (next === out) break;
        out = next;
    }
    return out;
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
    const playbooksRoot = path.join(repoRoot, ".agents", "skills", "proof-verify", "playbooks");
    const directCandidate = path.resolve(nameOrPath);
    if (nameOrPath.endsWith(".json") && fs.existsSync(directCandidate)) {
        return directCandidate;
    }
    const cleanRel = nameOrPath
        .replace(/\\/g, "/")
        .replace(/^\/+|\/+$/g, "")
        .replace(/\.json$/i, "")
        .split("/")
        .map((seg) => seg.replace(/[^a-zA-Z0-9_-]+/g, "-").toLowerCase())
        .filter(Boolean)
        .join(path.sep);
    return path.join(playbooksRoot, `${cleanRel}.json`);
}

function loadPlaybookPresets(repoRoot) {
    const presetsPath = path.join(repoRoot, ".agents", "skills", "proof-verify", "playbooks", "presets.json");
    if (!fs.existsSync(presetsPath)) return {};
    try {
        const parsed = readJsonFile(presetsPath);
        return parsed?.presets && typeof parsed.presets === "object" ? parsed.presets : parsed;
    } catch {
        return {};
    }
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

function selectSuspectRepairFields(fields, errorMessage, previousValues = {}) {
    const errLower = String(errorMessage || "").toLowerCase();
    const errTokens = errLower.split(/[^a-z0-9]+/).filter((w) => w.length >= 4);
    const mentionsDate = /\b(date|time|future|past|before|after|start|end|window|arrival|depart|schedule|closed|period)\b/i.test(errLower);
    const mentionsNumber = /\b(amount|rate|hours|qty|quantity|number|positive|zero|greater|decimal|price|fee|charge)\b/i.test(errLower);

    const suspects = [];
    for (const f of fields) {
        const keyLower = f.key.toLowerCase();
        const labelLower = (f.label || "").toLowerCase();
        const prev = previousValues?.[f.key];
        const isBlankRequired = f.required && (prev === undefined || prev === null || String(prev).trim() === "");
        const matchesName = errLower.includes(keyLower)
            || (labelLower.length >= 3 && errLower.includes(labelLower.replace(/\*/g, "").trim()))
            || errTokens.some((tok) => keyLower.includes(tok) || labelLower.includes(tok));
        const matchesDate = mentionsDate && (f.type === "date" || f.type === "time" || f.type === "datetime-local" || /date|time|start|end/i.test(`${f.key} ${f.label}`));
        const matchesNumber = mentionsNumber && (f.type === "number" || f.inputMode === "decimal" || f.inputMode === "numeric" || /amount|rate|hour|qty|fee|charge/i.test(`${f.key} ${f.label}`));

        if (isBlankRequired || matchesName || matchesDate || matchesNumber) {
            suspects.push(f);
        }
    }
    return suspects;
}

async function generateFormValues(fields, scenario, overrides, ollamaOpts, repairContext = null, knownFields = null, vars = {}) {
    const expandedOverrides = expandTemplatesDeep(overrides, vars);
    const resolvedOverrides = repairContext?.unlockOverrides ? {} : resolveOverrides(fields, expandedOverrides);
    const afterOverrides = fields.filter((f) => !(f.key in resolvedOverrides));

    let reusedKnown = {};
    let remainingFields = afterOverrides;
    if (repairContext) {
        const prev = repairContext.previousValues || {};
        const suspects = selectSuspectRepairFields(afterOverrides, repairContext.errorMessage, prev);
        if (suspects.length > 0 && suspects.length < afterOverrides.length) {
            const suspectKeys = new Set(suspects.map((s) => s.key));
            for (const f of afterOverrides) {
                if (!suspectKeys.has(f.key) && f.key in prev) {
                    reusedKnown[f.key] = prev[f.key];
                }
            }
            remainingFields = suspects;
            console.log(`  [self-heal] Diff-only form repair: regenerating ${suspects.length}/${afterOverrides.length} suspect field(s): ${suspects.map((s) => s.key).join(", ")}`);
        }
    } else if (knownFields) {
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
        ? `Scenario: ${scenario || "valid realistic business test data"}\n\nPrevious values submitted for the failing field(s):\n${JSON.stringify(Object.fromEntries(remainingFields.map((f) => [f.key, repairContext.previousValues?.[f.key] ?? ""])), null, 2)}\n\nVALIDATION ERROR FROM THE APP:\n${repairContext.errorMessage}\n\nOutput corrected values ONLY for the fields listed below so the validation error is resolved.\n\nFields to repair:\n${fieldLines.join("\n")}`
        : `Scenario: ${scenario || "valid realistic business test data (Philippines)"}\n\nFields:\n${fieldLines.join("\n")}`;

    const res = await callOllamaJson({
        ...ollamaOpts,
        temperature: repairContext ? 0.1 : 0.3,
        system: `Generate valid test values for web form fields. Current Philippine time is ${phNow}. Respect max lengths strictly, use exact option IDs for selects, YYYY-MM-DD for dates, HH:MM (24-hour) for times, and never copy placeholders verbatim. Return ONLY JSON.`,
        prompt: promptBody,
        schema
    });

    const combined = enforceDateTimeGuards(fields, {
        ...(repairContext?.previousValues || {}),
        ...reusedKnown,
        ...res.data,
        ...resolvedOverrides
    });
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
                || (!swalConfirmVisible && /processing|saving|loading|please wait/i.test(\`\${swalTitle} \${swalText}\`)))
        );
        const swalIsError = Boolean(swalPopup?.querySelector(".swal2-error, .swal2-warning, .modern-alert-icon--error, .modern-alert-icon--warning")
            || /error|invalid|required|failed|could not|cannot|future|must be/i.test(\`\${swalTitle} \${swalText}\`));
        const toastText = [...document.querySelectorAll(".toast-body, .toast-message, .alert")]
            .map((el) => (el.innerText || "").replace(/\\s+/g, " ").trim())
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
    })()`);
}

async function selectOptionCdp(cdp, selectorOrTarget, valueOrText, scopeSelector = "") {
    return cdp.evaluate(`((rawTarget, rawWanted, scopeSel) => {
        const root = scopeSel
            ? document.querySelector(scopeSel)
            : (document.querySelector(".modal.show") || document);
        if (!root) return { ok: false, reason: \`Scope not found: \${scopeSel}\` };

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

        let selectEl = null;
        const target = String(rawTarget || "").trim();
        if (target) {
            if (/^[#.\\[]|^select\\b/i.test(target)) {
                try {
                    const candidate = root.querySelector(target);
                    if (candidate && candidate.tagName === "SELECT") selectEl = candidate;
                    else if (candidate) selectEl = candidate.querySelector("select") || candidate.parentElement?.querySelector("select");
                } catch {}
            }
            if (!selectEl) {
                const needle = target.toLowerCase();
                const selects = [...root.querySelectorAll("select")].filter((s) => !s.disabled);
                selectEl = selects.find((s) => (s.id || "").toLowerCase() === needle || (s.name || "").toLowerCase() === needle)
                    || selects.find((s) => getLabel(s).toLowerCase().replace(/\\*/g, "").trim() === needle)
                    || selects.find((s) => getLabel(s).toLowerCase().includes(needle) || (s.id || "").toLowerCase().includes(needle) || (s.name || "").toLowerCase().includes(needle));
            }
        } else {
            selectEl = [...root.querySelectorAll("select")].find((s) => !s.disabled) || null;
        }

        if (!selectEl) return { ok: false, reason: \`Select element not found for "\${target}"\` };

        const opts = [...selectEl.options].filter((o) => !o.disabled);
        const nonPlaceholder = opts.filter((o) => !(o.value === "" && /select|choose|--/i.test((o.textContent || "").trim())));
        if (nonPlaceholder.length === 0) {
            return { ok: false, waitingForOptions: true, reason: \`Select "\${selectEl.id || selectEl.name || target}" has no populated options yet\` };
        }

        const wanted = String(rawWanted ?? "").trim();
        const wantedLower = wanted.toLowerCase();
        const matchedOpt = opts.find((o) => o.value === wanted)
            || opts.find((o) => (o.textContent || "").replace(/\\s+/g, " ").trim().toLowerCase() === wantedLower)
            || opts.find((o) => (o.textContent || "").replace(/\\s+/g, " ").trim().toLowerCase().includes(wantedLower));

        if (!matchedOpt) {
            return {
                ok: false,
                waitingForOptions: true,
                reason: \`Option "\${wanted}" not found in "\${selectEl.id || selectEl.name || target}" (\${nonPlaceholder.length} options)\`,
                availableSample: nonPlaceholder.slice(0, 8).map((o) => \`\${o.value}=\${(o.textContent || "").trim()}\`)
            };
        }

        const modernContainer = selectEl.nextElementSibling?.classList.contains("modern-select-container")
            ? selectEl.nextElementSibling
            : selectEl.parentElement?.querySelector(".modern-select-container");
        let usedModernClick = false;
        if (modernContainer) {
            const trigger = modernContainer.querySelector(".modern-select-trigger");
            if (trigger) {
                trigger.scrollIntoView({ block: "center" });
                trigger.click();
                const optNode = document.querySelector(\`.modern-select-dropdown.show .modern-select-option[data-value="\${CSS.escape(matchedOpt.value)}"]\`)
                    || modernContainer.querySelector(\`.modern-select-option[data-value="\${CSS.escape(matchedOpt.value)}"]\`);
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
                } catch {}
            }
        }

        return {
            ok: true,
            matchedKey: selectEl.id || selectEl.name || getLabel(selectEl),
            selectedValue: selectEl.value,
            selectedText: (matchedOpt.textContent || "").replace(/\\s+/g, " ").trim(),
            usedModernClick
        };
    })(${JSON.stringify(selectorOrTarget)}, ${JSON.stringify(valueOrText)}, ${JSON.stringify(scopeSelector)})`);
}

async function uploadFileCdp(cdp, selector, payload, scopeSelector = "") {
    return cdp.evaluate(`((sel, fileName, mimeType, base64Content, scopeSel) => {
        const root = scopeSel ? document.querySelector(scopeSel) : (document.querySelector(".modal.show") || document);
        if (!root) return { ok: false, reason: \`Scope not found: \${scopeSel}\` };
        const inputEl = sel
            ? (root.querySelector(sel) || document.querySelector(sel))
            : (root.querySelector("input[type='file']:not([disabled])") || document.querySelector("input[type='file']:not([disabled])"));
        if (!inputEl) return { ok: false, reason: \`File input not found: \${sel || "input[type='file']"}\` };

        const bin = atob(base64Content);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        const file = new File([bytes], fileName, { type: mimeType || "application/octet-stream" });
        const dt = new DataTransfer();
        dt.items.add(file);
        inputEl.files = dt.files;
        inputEl.dispatchEvent(new Event("input", { bubbles: true }));
        inputEl.dispatchEvent(new Event("change", { bubbles: true }));
        return {
            ok: true,
            selectorUsed: inputEl.id ? \`#\${inputEl.id}\` : (inputEl.name ? \`input[name='\${inputEl.name}']\` : "input[type='file']"),
            fileName,
            size: bytes.length
        };
    })(${JSON.stringify(selector || "")}, ${JSON.stringify(payload.fileName)}, ${JSON.stringify(payload.mimeType)}, ${JSON.stringify(payload.base64Content)}, ${JSON.stringify(scopeSelector)})`);
}

async function captureSnapshotCdp(cdp, scopeSelector = "") {
    const fb = await readPageFeedback(cdp);
    const clickRes = await extractClickables(cdp, scopeSelector);
    const fieldRes = await extractFormFields(cdp, scopeSelector, false);
    const pathLower = String(fb.pathname || "").toLowerCase();
    return {
        ...fb,
        isLoginPage: pathLower.includes("/identity/account/login"),
        isAccessDenied: pathLower.includes("/identity/account/accessdenied"),
        inModal: clickRes.inModal || fieldRes.inModal,
        clickables: (clickRes.items || []).slice(0, 25),
        fields: (fieldRes.fields || []).slice(0, 25).map((f) => ({
            key: f.key,
            tag: f.tag,
            type: f.type,
            label: f.label,
            required: f.required,
            currentValue: f.currentValue,
            options: Array.isArray(f.options) ? f.options.slice(0, 10) : undefined
        })),
        bodyExcerpt: (fb.bodySample || "").slice(0, 1800)
    };
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

    async closeTab() {
        await this.rpc("close_tab", { tabId: this.tabId }).catch(() => {});
        this.tabId = null;
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

    async selectOption(target, value, scopeSelector = "") {
        return this.rpc("select_option", { target, value, scopeSelector });
    }

    async uploadFile(selector, payload, scopeSelector = "") {
        return this.rpc("upload_file", {
            selector,
            fileName: payload.fileName,
            mimeType: payload.mimeType,
            base64Content: payload.base64Content,
            scopeSelector
        });
    }

    async captureSnapshot(scopeSelector = "") {
        return this.rpc("capture_snapshot", { scopeSelector });
    }

    async stepPreflight(params = {}) {
        return this.rpc("step_preflight", params);
    }

    async autoLogin(username, password) {
        const res = await this.rpc("auto_login", { username, password });
        return Boolean(res?.submitted);
    }

    async autoLogout() {
        const res = await this.rpc("auto_logout");
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
// Autonomous Recovery Helpers (Tier 0 / Tier 1 Ollama / Tier 2 OpenCode)
// ============================================================================

function detectPageBlocker(fb, allowLoginPage = false) {
    if (!fb) return null;
    if (fb.hasServerError) {
        return { type: "server_error", fatal: true, message: `ASP.NET server error on ${fb.url || "current page"}` };
    }
    const pathLower = String(fb.pathname || "").toLowerCase();
    if (pathLower.includes("/identity/account/accessdenied")) {
        return { type: "access_denied", fatal: true, message: `Access denied at ${fb.url}` };
    }
    if (!allowLoginPage && pathLower.includes("/identity/account/login")) {
        return { type: "login_redirect", fatal: false, message: `Redirected to Login (${fb.url})` };
    }
    if (fb.swal && fb.swal.isError) {
        return {
            type: "error_swal",
            fatal: false,
            message: `Error popup: ${fb.swal.title || ""} ${fb.swal.text || ""}`.trim()
        };
    }
    return null;
}

function stepAllowsDestructive(step, goalText = "") {
    const raw = [
        step?.target,
        step?.selector,
        step?.scenario,
        step?.text,
        goalText
    ].filter(Boolean).join(" ");
    return DESTRUCTIVE_TARGET_RE.test(raw);
}

function isDestructiveCandidate(labelText) {
    return DESTRUCTIVE_TARGET_RE.test(String(labelText || ""));
}

function formatCompactSnapshotForLlm(snapshot) {
    return {
        url: snapshot.url,
        pathname: snapshot.pathname,
        title: snapshot.title,
        inModal: snapshot.inModal,
        isLoginPage: snapshot.isLoginPage,
        isAccessDenied: snapshot.isAccessDenied,
        hasServerError: snapshot.hasServerError,
        swal: snapshot.swal,
        validationErrors: snapshot.validationErrors || [],
        alerts: snapshot.alerts || [],
        clickables: (snapshot.clickables || []).slice(0, 20).map((c) => ({
            id: c.id,
            tag: c.tag,
            text: c.text,
            domId: c.domId || undefined
        })),
        fields: (snapshot.fields || []).slice(0, 20).map((f) => ({
            key: f.key,
            tag: f.tag,
            type: f.type,
            label: f.label,
            required: f.required || undefined,
            currentValue: f.currentValue,
            options: Array.isArray(f.options) ? f.options.slice(0, 8) : undefined
        })),
        bodyExcerpt: (snapshot.bodyExcerpt || "").slice(0, 1200)
    };
}

async function askOllamaStepRecovery({ failedStep, errorMessage, snapshot, ollamaOpts }) {
    const clickMenu = (snapshot.clickables || [])
        .slice(0, 20)
        .map((c) => `[${c.id}] <${c.tag}> "${c.text}"${c.domId ? ` (#${c.domId})` : ""}`)
        .join("\n");
    const fieldMenu = (snapshot.fields || [])
        .slice(0, 15)
        .map((f) => {
            const optsStr = Array.isArray(f.options) && f.options.length
                ? ` options=[${f.options.slice(0, 6).map((o) => `${o.value}:${o.text}`).join(", ")}]`
                : "";
            return `- ${f.key} (${f.label}, ${f.type}${f.required ? ", required" : ""}, cur="${f.currentValue ?? ""}")${optsStr}`;
        })
        .join("\n");

    const schema = {
        type: "object",
        properties: {
            recoveryAction: {
                type: "string",
                enum: ["click_alternative", "select_option", "fill_missing_fields", "dismiss_modal", "wait_and_retry", "escalate", "abort"]
            },
            reason: { type: "string" },
            clickableId: { type: "integer" },
            selectTarget: { type: "string" },
            selectValue: { type: "string" },
            fieldPatches: {
                type: "array",
                items: {
                    type: "object",
                    properties: {
                        key: { type: "string" },
                        value: { type: "string" }
                    },
                    required: ["key", "value"],
                    additionalProperties: false
                }
            }
        },
        required: ["recoveryAction", "reason"],
        additionalProperties: false
    };

    const prompt = [
        `Failed step: ${JSON.stringify(failedStep)}`,
        `Error: ${errorMessage}`,
        `Current URL: ${snapshot.url}`,
        snapshot.swal ? `Active popup: ${JSON.stringify(snapshot.swal)}` : "",
        snapshot.validationErrors?.length ? `Validation errors: ${snapshot.validationErrors.join(" | ")}` : "",
        clickMenu ? `Visible clickables:\n${clickMenu}` : "Visible clickables: (none)",
        fieldMenu ? `Visible fields:\n${fieldMenu}` : "Visible fields: (none)",
        "",
        "Choose ONE safe recovery action. Use \"escalate\" if unsure or if multiple steps are needed. Use \"abort\" if the app shows a genuine server/business error."
    ].filter(Boolean).join("\n");

    const res = await callOllamaJson({
        ...ollamaOpts,
        temperature: 0.1,
        numPredict: 80,
        system: "You are a fast browser recovery advisor. Keep reason under 8 words. If a failed click step matches a visible clickable by intent (e.g. 'new-schedule' -> 'New Schedule' or 'OK' -> 'Done'), pick click_alternative with its clickableId. Pick escalate only when no single element matches. Never pick destructive buttons (Delete, Void, Reverse, Finalize) unless requested. Return ONLY JSON.",
        prompt,
        schema
    });
    return res;
}

async function askOllamaMultiStepRecovery({ failedStep, errorMessage, snapshot, ollamaOpts }) {
    const clickMenu = (snapshot.clickables || [])
        .slice(0, 18)
        .map((c) => `[${c.id}] <${c.tag}> "${c.text}"${c.domId ? ` (#${c.domId})` : ""}`)
        .join("\n");
    const fieldMenu = (snapshot.fields || [])
        .slice(0, 12)
        .map((f) => `- ${f.key} (${f.label}, ${f.type}, cur="${f.currentValue ?? ""}")`)
        .join("\n");

    const schema = {
        type: "object",
        properties: {
            abort: { type: "boolean" },
            reason: { type: "string" },
            recoverySteps: {
                type: "array",
                items: {
                    type: "object",
                    properties: {
                        action: {
                            type: "string",
                            enum: ["click", "select", "fill_form", "wait", "navigate"]
                        },
                        target: { type: "string" },
                        selector: { type: "string" },
                        value: { type: "string" },
                        url: { type: "string" },
                        ms: { type: "integer" }
                    },
                    required: ["action"],
                    additionalProperties: false
                }
            }
        },
        required: ["abort", "reason", "recoverySteps"],
        additionalProperties: false
    };

    const prompt = [
        `Failed step: ${JSON.stringify(failedStep)}`,
        `Error: ${errorMessage}`,
        `URL: ${snapshot.url}`,
        snapshot.swal ? `Active popup: ${JSON.stringify(snapshot.swal)}` : "",
        snapshot.validationErrors?.length ? `Validation errors: ${snapshot.validationErrors.join(" | ")}` : "",
        clickMenu ? `Visible clickables:\n${clickMenu}` : "",
        fieldMenu ? `Visible fields:\n${fieldMenu}` : "",
        `Page text excerpt:\n${(snapshot.bodyExcerpt || "").slice(0, 450)}`,
        "",
        "Propose 1 to 2 safe recoverySteps to overcome this UI blocker, or set abort=true if this is a real app error."
    ].filter(Boolean).join("\n");

    const res = await callOllamaJson({
        ...ollamaOpts,
        temperature: 0.1,
        numPredict: 140,
        system: "You are a local browser recovery planner. Output 1-2 safe recoverySteps (prefer action='click' with exact visible button text as target, or action='select' with target and value). Set abort=true and recoverySteps=[] if the page shows a genuine server/permission error. Return ONLY JSON.",
        prompt,
        schema
    });
    return { ok: true, data: res.data, stats: res.stats };
}

async function askOllamaGoalNextAction({ goal, turn, maxTurns, history, snapshot, ollamaOpts }) {
    const clickMenu = (snapshot.clickables || [])
        .slice(0, 20)
        .map((c) => `[${c.id}] <${c.tag}> "${c.text}"${c.domId ? ` (#${c.domId})` : ""}`)
        .join("\n");
    const fieldMenu = (snapshot.fields || [])
        .slice(0, 12)
        .map((f) => {
            const optsStr = Array.isArray(f.options) && f.options.length
                ? ` options=[${f.options.slice(0, 5).map((o) => `${o.value}:${o.text}`).join(", ")}]`
                : "";
            return `- ${f.key} (${f.label}, ${f.type}, cur="${f.currentValue ?? ""}")${optsStr}`;
        })
        .join("\n");

    const schema = {
        type: "object",
        properties: {
            action: {
                type: "string",
                enum: ["navigate", "click", "select", "fill_form", "fill_and_submit", "upload", "wait", "assert", "done", "fail"]
            },
            reason: { type: "string" },
            url: { type: "string" },
            target: { type: "string" },
            selector: { type: "string" },
            value: { type: "string" },
            text: { type: "string" },
            summary: { type: "string" }
        },
        required: ["action", "reason"],
        additionalProperties: false
    };

    const prompt = [
        `Goal: ${goal}`,
        `Turn: ${turn}/${maxTurns}`,
        `Recent history: ${JSON.stringify(history.slice(-5))}`,
        `Current URL: ${snapshot.url} (title: ${snapshot.title})`,
        snapshot.swal ? `Active popup: ${JSON.stringify(snapshot.swal)}` : "",
        clickMenu ? `Visible clickables:\n${clickMenu}` : "",
        fieldMenu ? `Visible fields:\n${fieldMenu}` : "",
        `Page excerpt:\n${(snapshot.bodyExcerpt || "").slice(0, 500)}`,
        "",
        "Choose the single next browser action to achieve the Goal. Return action='done' only when the goal is verified on the page."
    ].filter(Boolean).join("\n");

    const res = await callOllamaJson({
        ...ollamaOpts,
        temperature: 0.1,
        numPredict: 120,
        system: "You drive a browser step-by-step to achieve a verification goal. Keep reason under 10 words. Prefer semantic target text for click/select. Return ONLY JSON.",
        prompt,
        schema
    });
    return { ok: true, data: res.data, stats: res.stats };
}

const SAFE_RECOVERY_ACTIONS = new Set([
    "navigate",
    "click",
    "select",
    "fill_form",
    "fill_and_submit",
    "upload",
    "wait",
    "assert"
]);

async function askOpencodeStepRecovery({ repoRoot, runDir, primaryModel, failedStep, errorMessage, snapshot, stepNum }) {
    const contextPath = path.join(runDir, `tier2-recovery-step-${stepNum}.json`);
    fs.writeFileSync(
        contextPath,
        JSON.stringify(
            {
                failedStep,
                errorMessage,
                snapshot: formatCompactSnapshotForLlm(snapshot)
            },
            null,
            2
        ),
        "utf8"
    );
    const prompt = [
        "Do NOT run shell commands or inspect repo files. Read ONLY the attached JSON snapshot of a failed browser step.",
        "Decide whether this is a recoverable UI/selector/modal/missing-input hiccup or a genuine application bug.",
        "Reply with ONLY valid JSON matching this schema:",
        "{",
        "  \"abort\": false,",
        "  \"reason\": \"short explanation\",",
        "  \"recoverySteps\": [",
        "    { \"action\": \"click|select|fill_form|fill_and_submit|upload|wait|navigate\", \"target\": \"...\", \"selector\": \"...\", \"value\": \"...\", \"overrides\": {}, \"ms\": 500 }",
        "  ],",
        "  \"replaceStep\": { \"action\": \"...\" }",
        "}",
        "Rules:",
        "- Keep recoverySteps to 1-3 safe steps using ONLY actions: navigate, click, select, fill_form, fill_and_submit, upload, wait.",
        "- Prefer semantic select ({ \"action\": \"select\", \"target\": \"label or #id\", \"value\": \"option value or text\" }) for dropdowns.",
        "- For clicks, prefer target text (e.g. { \"action\": \"click\", \"target\": \"+ New schedule\" }) WITHOUT selector. NEVER use Playwright/jQuery pseudo-selectors like :has-text() or :contains().",
        "- NEVER click destructive actions (Delete, Void, Reverse, Finalize) unless failedStep explicitly targeted them.",
        "- Set replaceStep to a clean single replacement step for the playbook if appropriate, or omit it.",
        "- Set abort=true and recoverySteps=[] if the page shows a real server error, access denied, or unrecoverable business failure."
    ].join("\n");

    return callOpencodeJson({
        repoRoot,
        primaryModel,
        prompt,
        fileAttachments: [contextPath],
        timeoutMs: 45000
    });
}

async function askOpencodeGoalNextAction({ repoRoot, runDir, primaryModel, goal, turn, maxTurns, history, snapshot }) {
    const contextPath = path.join(runDir, `goal-turn-${turn}.json`);
    fs.writeFileSync(
        contextPath,
        JSON.stringify(
            {
                goal,
                turn,
                maxTurns,
                recentHistory: history.slice(-8),
                page: formatCompactSnapshotForLlm(snapshot)
            },
            null,
            2
        ),
        "utf8"
    );
    const prompt = [
        "Do NOT run shell commands or read repo files. Read ONLY the attached live browser state JSON.",
        "You are driving a browser step-by-step to achieve the user's verification goal.",
        "Return ONLY valid JSON for the single next action:",
        "{",
        "  \"action\": \"navigate|click|select|fill_form|fill_and_submit|upload|wait|assert|done|fail\",",
        "  \"reason\": \"concise explanation\",",
        "  \"url\": \"/route (for navigate)\",",
        "  \"target\": \"visible button text or dropdown label (for click/select)\",",
        "  \"selector\": \"optional CSS selector\",",
        "  \"value\": \"option value or text (for select)\",",
        "  \"scenario\": \"short description (for fill_form/fill_and_submit)\",",
        "  \"overrides\": { \"FieldKey\": \"Value\" },",
        "  \"fixture\": \"pdf or png (for upload)\",",
        "  \"text\": \"expected text (for assert)\",",
        "  \"urlIncludes\": \"expected url fragment (for assert)\",",
        "  \"summary\": \"final verification summary (for done/fail)\"",
        "}",
        "Rules:",
        "- Prefer semantic target/label over brittle CSS selectors.",
        "- Use action=\"select\" for dropdowns (including ModernSelect).",
        "- Before returning action=\"done\", make sure the goal's expected outcome is actually visible on the page (or verified via assert).",
        "- Return action=\"fail\" immediately if a genuine application bug / 500 error / missing expected data prevents achieving the goal."
    ].join("\n");

    return callOpencodeJson({
        repoRoot,
        primaryModel,
        prompt,
        fileAttachments: [contextPath],
        timeoutMs: 45000
    });
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
    const checkpointPath = path.join(repoRoot, "artifacts", "proof-verify", "last-checkpoint.json");
    let loadedCheckpoint = null;
    if (opts.resume) {
        if (!fs.existsSync(checkpointPath)) {
            throw new Error(`Cannot --resume: checkpoint not found at ${checkpointPath}`);
        }
        loadedCheckpoint = readJsonFile(checkpointPath);
        if (!opts.playbook && loadedCheckpoint.playbook) opts.playbook = loadedCheckpoint.playbook;
        if (!opts.stepsFile && !opts.playbook && loadedCheckpoint.stepsFile) {
            opts.stepsFile = loadedCheckpoint.stepsFile;
            const loaded = readJsonFile(opts.stepsFile);
            if (Array.isArray(loaded)) opts.steps.push(...loaded);
            else if (loaded && typeof loaded === "object") {
                if (!opts.url && loaded.url) opts.url = loaded.url;
                if (!opts.account && loaded.account) opts.account = loaded.account;
                if (Array.isArray(loaded.steps)) opts.steps.push(...loaded.steps);
            }
        }
        if (!opts.url && loadedCheckpoint.url) opts.url = loadedCheckpoint.url;
        if (!opts.account && loadedCheckpoint.account) opts.account = loadedCheckpoint.account;
        if (opts.fromStep <= 1 && Number.isInteger(loadedCheckpoint.nextStep) && loadedCheckpoint.nextStep > 1) {
            opts.fromStep = loadedCheckpoint.nextStep;
        }
        console.log(`[guided-hands] Resuming from checkpoint at step ${opts.fromStep} (vars: ${Object.keys(loadedCheckpoint.vars || {}).join(", ") || "none"})`);
    } else if (opts.fromStep > 1 && fs.existsSync(checkpointPath)) {
        try {
            loadedCheckpoint = readJsonFile(checkpointPath);
        } catch {
            loadedCheckpoint = null;
        }
    }

    if (opts.resume && loadedCheckpoint?.playbook && opts.playbooks.length === 0) {
        opts.playbooks = String(loadedCheckpoint.playbook).split(",").map((s) => s.trim()).filter(Boolean);
        opts.playbook = opts.playbooks.join(",");
    }

    const presets = loadPlaybookPresets(repoRoot);
    let presetVars = {};
    const expandedPlaybookNames = [];
    if (opts.preset) {
        const entry = presets[opts.preset];
        if (!entry) {
            throw new Error(`Unknown preset "${opts.preset}". Available presets: ${Object.keys(presets).join(", ") || "(none)"}`);
        }
        const chainList = Array.isArray(entry) ? entry : (Array.isArray(entry.chain) ? entry.chain : []);
        if (entry.vars && typeof entry.vars === "object") {
            presetVars = { ...presetVars, ...entry.vars };
        }
        expandedPlaybookNames.push(...chainList);
        console.log(`[guided-hands] Expanded preset "${opts.preset}" -> ${chainList.join(" -> ")}`);
    }
    for (const rawName of opts.playbooks) {
        const directPath = resolvePlaybookPath(repoRoot, rawName);
        if (!fs.existsSync(directPath) && presets[rawName]) {
            const entry = presets[rawName];
            const chainList = Array.isArray(entry) ? entry : (Array.isArray(entry.chain) ? entry.chain : []);
            if (entry.vars && typeof entry.vars === "object") {
                presetVars = { ...presetVars, ...entry.vars };
            }
            expandedPlaybookNames.push(...chainList);
            console.log(`[guided-hands] Expanded preset "${rawName}" -> ${chainList.join(" -> ")}`);
        } else {
            expandedPlaybookNames.push(rawName);
        }
    }
    if (expandedPlaybookNames.length > 0) {
        opts.playbooks = expandedPlaybookNames;
        opts.playbook = expandedPlaybookNames.join(",");
    }

    const loadedPlaybooks = [];
    let mergedPlaybookVars = { ...presetVars };
    const collectedSteps = [];
    for (const pbName of opts.playbooks) {
        const pbPath = resolvePlaybookPath(repoRoot, pbName);
        if (!fs.existsSync(pbPath)) {
            throw new Error(`Playbook not found: ${pbPath}`);
        }
        const doc = readJsonFile(pbPath);
        loadedPlaybooks.push({ name: pbName, path: pbPath, doc });
        if (!opts.url && doc.url) opts.url = doc.url;
        if (!opts.account && doc.account) opts.account = doc.account;
        if (doc.vars && typeof doc.vars === "object") {
            mergedPlaybookVars = { ...mergedPlaybookVars, ...doc.vars };
        }
        if (Array.isArray(doc.steps)) {
            const cloned = JSON.parse(JSON.stringify(doc.steps));
            cloned.forEach((s, idx) => {
                s._sourcePlaybookPath = pbPath;
                s._sourceStepIndex = idx;
            });
            collectedSteps.push(...cloned);
        }
    }
    if (loadedPlaybooks.length > 0 && opts.steps.length === 0) {
        opts.steps = collectedSteps;
        const summaryNames = loadedPlaybooks.map((p) => p.name).join(" -> ");
        console.log(`[guided-hands] Loaded ${loadedPlaybooks.length} playbook(s): ${summaryNames} (${opts.steps.length} total step(s))`);
    }

    const loadedPlaybook = loadedPlaybooks.length === 1 ? loadedPlaybooks[0].doc : null;
    const playbookPath = loadedPlaybooks.length === 1 ? loadedPlaybooks[0].path : "";
    const savePlaybookPath = opts.savePlaybook
        ? resolvePlaybookPath(repoRoot, opts.savePlaybook)
        : (playbookPath && !opts.noUpdatePlaybook ? playbookPath : "");

    const vars = buildTemplateVars({
        ...mergedPlaybookVars,
        ...(loadedCheckpoint?.vars || {}),
        ...opts.vars
    });

    if (opts.account && (!opts.user || !opts.pass)) {
        const creds = loadAccountFromRegistry(repoRoot, opts.account);
        opts.user = creds.username;
        opts.pass = creds.password;
        console.log(`[guided-hands] Loaded credentials for account "${creds.username}" from test-accounts.local.json`);
    }

    if (!opts.url) {
        throw new Error("Missing --url (or use --playbook / --resume / --benchmark).");
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
    const expandedStartUrl = expandTemplate(
        (opts.resume && loadedCheckpoint?.currentUrl) ? loadedCheckpoint.currentUrl : opts.url,
        vars
    );
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
        healEngine: opts.healEngine,
        transport: opts.extension ? "chrome-extension" : "cdp",
        startUrl,
        fromStep: opts.fromStep,
        goal: opts.goal || null,
        passed: true,
        steps: [],
        recoveries: [],
        patchedSteps: [],
        failureSnapshot: null,
        screenshots: [],
        errors: []
    };

    let currentStepIndex = -1;
    let currentStepObj = null;

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
        const doSelectOption = (target, value, scope) => (ext
            ? ext.selectOption(target, value, scope)
            : selectOptionCdp(cdp, target, value, scope));
        const doUploadFile = (selector, payload, scope) => (ext
            ? ext.uploadFile(selector, payload, scope)
            : uploadFileCdp(cdp, selector, payload, scope));
        const doCaptureSnapshot = (scope = "") => (ext
            ? ext.captureSnapshot(scope)
            : captureSnapshotCdp(cdp, scope));
        const doCheckSelector = (sel) => (ext
            ? ext.checkSelector(sel)
            : cdp.evaluate(`(() => { try { return Boolean(document.querySelector(${JSON.stringify(sel)})); } catch { return false; } })()`));
        const doClickSelector = (sel) => (ext
            ? ext.clickSelector(sel)
            : cdp.evaluate(`(() => {
                let el = null;
                try { el = document.querySelector(${JSON.stringify(sel)}); } catch { return false; }
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
        const doLogin = async (username, password) => {
            if (!username || !password) return false;
            if (!ext) return attemptAutoLogin(cdp, username, password);
            const ok = await ext.autoLogin(username, password);
            if (!ok) return false;
            const deadline = Date.now() + 15000;
            while (Date.now() < deadline) {
                await new Promise((r) => setTimeout(r, 300));
                const fb = await ext.readFeedback().catch(() => null);
                if (fb?.pathname && !fb.pathname.toLowerCase().includes("/identity/account/login")) {
                    return true;
                }
            }
            return false;
        };
        const doLogout = async () => {
            if (ext) return ext.autoLogout();
            const submitted = await cdp.evaluate(`(() => {
                const form = document.querySelector("form#mnav-logout-form, form#logoutForm, form[action*='Logout']");
                if (!form) return false;
                form.submit();
                return true;
            })()`);
            if (submitted) {
                await new Promise((r) => setTimeout(r, 700));
            }
            return Boolean(submitted);
        };
        const SWAL_CONFIRM_SEL = ".swal2-popup:not([style*='display: none']):not(.swal2-hide):not(.animate__fadeOutDown) .swal2-confirm";
        const waitForSwalClose = async (urlBefore = "", maxMs = 1500) => {
            const deadline = Date.now() + maxMs;
            while (Date.now() < deadline) {
                await new Promise((r) => setTimeout(r, 120));
                const fb = await getFeedback().catch(() => null);
                if (!fb) continue;
                if (urlBefore && fb.url !== urlBefore) {
                    if (ext) await ext.waitLoad().catch(() => {});
                    return true;
                }
                if (!fb.swal) return true;
            }
            return false;
        };
        const dismissInfoSwalIfBlocking = async (allowConfirm = false) => {
            const fb = await getFeedback().catch(() => null);
            if (!fb?.swal || fb.swal.isError || fb.swal.isLoading) return false;
            const confirmText = (fb.swal.confirmText || "").trim();
            if (fb.swal.confirmVisible && !fb.swal.cancelVisible && (!confirmText || /^ok$/i.test(confirmText))) {
                const clicked = await doClickSelector(SWAL_CONFIRM_SEL).catch(() => false);
                if (clicked) {
                    await waitForSwalClose(fb.url, 1200);
                    return true;
                }
            }
            const { items } = await doExtractClickables(".swal2-popup:not([style*='display: none']):not(.swal2-hide):not(.animate__fadeOutDown)").catch(() => ({ items: [] }));
            if (items.length === 1 && /^ok$/i.test(items[0].text.trim())) {
                const clicked = await doClickSelector(`[data-gh-click-id="${items[0].id}"]`).catch(() => false);
                if (clicked) {
                    await waitForSwalClose(fb.url, 1200);
                    return true;
                }
            }
            if (allowConfirm && items.length > 0) {
                const confirmBtn = items.find((it) => /^(ok|yes|proceed|continue|confirm)\b/i.test(it.text.trim()) && !isDestructiveCandidate(it.text));
                if (confirmBtn) {
                    const clicked = await doClickSelector(`[data-gh-click-id="${confirmBtn.id}"]`).catch(() => false);
                    if (clicked) {
                        await waitForSwalClose(fb.url, 1200);
                        return true;
                    }
                }
            }
            return false;
        };
        const settlePostSubmitSwal = async ({ urlBefore, waitAfterMs, waitForNavigation, extractErrs }) => {
            const initialDeadline = Date.now() + Math.max(waitAfterMs || 0, 3500);
            const loadingDeadline = Date.now() + 12000;
            await new Promise((r) => setTimeout(r, 220));
            let fb = await getFeedback().catch(() => ({ url: urlBefore, validationErrors: [], swal: null, hasServerError: false }));
            while (Date.now() < loadingDeadline) {
                if (fb.url !== urlBefore || fb.hasServerError || extractErrs(fb).length > 0) break;
                if (fb.swal && !fb.swal.isLoading) break;
                if (!fb.swal?.isLoading && Date.now() >= initialDeadline) break;
                await new Promise((r) => setTimeout(r, 150));
                fb = await getFeedback().catch(() => fb);
            }

            const errParts = extractErrs(fb);
            if (errParts.length > 0 || !fb.swal) {
                return { fb, errParts };
            }

            // Handle confirm -> loading -> Success [OK] chains cleanly without clicking hidden buttons
            const chainDeadline = Date.now() + 10000;
            let noSwalTicks = 0;
            let clickedConfirm = false;
            while (Date.now() < chainDeadline) {
                const fbNow = await getFeedback().catch(() => null);
                if (!fbNow) {
                    await new Promise((r) => setTimeout(r, 150));
                    continue;
                }
                fb = fbNow;
                if (fb.url !== urlBefore && !fb.swal) break;
                const errsNow = extractErrs(fb);
                if (errsNow.length > 0) {
                    errParts.push(...errsNow);
                    break;
                }
                if (!fb.swal) {
                    noSwalTicks++;
                    // If we just dismissed a modal and expect navigation, give location.href a moment to start
                    const neededIdleTicks = (clickedConfirm && waitForNavigation) ? 6 : 2;
                    if (noSwalTicks >= neededIdleTicks) break;
                    await new Promise((r) => setTimeout(r, 150));
                    continue;
                }
                noSwalTicks = 0;
                if (fb.swal.isLoading) {
                    await new Promise((r) => setTimeout(r, 180));
                    continue;
                }
                if (fb.swal.confirmVisible) {
                    const clicked = await doClickSelector(SWAL_CONFIRM_SEL).catch(() => false);
                    if (clicked) clickedConfirm = true;
                    await new Promise((r) => setTimeout(r, 220));
                    continue;
                }
                await new Promise((r) => setTimeout(r, 150));
            }
            return { fb, errParts };
        };

        const activeAuth = { user: opts.user, pass: opts.pass, account: opts.account };
        let lastIntendedUrl = startUrl;

        const initFb = await getFeedback();
        const curPath = initFb.pathname || new URL(initFb.url).pathname;
        const firstStepIsLogin = opts.steps[0]?.action === "login";
        if (!firstStepIsLogin && !opts.url.toLowerCase().includes("/identity/account/login") && String(curPath).toLowerCase().includes("/identity/account/login")) {
            const loggedIn = await doLogin(activeAuth.user, activeAuth.pass);
            if (!loggedIn) {
                throw new Error("AUTH_REQUIRED: redirected to Login. Pass --account <name> (from test-accounts.local.json) or set IBS_VERIFY_USER & IBS_VERIFY_PASS.");
            }
            await doNavigate(startUrl);
        }

        if (opts.inspect) {
            const snap = await doCaptureSnapshot("");
            console.log(JSON.stringify(snap, null, 2));
            await doHud("Guided Hands inspect complete", "done");
            return;
        }

        const saveCheckpoint = (stepNum, currentUrl) => {
            const doc = {
                timestamp: new Date().toISOString(),
                playbook: opts.playbook || "",
                stepsFile: opts.stepsFile || "",
                url: opts.url,
                account: activeAuth.account || "",
                lastCompletedStep: stepNum,
                nextStep: stepNum + 1,
                currentUrl: currentUrl || lastIntendedUrl,
                vars
            };
            fs.writeFileSync(checkpointPath, JSON.stringify(doc, null, 2) + "\n", "utf8");
        };

        const runStartMs = Date.now();
        const preClickDelay = () => (opts.slowMo > 0 ? opts.slowMo : 60);

        const executeStepAction = async (step, stepNum) => {
            const detail = {};
            const elapsed = ((Date.now() - runStartMs) / 1000).toFixed(1);

            if (step.action === "navigate") {
                if (!step.url) {
                    throw new Error("Navigate step is missing required \"url\".");
                }
                const rawUrl = expandTemplate(step.url, vars);
                const dest = rawUrl.startsWith("http") ? rawUrl : `${opts.baseUrl}${rawUrl.startsWith("/") ? "" : "/"}${rawUrl}`;
                lastIntendedUrl = dest;
                await doNavigate(dest);
                const fb = await getFeedback();
                const blocker = detectPageBlocker(fb, dest.toLowerCase().includes("/identity/account/login"));
                if (blocker && blocker.type === "login_redirect" && activeAuth.user && activeAuth.pass) {
                    const ok = await doLogin(activeAuth.user, activeAuth.pass);
                    if (ok) {
                        await doNavigate(dest);
                    }
                }
                detail.url = (await getFeedback()).url;
                return detail;
            }

            if (step.action === "login") {
                let loginUser = expandTemplate(step.username || activeAuth.user || "", vars);
                let loginPass = expandTemplate(step.password || activeAuth.pass || "", vars);
                if (step.account) {
                    const accountName = expandTemplate(step.account, vars);
                    const creds = loadAccountFromRegistry(repoRoot, accountName);
                    loginUser = creds.username;
                    loginPass = creds.password;
                    activeAuth.account = accountName;
                }
                if (!loginUser || !loginPass) {
                    throw new Error("Login step requires account/username/password.");
                }
                const fbBefore = await getFeedback();
                const onLoginPage = String(fbBefore.pathname || "").toLowerCase().includes("/identity/account/login");
                if (!step.force && !onLoginPage && activeAuth.loggedInUser && activeAuth.loggedInUser.toLowerCase() === loginUser.toLowerCase()) {
                    console.log(`  [login] Already signed in as "${loginUser}"; skipping redundant re-login.`);
                    return { username: loginUser, reusedSession: true, url: fbBefore.url };
                }
                if (!onLoginPage) {
                    const loggedOut = await doLogout().catch(() => false);
                    if (loggedOut) {
                        const outDeadline = Date.now() + 4000;
                        while (Date.now() < outDeadline) {
                            const fbOut = await getFeedback().catch(() => null);
                            if (String(fbOut?.pathname || "").toLowerCase().includes("/identity/account/login")) break;
                            await new Promise((r) => setTimeout(r, 150));
                        }
                    }
                    const fbAfterLogout = await getFeedback().catch(() => null);
                    if (!String(fbAfterLogout?.pathname || "").toLowerCase().includes("/identity/account/login")) {
                        await doNavigate(`${opts.baseUrl}/Identity/Account/Login`);
                    }
                }
                const ok = await doLogin(loginUser, loginPass);
                if (!ok) throw new Error(`Login step failed for "${loginUser}": login form not found or authentication did not redirect.`);
                activeAuth.user = loginUser;
                activeAuth.pass = loginPass;
                activeAuth.loggedInUser = loginUser;
                return { username: loginUser, url: (await getFeedback()).url };
            }

            if (step.action === "wait") {
                if (step.selector) {
                    const deadline = Date.now() + (step.timeoutMs || 10000);
                    let found = false;
                    let tick = 0;
                    while (Date.now() < deadline) {
                        found = await doCheckSelector(step.selector);
                        if (found) break;
                        tick++;
                        if (tick % 2 === 0) {
                            await dismissInfoSwalIfBlocking(false);
                            const fb = await getFeedback().catch(() => null);
                            const blocker = detectPageBlocker(fb, false);
                            if (blocker) {
                                const err = new Error(`Fast-fail while waiting for "${step.selector}": ${blocker.message}`);
                                err.blocker = blocker;
                                throw err;
                            }
                        }
                        await new Promise((r) => setTimeout(r, 150));
                    }
                    if (!found) throw new Error(`Timed out waiting for selector: ${step.selector}`);
                    return { selector: step.selector };
                }
                await new Promise((r) => setTimeout(r, step.ms || 500));
                return { ms: step.ms || 500 };
            }

            if (step.action === "select") {
                const target = expandTemplate(step.selector || step.target || "", vars);
                const value = expandTemplate(String(step.value ?? step.text ?? ""), vars);
                const timeoutMs = step.timeoutMs || 4000;
                const deadline = Date.now() + timeoutMs;
                let lastRes = null;
                let tick = 0;
                while (Date.now() < deadline) {
                    lastRes = await doSelectOption(target, value, step.scope || "");
                    if (lastRes?.ok) {
                        if (step.waitAfterMs) {
                            await new Promise((r) => setTimeout(r, step.waitAfterMs));
                        } else {
                            await new Promise((r) => setTimeout(r, 200));
                        }
                        return lastRes;
                    }
                    tick++;
                    if (tick % 3 === 0) {
                        const fb = await getFeedback().catch(() => null);
                        const blocker = detectPageBlocker(fb, false);
                        if (blocker) {
                            const err = new Error(`Fast-fail during select "${target}": ${blocker.message}`);
                            err.blocker = blocker;
                            throw err;
                        }
                    }
                    await new Promise((r) => setTimeout(r, 180));
                }
                if (step.optional) {
                    return { target, value, skipped: true, reason: lastRes?.reason };
                }
                throw new Error(lastRes?.reason || `Select failed for "${target}" = "${value}"`);
            }

            if (step.action === "upload") {
                const payload = ensureFixturePayload(runDir, step);
                const res = await doUploadFile(step.selector || "", payload, step.scope || "");
                if (!res?.ok) {
                    if (step.optional) return { skipped: true, reason: res?.reason };
                    throw new Error(res?.reason || "File upload failed.");
                }
                await new Promise((r) => setTimeout(r, step.waitAfterMs || 200));
                return res;
            }

            if (step.action === "click") {
                const targetingSwal = Boolean(step.scope && step.scope.includes("swal2"));
                if (!targetingSwal) {
                    await dismissInfoSwalIfBlocking(false);
                }
                const timeoutMs = step.timeoutMs || (step.optional ? 600 : 2500);
                const hasNonCssPseudo = Boolean(step.selector && /:(?:has-text|contains|text)\s*\(/i.test(step.selector));
                const effectiveSelector = hasNonCssPseudo ? "" : (step.selector || "");
                const effectiveTarget = step.target || (hasNonCssPseudo
                    ? (step.selector.match(/:(?:has-text|contains|text)\s*\(\s*["']([^"']+)["']\s*\)/i)?.[1] || "")
                    : "");
                if (effectiveSelector) {
                    const deadline = Date.now() + timeoutMs;
                    let clicked = false;
                    let tick = 0;
                    while (Date.now() < deadline) {
                        const exists = await doCheckSelector(effectiveSelector);
                        if (exists) {
                            if (opts.headed) {
                                await doHud(`Guided Hands (${elapsed}s) · Clicking ${effectiveSelector}`, "active", effectiveSelector);
                                await new Promise((r) => setTimeout(r, preClickDelay()));
                            }
                            clicked = await doClickSelector(effectiveSelector);
                            if (clicked) break;
                        }
                        tick++;
                        if (tick % 3 === 0) {
                            if (!targetingSwal) {
                                await dismissInfoSwalIfBlocking(false);
                            }
                            const fb = await getFeedback().catch(() => null);
                            const blocker = detectPageBlocker(fb, false);
                            if (blocker && !targetingSwal) {
                                const err = new Error(`Fast-fail before clicking "${effectiveSelector}": ${blocker.message}`);
                                err.blocker = blocker;
                                throw err;
                            }
                        }
                        await new Promise((r) => setTimeout(r, 150));
                    }
                    if (!clicked) {
                        if (effectiveTarget) {
                            // Tier 0 fallback: selector missed, try semantic target matching
                            const { inModal, items } = await doExtractClickables(step.scope || "");
                            const pick = items.length > 0
                                ? await resolveClickTarget(items, effectiveTarget, ollamaOpts, Boolean(step.optional))
                                : { matched: null };
                            if (pick.matched) {
                                const clickSel = `[data-gh-click-id="${pick.matched.id}"]`;
                                const okFallback = await doClickSelector(clickSel);
                                if (okFallback) {
                                    await new Promise((r) => setTimeout(r, step.waitAfterMs ?? 250));
                                    if (ext && step.waitForNavigation) await ext.waitLoad();
                                    return { inModal, target: effectiveTarget, matched: pick.matched, via: `selector-fallback:${pick.via}` };
                                }
                            }
                        }
                        if (step.optional) return { selector: effectiveSelector, via: "selector", skipped: true };
                        throw new Error(`Selector not found: ${effectiveSelector}`);
                    }
                    await new Promise((r) => setTimeout(r, step.waitAfterMs ?? 250));
                    if (ext && step.waitForNavigation) {
                        await ext.waitLoad();
                    }
                    return { selector: effectiveSelector, via: "selector", skipped: false };
                }

                const clickTargetText = effectiveTarget || step.target || "";
                const deadline = Date.now() + (targetingSwal ? (step.waitForPopupMs ?? 1200) : timeoutMs);
                let inModal = false;
                let items = [];
                let pick = { matched: null };
                let tick = 0;

                while (Date.now() < deadline) {
                    ({ inModal, items } = await doExtractClickables(step.scope || ""));
                    if (items.length > 0) {
                        pick = await resolveClickTarget(items, clickTargetText, ollamaOpts, Boolean(step.optional));
                        if (pick.matched) break;
                        // If Ollama already ran and failed, don't keep looping Ollama
                        if (pick.via === "ollama") break;
                    }
                    tick++;
                    if (tick % 3 === 0 && !targetingSwal) {
                        await dismissInfoSwalIfBlocking(false);
                        const fb = await getFeedback().catch(() => null);
                        const blocker = detectPageBlocker(fb, false);
                        if (blocker) {
                            const err = new Error(`Fast-fail before clicking "${clickTargetText}": ${blocker.message}`);
                            err.blocker = blocker;
                            throw err;
                        }
                    }
                    if (step.optional && tick >= 2) break;
                    await new Promise((r) => setTimeout(r, 175));
                }

                if (items.length === 0) {
                    if (step.optional) return { target: clickTargetText, skipped: true };
                    throw new Error(`No visible clickable elements found for "${clickTargetText}".`);
                }
                if (!pick.matched) {
                    if (step.optional) return { target: clickTargetText, skipped: true };
                    throw new Error(`Could not match click target "${clickTargetText}" among ${items.length} elements (${pick.reason || "no match"}).`);
                }

                const clickSel = `[data-gh-click-id="${pick.matched.id}"]`;
                if (opts.headed) {
                    const nowSec = ((Date.now() - runStartMs) / 1000).toFixed(1);
                    await doHud(`Guided Hands (${nowSec}s) · Clicking "${pick.matched.text}" (${pick.via})`, "active", clickSel);
                    await new Promise((r) => setTimeout(r, preClickDelay()));
                }
                const clicked = await doClickSelector(clickSel);
                if (!clicked) throw new Error("Matched element disappeared before click.");
                await new Promise((r) => setTimeout(r, step.waitAfterMs ?? 250));
                if (ext && step.waitForNavigation) {
                    await ext.waitLoad();
                }
                return { inModal, target: clickTargetText, matched: pick.matched, via: pick.via, llmStats: pick.llmStats };
            }

            if (step.action === "fill_form" || step.action === "fill_and_submit") {
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
                        const fbCheck = await getFeedback().catch(() => null);
                        const blocker = detectPageBlocker(fbCheck, false);
                        const err = new Error(blocker ? `No visible editable form fields found (${blocker.message}).` : "No visible editable form fields found.");
                        if (blocker) err.blocker = blocker;
                        throw err;
                    }
                    if (attempt > 0) {
                        const nowSec = ((Date.now() - runStartMs) / 1000).toFixed(1);
                        await doHud(
                            `Guided Hands (${nowSec}s) · Diff-repair ${attempt}/${maxRetries}: ${repairContext.errorMessage.slice(0, 55)}`,
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
                        await new Promise((r) => setTimeout(r, step.waitAfterMs ?? 250));
                        break;
                    }

                    // Submit with reactive condition-based settlement instead of blind 1800ms + 1200ms sleeps
                    const urlBeforeSubmit = (await getFeedback()).url;
                    const submitSel = step.submitSelector || "button[type='submit']";
                    await doHud(`Guided Hands · Submitting (${attempt + 1}/${maxRetries + 1})`, "active", submitSel);
                    await new Promise((r) => setTimeout(r, preClickDelay()));
                    const clicked = await doClickSelector(submitSel);
                    if (!clicked) throw new Error(`Submit button not found: ${submitSel}`);

                    const extractErrs = (feedbackObj) => {
                        const out = [];
                        if (feedbackObj?.swal && feedbackObj.swal.isError !== false && !feedbackObj.swal.isLoading
                            && /error|invalid|required|failed|could not|cannot|future|must be/i.test(`${feedbackObj.swal.title} ${feedbackObj.swal.text}`)) {
                            out.push(`${feedbackObj.swal.title}: ${feedbackObj.swal.text}`.trim());
                        }
                        const realValErrs = (feedbackObj?.validationErrors || []).filter(
                            (msg) => !/^(cancel\s+cancel\s+service|delete_forever\s+void\s+ticket)$/i.test(msg.trim())
                        );
                        if (realValErrs.length) out.push(...realValErrs);
                        return out;
                    };

                    const { fb, errParts } = await settlePostSubmitSwal({
                        urlBefore: urlBeforeSubmit,
                        waitAfterMs: step.waitAfterMs,
                        waitForNavigation: Boolean(step.waitForNavigation),
                        extractErrs
                    });

                    if (errParts.length === 0) {
                        if (ext && step.waitForNavigation) {
                            await ext.waitLoad();
                        }
                        attemptRecord.submitted = true;
                        attempts.push(attemptRecord);
                        succeeded = true;
                        break;
                    }

                    const errorMessage = errParts.join(" | ");
                    console.log(`  [self-heal] Attempt ${attempt + 1} rejected: ${errorMessage}`);
                    attemptRecord.rejectedWith = errorMessage;
                    attempts.push(attemptRecord);

                    if (fb.swal?.confirmVisible) {
                        await doClickSelector(SWAL_CONFIRM_SEL).catch(() => {});
                        await waitForSwalClose(urlBeforeSubmit, 1000);
                    }
                    repairContext = {
                        previousValues: gen.values,
                        errorMessage,
                        unlockOverrides: attempt >= 1
                    };
                }

                if (!succeeded) {
                    const lastErr = attempts[attempts.length - 1]?.rejectedWith || "Form submission rejected";
                    const err = new Error(`Form failed after ${attempts.length} attempt(s): ${lastErr}`);
                    err.attempts = attempts;
                    throw err;
                }
                return step.action === "fill_form" ? attempts[0] : { attempts, retries: attempts.length - 1 };
            }

            if (step.action === "capture_url") {
                const regex = new RegExp(step.pattern, "i");
                let feedback = await getFeedback();
                let match = feedback.url.match(regex);
                if (!match || !match[1]) {
                    const capDeadline = Date.now() + (step.timeoutMs || 3000);
                    while (Date.now() < capDeadline) {
                        await dismissInfoSwalIfBlocking(false);
                        await new Promise((r) => setTimeout(r, 180));
                        feedback = await getFeedback().catch(() => feedback);
                        match = feedback.url.match(regex);
                        if (match && match[1]) break;
                    }
                }
                if (!match || !match[1]) {
                    if (step.optional) {
                        return { url: feedback.url, var: step.var, captured: null, skipped: true };
                    }
                    throw new Error(`Step ${stepNum} capture_url failed: URL "${feedback.url}" did not match /${step.pattern}/i.`);
                }
                vars[step.var] = match[1];
                console.log(`  [capture] {${step.var}} = "${match[1]}" (from ${feedback.url})`);
                return { url: feedback.url, var: step.var, captured: match[1] };
            }

            if (step.action === "assert") {
                const expectedUrl = expandTemplate(step.urlIncludes || "", vars);
                const expectedText = expandTemplate(step.text || "", vars);
                const timeoutMs = step.timeoutMs ?? 3500;
                const deadline = Date.now() + timeoutMs;
                let feedback = null;
                let failures = [];

                while (true) {
                    feedback = await getFeedback();
                    const bodyText = ext ? (feedback.bodySample || "") : await cdp.evaluate("(document.body?.innerText || '')");
                    failures = [];
                    if (expectedUrl && !feedback.url.toLowerCase().includes(expectedUrl.toLowerCase())) {
                        failures.push(`URL "${feedback.url}" does not include "${expectedUrl}"`);
                    }
                    if (expectedText && !bodyText.toLowerCase().includes(expectedText.toLowerCase())) {
                        failures.push(`text "${expectedText}" not found on page`);
                    }
                    if (step.noValidationErrors && feedback.validationErrors.length > 0) {
                        failures.push(`validation errors present: ${feedback.validationErrors.join("; ")}`);
                    }
                    if (failures.length === 0) break;

                    await dismissInfoSwalIfBlocking(false);
                    const blocker = detectPageBlocker(feedback, expectedUrl.toLowerCase().includes("login"));
                    if (blocker && blocker.type === "server_error") {
                        const err = new Error(`Step ${stepNum} assert fast-failed: ${blocker.message}`);
                        err.blocker = blocker;
                        throw err;
                    }
                    if (Date.now() >= deadline) break;
                    await new Promise((r) => setTimeout(r, 200));
                }

                if (failures.length > 0) {
                    throw new Error(`Step ${stepNum} assert failed: ${failures.join(" | ")}`);
                }
                return feedback;
            }

            if (step.action === "screenshot") {
                const safeName = (step.name || `step-${stepNum}`).replace(/[^a-zA-Z0-9_-]+/g, "-").toLowerCase();
                return { screenshot: await doScreenshot(path.join(runDir, `${safeName}.png`)) };
            }

            throw new Error(`Unknown step action: ${step.action}`);
        };

        const runStepWithRecovery = async (step, stepNum) => {
            try {
                return await executeStepAction(step, stepNum);
            } catch (initialErr) {
                if (opts.healEngine === "off" || step.noHeal) {
                    throw initialErr;
                }

                const allowsDestructive = stepAllowsDestructive(step, opts.goal);
                let lastErr = initialErr;

                // ---- Tier 0: Deterministic Guardrails (0 tokens, <300ms) ----
                let snap0 = await doCaptureSnapshot(step.scope || "").catch(() => null);
                if (step.scope && snap0 && (snap0.clickables?.length || 0) === 0 && (snap0.fields?.length || 0) === 0) {
                    snap0 = await doCaptureSnapshot("").catch(() => snap0);
                }
                const blocker0 = lastErr.blocker || detectPageBlocker(snap0, false);

                if (blocker0?.type === "server_error" || blocker0?.type === "access_denied") {
                    throw lastErr; // Genuine app/auth defect: fail fast to Tier 3 dossier
                }

                // Tier 0a: Mid-run Login redirect recovery
                if (blocker0?.type === "login_redirect" && activeAuth.user && activeAuth.pass) {
                    console.log(`  [tier0-heal] Session redirected to Login; auto-logging in as "${activeAuth.user}" and retrying step ${stepNum}...`);
                    await doHud(`Tier 0 Heal · Auto-login (${activeAuth.user})`, "active");
                    const ok = await doLogin(activeAuth.user, activeAuth.pass);
                    if (ok) {
                        if (lastIntendedUrl) await doNavigate(lastIntendedUrl);
                        const retryDetail = await executeStepAction(step, stepNum);
                        report.recoveries.push({
                            step: stepNum,
                            tier: "tier0-deterministic",
                            originalError: lastErr.message,
                            reason: "Mid-run login redirect recovered via auto-login",
                            actionTaken: { action: "auto_login", user: activeAuth.user }
                        });
                        return retryDetail;
                    }
                }

                // Tier 0b: Unexpected non-error SweetAlert blocking step
                if (snap0?.swal && !snap0.swal.isError && !(step.scope || "").includes("swal2")) {
                    const dismissed = await dismissInfoSwalIfBlocking(true);
                    if (dismissed) {
                        console.log(`  [tier0-heal] Dismissed blocking modal "${snap0.swal.title || snap0.swal.text}"; retrying step ${stepNum}...`);
                        try {
                            const retryDetail = await executeStepAction(step, stepNum);
                            report.recoveries.push({
                                step: stepNum,
                                tier: "tier0-deterministic",
                                originalError: lastErr.message,
                                reason: `Dismissed blocking modal "${snap0.swal.title || snap0.swal.text}"`,
                                actionTaken: { action: "dismiss_modal" }
                            });
                            return retryDetail;
                        } catch (e) {
                            lastErr = e;
                        }
                    }
                }

                // Tier 0c: fill_form on single select outside <form> (e.g. ModernSelect filter dropdown)
                if (step.action === "fill_form" && step.overrides && !Array.isArray(step.overrides)) {
                    const entries = Object.entries(step.overrides);
                    if (entries.length === 1) {
                        const [k, v] = entries[0];
                        const val = expandTemplate(String(v), vars);
                        let selRes = await doSelectOption(k, val, step.scope || "");
                        if (!selRes?.ok && step.scope) {
                            selRes = await doSelectOption(k, val, "");
                        }
                        if (selRes?.ok) {
                            const replacement = { action: "select", target: k, value: String(v) };
                            step._patchedStep = replacement;
                            console.log(`  [tier0-heal] Converted fill_form -> select("${k}", "${val}") and queued playbook patch.`);
                            report.recoveries.push({
                                step: stepNum,
                                tier: "tier0-deterministic",
                                originalError: lastErr.message,
                                reason: "Standalone select/ModernSelect outside form handled via semantic select",
                                actionTaken: replacement,
                                suggestedStepPatch: { replaceStep: stepNum, step: replacement }
                            });
                            return selRes;
                        }
                    }
                }

                // If unauthenticated Login redirect with no configured credentials, fail fast cleanly
                if (blocker0?.type === "login_redirect" && (!activeAuth.user || !activeAuth.pass)) {
                    throw new Error(`AUTH_REQUIRED at step ${stepNum}: redirected to Login. Pass --account <name> or set IBS_VERIFY_USER & IBS_VERIFY_PASS.`);
                }

                // ---- Tier 1: Local Ollama qwen2.5:1.5b Micro-Recovery (~1-2s, 0 cloud tokens) ----
                let shouldEscalateToTier2 = opts.healEngine === "opencode";
                if (opts.healEngine === "auto" || opts.healEngine === "ollama") {
                    let snap1 = await doCaptureSnapshot(step.scope || "").catch(() => snap0);
                    if (step.scope && snap1 && (snap1.clickables?.length || 0) === 0 && (snap1.fields?.length || 0) === 0) {
                        snap1 = await doCaptureSnapshot("").catch(() => snap0);
                    }
                    if (snap1) {
                        console.log(`  [tier1-ollama] Diagnosing step ${stepNum} failure with local ${opts.model}...`);
                        await doHud(`Tier 1 Heal (${opts.model}) · Step ${stepNum}`, "active");
                        try {
                            const advice = await askOllamaStepRecovery({
                                failedStep: step,
                                errorMessage: lastErr.message,
                                snapshot: snap1,
                                ollamaOpts
                            });
                            const act = advice.data?.recoveryAction || "escalate";
                            console.log(`  [tier1-ollama] Decision: ${act} (${advice.data?.reason || ""}) [${advice.stats.durationMs}ms]`);

                            if (act === "abort") {
                                throw lastErr;
                            }
                            if (act === "escalate") {
                                shouldEscalateToTier2 = true;
                            } else if (act === "dismiss_modal") {
                                await dismissInfoSwalIfBlocking(true);
                                const retryDetail = await executeStepAction(step, stepNum);
                                report.recoveries.push({
                                    step: stepNum,
                                    tier: "tier1-ollama",
                                    originalError: lastErr.message,
                                    reason: advice.data.reason,
                                    actionTaken: { action: "dismiss_modal" },
                                    llmStats: advice.stats
                                });
                                return retryDetail;
                            } else if (act === "wait_and_retry") {
                                await new Promise((r) => setTimeout(r, 1200));
                                const retryDetail = await executeStepAction(step, stepNum);
                                report.recoveries.push({
                                    step: stepNum,
                                    tier: "tier1-ollama",
                                    originalError: lastErr.message,
                                    reason: advice.data.reason,
                                    actionTaken: { action: "wait_and_retry", ms: 1200 },
                                    llmStats: advice.stats
                                });
                                return retryDetail;
                            } else if (act === "click_alternative") {
                                const reasonLower = String(advice.data.reason || "").toLowerCase();
                                const explicitNoMatch = /\bno\s+match(ing)?\b|\bnot\s+found\b|\bnone\b/i.test(reasonLower);
                                let chosen = (!explicitNoMatch && Number.isInteger(advice.data.clickableId) && advice.data.clickableId > 0)
                                    ? (snap1.clickables || []).find((c) => c.id === advice.data.clickableId)
                                    : null;
                                if (!chosen && !explicitNoMatch && advice.data.reason) {
                                    const qMatch = advice.data.reason.match(/['"]([^'"]{2,40})['"]/);
                                    if (qMatch) {
                                        const needle = qMatch[1].trim().toLowerCase();
                                        chosen = (snap1.clickables || []).find((c) => c.text.toLowerCase().includes(needle)) || null;
                                    }
                                }
                                if (chosen && (allowsDestructive || !isDestructiveCandidate(chosen.text))) {
                                    const clicked = await doClickSelector(`[data-gh-click-id="${chosen.id}"]`);
                                    if (clicked) {
                                        await new Promise((r) => setTimeout(r, 300));
                                        const replacement = step.action === "click"
                                            ? { ...step, selector: undefined, target: chosen.text }
                                            : null;
                                        if (replacement) {
                                            delete replacement.selector;
                                            step._patchedStep = replacement;
                                        }
                                        const finalDetail = step.action === "click"
                                            ? { matched: chosen, via: "tier1-ollama-recovery" }
                                            : await executeStepAction(step, stepNum);
                                        report.recoveries.push({
                                            step: stepNum,
                                            tier: "tier1-ollama",
                                            originalError: lastErr.message,
                                            reason: advice.data.reason,
                                            actionTaken: { action: "click", target: chosen.text },
                                            suggestedStepPatch: replacement ? { replaceStep: stepNum, step: replacement } : undefined,
                                            llmStats: advice.stats
                                        });
                                        return finalDetail;
                                    }
                                }
                                shouldEscalateToTier2 = true;
                            } else if (act === "select_option" && advice.data.selectTarget) {
                                const selRes = await doSelectOption(advice.data.selectTarget, advice.data.selectValue ?? "", step.scope || "");
                                if (selRes?.ok) {
                                    const replacement = (step.action === "select" || step.action === "fill_form")
                                        ? { action: "select", target: advice.data.selectTarget, value: String(advice.data.selectValue ?? "") }
                                        : null;
                                    if (replacement) step._patchedStep = replacement;
                                    const finalDetail = (step.action === "select" || step.action === "fill_form")
                                        ? selRes
                                        : await executeStepAction(step, stepNum);
                                    report.recoveries.push({
                                        step: stepNum,
                                        tier: "tier1-ollama",
                                        originalError: lastErr.message,
                                        reason: advice.data.reason,
                                        actionTaken: { action: "select", target: advice.data.selectTarget, value: advice.data.selectValue },
                                        suggestedStepPatch: replacement ? { replaceStep: stepNum, step: replacement } : undefined,
                                        llmStats: advice.stats
                                    });
                                    return finalDetail;
                                }
                                shouldEscalateToTier2 = true;
                            } else if (act === "fill_missing_fields" && Array.isArray(advice.data.fieldPatches) && advice.data.fieldPatches.length > 0) {
                                const patchObj = Object.fromEntries(advice.data.fieldPatches.map((p) => [p.key, p.value]));
                                await doApplyValues(patchObj);
                                await new Promise((r) => setTimeout(r, 200));
                                const retryDetail = await executeStepAction(step, stepNum);
                                report.recoveries.push({
                                    step: stepNum,
                                    tier: "tier1-ollama",
                                    originalError: lastErr.message,
                                    reason: advice.data.reason,
                                    actionTaken: { action: "fill_missing_fields", patchObj },
                                    llmStats: advice.stats
                                });
                                return retryDetail;
                            } else {
                                shouldEscalateToTier2 = true;
                            }
                        } catch (tier1Err) {
                            console.warn(`  [tier1-ollama] Recovery attempt failed: ${tier1Err.message}`);
                            shouldEscalateToTier2 = true;
                        }
                    }
                }

                // ---- Tier 2: Multi-Step Escalation (Free OpenCode -> Local Ollama Fallback via Circuit Breaker) ----
                if (shouldEscalateToTier2 && opts.healEngine !== "off") {
                    let snap2 = await doCaptureSnapshot(step.scope || "").catch(() => snap0);
                    if (step.scope && snap2 && (snap2.clickables?.length || 0) === 0 && (snap2.fields?.length || 0) === 0) {
                        snap2 = await doCaptureSnapshot("").catch(() => snap0);
                    }
                    if (snap2 && !snap2.hasServerError && !snap2.isAccessDenied) {
                        let oc = { ok: false, error: "Skipped (ollama-only mode)" };
                        let usedTierLabel = "tier2-opencode";
                        if ((opts.healEngine === "auto" || opts.healEngine === "opencode") && !opencodeCircuitBroken) {
                            console.log(`  [tier2-opencode] Escalating step ${stepNum} recovery to free OpenCode (${opts.opencodeModel})...`);
                            await doHud(`Tier 2 Heal (OpenCode free) · Step ${stepNum}`, "active");
                            oc = await askOpencodeStepRecovery({
                                repoRoot,
                                runDir,
                                primaryModel: opts.opencodeModel,
                                failedStep: step,
                                errorMessage: lastErr.message,
                                snapshot: snap2,
                                stepNum
                            });
                        }
                        if (!oc.ok && (opts.healEngine === "auto" || opts.healEngine === "ollama")) {
                            usedTierLabel = "tier2-ollama-multistep";
                            console.log(`  [tier2-ollama] Running local multi-step recovery with ${opts.model}...`);
                            await doHud(`Tier 2 Local Heal (${opts.model}) · Step ${stepNum}`, "active");
                            try {
                                oc = await askOllamaMultiStepRecovery({
                                    failedStep: step,
                                    errorMessage: lastErr.message,
                                    snapshot: snap2,
                                    ollamaOpts
                                });
                            } catch (localMultiErr) {
                                oc = { ok: false, error: localMultiErr.message };
                            }
                        }
                        if (oc.ok && oc.data && !oc.data.abort && Array.isArray(oc.data.recoverySteps) && oc.data.recoverySteps.length > 0) {
                            const planned = oc.data.recoverySteps.slice(0, 3);
                            let safeAll = true;
                            for (const rs of planned) {
                                if (!SAFE_RECOVERY_ACTIONS.has(rs?.action)) {
                                    safeAll = false;
                                    break;
                                }
                                if (rs.action === "navigate" && !rs.url) {
                                    safeAll = false;
                                    break;
                                }
                                if (!allowsDestructive && isDestructiveCandidate(`${rs.target || ""} ${rs.selector || ""}`)) {
                                    safeAll = false;
                                    break;
                                }
                            }
                            if (safeAll) {
                                console.log(`  [${usedTierLabel}] Executing ${planned.length} recovery step(s): ${oc.data.reason || ""} [${oc.stats.durationMs}ms]`);
                                try {
                                    const executedDetails = [];
                                    for (const rs of planned) {
                                        executedDetails.push(await executeStepAction(rs, stepNum));
                                    }
                                    // If replaceStep replaced the failed step directly, don't duplicate the broken original step
                                    let finalDetail = { recoveredBy: planned, details: executedDetails };
                                    if (oc.data.replaceStep && SAFE_RECOVERY_ACTIONS.has(oc.data.replaceStep.action)) {
                                        step._patchedStep = oc.data.replaceStep;
                                    } else {
                                        finalDetail = await executeStepAction(step, stepNum);
                                    }
                                    report.recoveries.push({
                                        step: stepNum,
                                        tier: usedTierLabel,
                                        originalError: lastErr.message,
                                        reason: oc.data.reason || "",
                                        actionTaken: planned,
                                        suggestedStepPatch: oc.data.replaceStep ? { replaceStep: stepNum, step: oc.data.replaceStep } : undefined,
                                        llmStats: oc.stats
                                    });
                                    return finalDetail;
                                } catch (tier2Err) {
                                    console.warn(`  [${usedTierLabel}] Recovery execution failed: ${tier2Err.message}`);
                                }
                            }
                        } else if (!oc.ok) {
                            console.warn(`  [${usedTierLabel}] Unavailable: ${oc.error}`);
                        }
                    }
                }

                throw lastErr;
            }
        };

        // ---- Mode 1: Autonomous Goal-Driven Execution (--goal) ----
        if (opts.goal) {
            const goalEngineDesc = opts.healEngine === "ollama" ? `local ${opts.model}` : `${opts.opencodeModel} (fallback: local ${opts.model})`;
            console.log(`[guided-hands] Goal mode (max ${opts.maxTurns} turns, planner=${goalEngineDesc}): "${opts.goal}"`);
            const history = [];
            const recordedSteps = [];
            let goalCompleted = false;

            for (let turn = 1; turn <= opts.maxTurns; turn++) {
                currentStepIndex = turn - 1;
                const snap = await doCaptureSnapshot("");
                const blocker = detectPageBlocker(snap, false);
                if (blocker?.type === "login_redirect" && activeAuth.user && activeAuth.pass) {
                    console.log(`  [goal turn ${turn}] Auto-logging in as "${activeAuth.user}"...`);
                    await doLogin(activeAuth.user, activeAuth.pass);
                    await doNavigate(lastIntendedUrl);
                    continue;
                }
                if (blocker?.fatal) {
                    throw new Error(`Goal mode aborted: ${blocker.message}`);
                }

                await doHud(`Guided Hands Goal (${turn}/${opts.maxTurns}) · Thinking...`, "active");
                let plan = { ok: false, error: "Skipped OpenCode" };
                if (opts.healEngine !== "ollama" && !opencodeCircuitBroken) {
                    plan = await askOpencodeGoalNextAction({
                        repoRoot,
                        runDir,
                        primaryModel: opts.opencodeModel,
                        goal: opts.goal,
                        turn,
                        maxTurns: opts.maxTurns,
                        history,
                        snapshot: snap
                    });
                }
                if (!plan.ok && opts.healEngine !== "opencode") {
                    plan = await askOllamaGoalNextAction({
                        goal: opts.goal,
                        turn,
                        maxTurns: opts.maxTurns,
                        history,
                        snapshot: snap,
                        ollamaOpts
                    }).catch((err) => ({ ok: false, error: err.message }));
                }
                if (!plan.ok || !plan.data?.action) {
                    throw new Error(`Goal planner failed at turn ${turn}: ${plan.error || "missing action"}`);
                }

                const nextStep = plan.data;
                currentStepObj = nextStep;
                console.log(`[Goal ${turn}/${opts.maxTurns}] ${nextStep.action} — ${nextStep.reason || ""} (${plan.stats.model}, ${plan.stats.durationMs}ms)`);

                if (nextStep.action === "done") {
                    report.steps.push({ step: turn, action: "done", ok: true, detail: { summary: nextStep.summary || nextStep.reason } });
                    goalCompleted = true;
                    break;
                }
                if (nextStep.action === "fail") {
                    throw new Error(`Goal mode reported failure: ${nextStep.summary || nextStep.reason || "Goal could not be completed"}`);
                }
                if (!SAFE_RECOVERY_ACTIONS.has(nextStep.action)) {
                    throw new Error(`Goal planner returned disallowed action: ${nextStep.action}`);
                }
                if (!stepAllowsDestructive(nextStep, opts.goal) && isDestructiveCandidate(`${nextStep.target || ""} ${nextStep.selector || ""}`)) {
                    throw new Error(`Goal planner attempted destructive action not requested by goal: ${nextStep.target || nextStep.selector}`);
                }

                const cleanStep = { action: nextStep.action };
                for (const k of ["url", "target", "selector", "value", "text", "urlIncludes", "scenario", "overrides", "fixture", "file", "ms", "scope", "noValidationErrors"]) {
                    if (nextStep[k] !== undefined && nextStep[k] !== null && nextStep[k] !== "") {
                        cleanStep[k] = nextStep[k];
                    }
                }

                try {
                    const detail = await executeStepAction(cleanStep, turn);
                    recordedSteps.push(cleanStep);
                    history.push({ turn, step: cleanStep, ok: true });
                    report.steps.push({ step: turn, action: cleanStep.action, ok: true, detail, llmStats: plan.stats });
                } catch (stepErr) {
                    history.push({ turn, step: cleanStep, ok: false, error: stepErr.message });
                    report.steps.push({ step: turn, action: cleanStep.action, ok: false, detail: { error: stepErr.message } });
                    console.log(`  [goal turn ${turn}] Action failed (${stepErr.message}); planner will adapt on next turn.`);
                }
            }

            if (!goalCompleted) {
                throw new Error(`Goal mode reached max turns (${opts.maxTurns}) without completing goal.`);
            }
            opts.steps = recordedSteps;
        } else {
            // ---- Mode 2: Deterministic / Self-Healing Step Runner ----
            const startIdx = Math.max(0, opts.fromStep - 1);
            if (startIdx >= opts.steps.length && opts.steps.length > 0) {
                throw new Error(`--from-step ${opts.fromStep} exceeds total step count (${opts.steps.length}).`);
            }

            for (let i = startIdx; i < opts.steps.length; i++) {
                const step = opts.steps[i];
                const stepNum = i + 1;
                currentStepIndex = i;
                currentStepObj = step;
                const errStart = { page: pageErrors.length, console: consoleErrors.length, net: failedRequests.length };
                const stepResult = { step: stepNum, action: step.action, ok: true, detail: {} };

                const stepLabel = `[Step ${stepNum}/${opts.steps.length}] ${step.action} ${step.target || step.selector || step.scenario || step.url || step.text || step.account || ""}`;
                console.log(stepLabel);
                const elapsed = ((Date.now() - runStartMs) / 1000).toFixed(1);
                await doHud(`Guided Hands (${elapsed}s) · ${stepLabel.slice(0, 88)}`, "active");

                stepResult.detail = await runStepWithRecovery(step, stepNum);

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

                const postFb = await getFeedback().catch(() => null);
                saveCheckpoint(stepNum, postFb?.url || lastIntendedUrl);

                if (opts.slowMo > 0) {
                    await new Promise((r) => setTimeout(r, opts.slowMo));
                }
            }
        }

        const totalSec = ((Date.now() - runStartMs) / 1000).toFixed(1);
        await doHud(
            `Guided Hands DONE in ${totalSec}s · ${report.steps.length} steps, ${report.recoveries.length} healed, ${report.errors.length} errors`,
            report.errors.length === 0 ? "done" : "error"
        );

        await doScreenshot(path.join(runDir, "final.png"));
        report.finalFeedback = await getFeedback();
        if (report.finalFeedback.hasServerError) {
            report.errors.push("Final page shows an ASP.NET server error.");
        }
    } catch (err) {
        report.errors.push(err.message);
        // Tier 3: Rich 1-turn Failure Dossier (URL, DOM snapshot, visible controls, screenshot)
        try {
            const failShotPath = path.join(runDir, `failure-step-${Math.max(1, currentStepIndex + 1)}.png`);
            const shot = await (ext ? ext.captureScreenshot(failShotPath).then((ok) => (ok ? failShotPath : null)) : (cdp ? cdp.send("Page.captureScreenshot", { format: "png" }).then((img) => { fs.writeFileSync(failShotPath, Buffer.from(img.data, "base64")); return failShotPath; }) : Promise.resolve(null))).catch(() => null);
            if (shot) report.screenshots.push(shot);
            const snap = await (ext ? ext.captureSnapshot("") : (cdp ? captureSnapshotCdp(cdp, "") : Promise.resolve(null))).catch(() => null);
            report.failureSnapshot = {
                failedStep: currentStepIndex >= 0 ? currentStepIndex + 1 : null,
                stepConfig: currentStepObj || null,
                error: err.message,
                screenshot: shot,
                page: snap ? formatCompactSnapshotForLlm(snap) : null
            };
        } catch {
            // Best-effort failure snapshot
        }
        if (ext) {
            await ext.setIndicator(`Guided Hands ERROR · ${err.message.slice(0, 70)}`, "error").catch(() => {});
        }
    } finally {
        const runFailed = report.errors.length > 0;
        if (runFailed && !opts.keepOpen && ext) {
            await ext.closeTab();
        }
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

    if (report.passed && loadedPlaybooks.length > 0) {
        const runsPath = path.join(repoRoot, "artifacts", "proof-verify", "playbook-runs.local.json");
        let runs = {};
        try {
            if (fs.existsSync(runsPath)) {
                runs = JSON.parse(fs.readFileSync(runsPath, "utf8")) || {};
            }
        } catch {
            runs = {};
        }
        for (const pb of loadedPlaybooks) {
            const key = pb.doc?.name || path.relative(path.join(repoRoot, ".agents", "skills", "proof-verify", "playbooks"), pb.path).replace(/\\/g, "/");
            runs[key] = {
                lastPassedAt: report.timestamp,
                ...(opts.preset ? { preset: opts.preset } : {}),
                steps: (pb.doc?.steps || []).length
            };
        }
        fs.mkdirSync(path.dirname(runsPath), { recursive: true });
        fs.writeFileSync(runsPath, JSON.stringify(runs, null, 2) + "\n", "utf8");
    }

    if (report.passed && !opts.noUpdatePlaybook && loadedPlaybooks.length > 1 && !opts.savePlaybook) {
        const byPath = new Map();
        for (const pb of loadedPlaybooks) {
            if (!byPath.has(pb.path)) {
                byPath.set(pb.path, { doc: pb.doc, steps: JSON.parse(JSON.stringify(pb.doc.steps || [])), patched: 0 });
            }
        }
        opts.steps.forEach((s, idx) => {
            const entry = s._sourcePlaybookPath ? byPath.get(s._sourcePlaybookPath) : null;
            if (!entry || !Number.isInteger(s._sourceStepIndex)) return;
            const targetIdx = s._sourceStepIndex;
            const base = s._patchedStep ? { ...s._patchedStep } : { ...(entry.steps[targetIdx] || s) };
            if (s._patchedStep) {
                report.patchedSteps.push({ step: idx + 1, playbook: path.basename(s._sourcePlaybookPath), patchedTo: base });
                entry.patched++;
            }
            if (s._capturedKnownFields) {
                base.knownFields = s._capturedKnownFields;
            }
            delete base._capturedKnownFields;
            delete base._patchedStep;
            delete base._sourcePlaybookPath;
            delete base._sourceStepIndex;
            entry.steps[targetIdx] = base;
        });
        let updatedCount = 0;
        for (const [pbPath, entry] of byPath.entries()) {
            const stepsChanged = JSON.stringify(entry.steps) !== JSON.stringify(entry.doc.steps || []);
            if (!stepsChanged) continue;
            const { lastPassedAt: _ignored, ...restDoc } = entry.doc;
            const updatedDoc = { ...restDoc, steps: entry.steps };
            fs.writeFileSync(pbPath, JSON.stringify(updatedDoc, null, 2) + "\n", "utf8");
            updatedCount++;
        }
        if (updatedCount > 0) {
            console.log(`[guided-hands] Updated ${updatedCount} chained module playbook(s)${report.patchedSteps.length ? ` (${report.patchedSteps.length} step(s) auto-patched)` : ""}`);
        }
    } else if (report.passed && savePlaybookPath) {
        fs.mkdirSync(path.dirname(savePlaybookPath), { recursive: true });
        const updatedSteps = opts.steps.map((s, idx) => {
            const base = s._patchedStep ? { ...s._patchedStep } : { ...s };
            if (s._patchedStep) {
                report.patchedSteps.push({ step: idx + 1, patchedTo: base });
            }
            if (s._capturedKnownFields) {
                base.knownFields = s._capturedKnownFields;
            }
            delete base._capturedKnownFields;
            delete base._patchedStep;
            delete base._sourcePlaybookPath;
            delete base._sourceStepIndex;
            return base;
        });
        const isExplicitSave = Boolean(opts.savePlaybook);
        const stepsChanged = !loadedPlaybook || JSON.stringify(updatedSteps) !== JSON.stringify(loadedPlaybook.steps || []);
        if (isExplicitSave || stepsChanged) {
            const playbookDoc = {
                name: loadedPlaybook?.name || path.basename(savePlaybookPath, ".json"),
                description: loadedPlaybook?.description || (opts.goal ? `Goal playbook: ${opts.goal}` : `Auto-recorded Guided Hands playbook for ${opts.url}`),
                url: loadedPlaybook?.url || opts.url,
                ...(opts.account || loadedPlaybook?.account ? { account: opts.account || loadedPlaybook?.account } : {}),
                ...(loadedPlaybook?.vars ? { vars: loadedPlaybook.vars } : {}),
                notes: loadedPlaybook?.notes || [],
                steps: updatedSteps
            };
            fs.writeFileSync(savePlaybookPath, JSON.stringify(playbookDoc, null, 2) + "\n", "utf8");
            console.log(`[guided-hands] Playbook saved/updated: ${savePlaybookPath}${report.patchedSteps.length ? ` (${report.patchedSteps.length} step(s) auto-patched)` : ""}`);
        }
    }

    const latestPath = path.join(repoRoot, "artifacts", "proof-verify", "latest-guided-hands.json");
    const runPath = path.join(runDir, "report.json");
    const jsonOut = JSON.stringify(report, null, 2) + "\n";
    fs.writeFileSync(runPath, jsonOut, "utf8");
    fs.writeFileSync(latestPath, jsonOut, "utf8");

    console.log(`\n[guided-hands] OVERALL: ${report.passed ? "PASS" : "FAIL"} (${report.steps.length} step(s), ${report.recoveries.length} healed, ${report.errors.length} error(s))`);
    console.log(`[guided-hands] Report: ${latestPath}`);
    if (report.recoveries.length > 0) {
        console.log("[guided-hands] Autonomous recoveries:");
        for (const r of report.recoveries) {
            console.log(`  - Step ${r.step} [${r.tier}]: ${r.originalError} -> ${r.reason || JSON.stringify(r.actionTaken)}`);
        }
    }
    if (report.errors.length > 0) {
        for (const e of report.errors) console.error(`  - ERROR: ${e}`);
        if (report.failureSnapshot?.page) {
            const p = report.failureSnapshot.page;
            console.error(`  - SNAPSHOT URL: ${p.url} (title="${p.title}", modal=${p.inModal}, clickables=${p.clickables.length}, fields=${p.fields.length})`);
            if (p.swal) console.error(`  - SNAPSHOT SWAL: ${p.swal.title} — ${p.swal.text}`);
            if (report.failureSnapshot.screenshot) console.error(`  - SNAPSHOT PNG: ${report.failureSnapshot.screenshot}`);
        }
    }
    process.exit(report.passed ? 0 : 1);
}

main().catch((err) => {
    console.error(`[guided-hands] Fatal: ${err.stack || err.message}`);
    process.exit(1);
});
