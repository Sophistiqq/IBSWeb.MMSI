#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { spawn, execFileSync } from "node:child_process";

const DEFAULT_PORT = 5073;
const DEFAULT_LINEAR_TEAM = "IBS-MSAP";
const DEFAULT_TEXT_MODELS = [
    "opencode/glm-5.3-flash-free",
    "opencode/deepseek-v4.1-flash-free",
    "opencode/mimo-v2.6-flash-free"
];
const DEFAULT_VISION_MODEL = "opencode/nemotron-3.5-super-vl-free";

const BROWSER_CANDIDATES = [
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"
];

const VIEWPORTS = [
    { name: "desktop", width: 1440, height: 900, deviceScaleFactor: 1, mobile: false },
    { name: "mobile", width: 390, height: 844, deviceScaleFactor: 2, mobile: true }
];

const TEXT_EXTENSIONS = new Set([
    ".cs", ".cshtml", ".js", ".mjs", ".css", ".json", ".yml", ".yaml",
    ".md", ".csproj", ".sln", ".props", ".targets", ".config", ".xml"
]);

function printUsage() {
    console.log(`Usage: node .agents/skills/proof-verify/scripts/proof-verify.mjs [options]

Options:
  --urls <list>            Comma-separated routes or URLs to verify (e.g. /MSAP/Billing/Index)
  --port <number>          Port for local IBSWeb server (default: 5073)
  --base-url <url>         Base URL override (default: http://localhost:<port>)
  --force-build            Force dotnet build even if only .cshtml/.js/.css changed
  --skip-build             Skip dotnet build in Stage 1
  --skip-runtime           Skip Stage 2 headless browser proof
  --skip-ai                Skip Stage 3 OpenCode model judges
  --user <username>        Login username (or set IBS_VERIFY_USER env var)
  --pass <password>        Login password (or set IBS_VERIFY_PASS env var)
  --interactive-login      Launch visible browser to log in and cache cookies in profile
  --model <id>             Primary OpenCode text/code model (default: opencode/glm-5.3-flash-free)
  --vision-model <id>      OpenCode vision model for screenshots (default: opencode/nemotron-3.5-super-vl-free)
  --linear-issue <id>      Target Linear issue identifier (e.g. IBS-126)
  --linear-team <name>     Linear team name/key (default: IBS-MSAP)
  --linear-mode <mode>     auto | issue | comment (default: auto)
  --log-linear             Post directly to Linear API if LINEAR_API_KEY is set, or emit MCP handoff
  -h, --help               Show this help message
`);
}

function parseArgs(argv) {
    const opts = {
        urls: [],
        port: DEFAULT_PORT,
        baseUrl: null,
        forceBuild: false,
        skipBuild: false,
        skipRuntime: false,
        skipAi: false,
        user: process.env.IBS_VERIFY_USER || "",
        pass: process.env.IBS_VERIFY_PASS || "",
        interactiveLogin: false,
        model: DEFAULT_TEXT_MODELS[0],
        visionModel: DEFAULT_VISION_MODEL,
        linearIssue: "",
        linearTeam: DEFAULT_LINEAR_TEAM,
        linearMode: "auto",
        logLinear: false,
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
        } else if (arg === "--urls") {
            opts.urls.push(...next().split(",").map((s) => s.trim()).filter(Boolean));
        } else if (arg.startsWith("--urls=")) {
            opts.urls.push(...arg.slice("--urls=".length).split(",").map((s) => s.trim()).filter(Boolean));
        } else if (arg === "--port") {
            opts.port = Number.parseInt(next(), 10);
        } else if (arg.startsWith("--port=")) {
            opts.port = Number.parseInt(arg.slice("--port=".length), 10);
        } else if (arg === "--base-url") {
            opts.baseUrl = next().replace(/\/+$/, "");
        } else if (arg.startsWith("--base-url=")) {
            opts.baseUrl = arg.slice("--base-url=".length).replace(/\/+$/, "");
        } else if (arg === "--force-build") {
            opts.forceBuild = true;
        } else if (arg === "--skip-build") {
            opts.skipBuild = true;
        } else if (arg === "--skip-runtime") {
            opts.skipRuntime = true;
        } else if (arg === "--skip-ai") {
            opts.skipAi = true;
        } else if (arg === "--interactive-login") {
            opts.interactiveLogin = true;
        } else if (arg === "--user") {
            opts.user = next();
        } else if (arg.startsWith("--user=")) {
            opts.user = arg.slice("--user=".length);
        } else if (arg === "--pass") {
            opts.pass = next();
        } else if (arg.startsWith("--pass=")) {
            opts.pass = arg.slice("--pass=".length);
        } else if (arg === "--model") {
            opts.model = next();
        } else if (arg.startsWith("--model=")) {
            opts.model = arg.slice("--model=".length);
        } else if (arg === "--vision-model") {
            opts.visionModel = next();
        } else if (arg.startsWith("--vision-model=")) {
            opts.visionModel = arg.slice("--vision-model=".length);
        } else if (arg === "--linear-issue") {
            opts.linearIssue = next().trim();
        } else if (arg.startsWith("--linear-issue=")) {
            opts.linearIssue = arg.slice("--linear-issue=".length).trim();
        } else if (arg === "--linear-team") {
            opts.linearTeam = next().trim() || DEFAULT_LINEAR_TEAM;
        } else if (arg.startsWith("--linear-team=")) {
            opts.linearTeam = arg.slice("--linear-team=".length).trim() || DEFAULT_LINEAR_TEAM;
        } else if (arg === "--linear-mode") {
            opts.linearMode = next().trim().toLowerCase();
        } else if (arg.startsWith("--linear-mode=")) {
            opts.linearMode = arg.slice("--linear-mode=".length).trim().toLowerCase();
        } else if (arg === "--log-linear") {
            opts.logLinear = true;
        } else {
            throw new Error(`Unknown option: ${arg}`);
        }
    }

    if (!["auto", "issue", "comment"].includes(opts.linearMode)) {
        throw new Error(`Invalid --linear-mode: ${opts.linearMode} (expected auto, issue, or comment)`);
    }
    if (!Number.isFinite(opts.port) || opts.port <= 0) {
        throw new Error(`Invalid port: ${opts.port}`);
    }
    if (!opts.baseUrl) {
        opts.baseUrl = `http://localhost:${opts.port}`;
    }
    return opts;
}

function getRepoRoot() {
    const out = execFileSync("git", ["rev-parse", "--show-toplevel"], { encoding: "utf8" });
    return path.resolve(out.trim());
}

function runGit(repoRoot, args) {
    try {
        const stdout = execFileSync("git", args, {
            cwd: repoRoot,
            encoding: "utf8",
            maxBuffer: 1024 * 1024 * 20
        });
        return { ok: true, stdout, stderr: "" };
    } catch (err) {
        return {
            ok: false,
            stdout: err.stdout ? String(err.stdout) : "",
            stderr: err.stderr ? String(err.stderr) : err.message
        };
    }
}

function parseGitStatusPathsZ(statusOut) {
    const files = new Set();
    const tokens = statusOut.split("\0");
    for (let i = 0; i < tokens.length; i++) {
        const token = tokens[i];
        if (!token || token.length < 4) {
            continue;
        }
        const xy = token.slice(0, 2);
        const filePath = token.slice(3).replace(/\\/g, "/");
        if (xy.includes("R") || xy.includes("C")) {
            i++; // Skip the original path token emitted after destination in -z mode
        }
        if (filePath && !filePath.startsWith("artifacts/")) {
            files.add(filePath);
        }
    }
    return [...files];
}

function getChangeScope(repoRoot) {
    const statusRes = runGit(repoRoot, ["status", "--porcelain=v1", "-uall", "-z"]);
    let workingFiles = parseGitStatusPathsZ(statusRes.stdout);
    let scopeMode = "working-tree";

    if (workingFiles.length === 0) {
        const headFilesRes = runGit(repoRoot, ["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", "HEAD"]);
        workingFiles = headFilesRes.stdout
            .split("\0")
            .map((s) => s.trim().replace(/\\/g, "/"))
            .filter((s) => s && !s.startsWith("artifacts/"));
        scopeMode = workingFiles.length > 0 ? "last-commit" : "clean";
    }

    const untrackedRes = runGit(repoRoot, ["ls-files", "--others", "--exclude-standard", "-z"]);
    const untrackedSet = new Set(
        untrackedRes.stdout
            .split("\0")
            .map((s) => s.trim().replace(/\\/g, "/"))
            .filter(Boolean)
    );

    const backendFiles = [];
    const razorFiles = [];
    const scriptFiles = [];
    const styleFiles = [];
    const msapFiles = [];

    for (const file of workingFiles) {
        const lower = file.toLowerCase();
        if (/\.(cs|csproj|sln|props|targets)$/i.test(lower)) {
            backendFiles.push(file);
        } else if (lower.endsWith(".cshtml")) {
            razorFiles.push(file);
        } else if (lower.endsWith(".js") || lower.endsWith(".mjs")) {
            scriptFiles.push(file);
        } else if (lower.endsWith(".css")) {
            styleFiles.push(file);
        }

        if (/^ibsweb\/(areas\/msap|areas\/msapadmin|wwwroot\/msap)\//i.test(lower)) {
            msapFiles.push(file);
        }
    }

    return {
        scopeMode,
        files: workingFiles,
        untrackedSet,
        backendFiles,
        razorFiles,
        scriptFiles,
        styleFiles,
        msapFiles
    };
}

function getFilteredDiff(repoRoot, scope) {
    if (scope.scopeMode === "clean") {
        return "";
    }

    const filterTargets = scope.files.filter(
        (f) => !/\.Designer\.cs$/i.test(f) && !/ModelSnapshot\.cs$/i.test(f) && !f.startsWith("artifacts/")
    );
    const omitted = scope.files.filter((f) => !filterTargets.includes(f));

    let diffText = "";
    if (scope.scopeMode === "working-tree") {
        if (filterTargets.length > 0) {
            const trackedRes = runGit(repoRoot, ["diff", "HEAD", "--", ...filterTargets]);
            diffText += trackedRes.stdout;
        }
        for (const f of filterTargets) {
            if (scope.untrackedSet.has(f)) {
                const abs = path.join(repoRoot, f);
                if (fs.existsSync(abs) && fs.statSync(abs).isFile()) {
                    const content = fs.readFileSync(abs, "utf8");
                    diffText += `\n--- /dev/null\n+++ b/${f}\n${content}\n`;
                }
            }
        }
    } else if (scope.scopeMode === "last-commit" && filterTargets.length > 0) {
        const showRes = runGit(repoRoot, ["show", "--format=medium", "HEAD", "--", ...filterTargets]);
        diffText = showRes.stdout;
    }

    if (omitted.length > 0) {
        diffText = `[Omitted generated/snapshot files: ${omitted.join(", ")}]\n\n` + diffText;
    }

    const MAX_DIFF_CHARS = 65000;
    if (diffText.length > MAX_DIFF_CHARS) {
        diffText = diffText.slice(0, MAX_DIFF_CHARS) + "\n\n...[diff truncated at 65,000 chars]...\n";
    }
    return diffText;
}

function stripJsComments(code) {
    return code
        .replace(/\/\*[\s\S]*?\*\//g, "")
        .replace(/(^|[^:\\])\/\/.*$/gm, "$1");
}

function sanitizeRazorScriptForSyntaxCheck(rawScript) {
    let s = rawScript;
    s = s.replace(/@\*[\s\S]*?\*@/g, "/* razor comment */");
    s = s.replace(/@@/g, "__AT__");
    s = s.replace(/@:/g, "");
    s = s.replace(/@foreach\s*\([^)]*\)/g, "for (const __razorItem of __RAZOR_EXPR__)");
    s = s.replace(/@(if|for|while|switch|catch)\b/g, "$1");
    s = s.replace(/@else\b/g, "else");

    // Iteratively collapse @( ... ) balanced expressions
    for (let pass = 0; pass < 4; pass++) {
        let changed = false;
        let out = "";
        for (let i = 0; i < s.length; i++) {
            if (s[i] === "@" && s[i + 1] === "(") {
                let depth = 1;
                let j = i + 2;
                while (j < s.length && depth > 0) {
                    if (s[j] === "(") depth++;
                    else if (s[j] === ")") depth--;
                    j++;
                }
                if (depth === 0) {
                    out += "__RAZOR_EXPR__";
                    i = j - 1;
                    changed = true;
                    continue;
                }
            }
            out += s[i];
        }
        s = out;
        if (!changed) break;
    }

    // Replace @Method(...) and @Identifier.Member chains
    s = s.replace(/@[A-Za-z_][A-Za-z0-9_.]*(?:\([^()\n]*\))?/g, "__RAZOR_EXPR__");
    return s;
}

function extractInlineScripts(cshtmlContent) {
    const blocks = [];
    const regex = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
    let match;
    while ((match = regex.exec(cshtmlContent)) !== null) {
        const attrs = match[1] || "";
        const body = match[2] || "";
        if (/\bsrc\s*=/i.test(attrs)) {
            continue;
        }
        const typeMatch = attrs.match(/\btype\s*=\s*["']([^"']+)["']/i);
        if (typeMatch) {
            const typeVal = typeMatch[1].toLowerCase().trim();
            if (typeVal !== "text/javascript" && typeVal !== "application/javascript" && typeVal !== "module") {
                continue;
            }
        }
        if (!body.trim()) {
            continue;
        }
        const lineNumber = cshtmlContent.slice(0, match.index).split("\n").length;
        blocks.push({ lineNumber, body });
    }
    return blocks;
}

function checkCshtmlNeedsBuild(repoRoot, scope) {
    if (scope.razorFiles.length === 0) {
        return false;
    }
    for (const file of scope.razorFiles) {
        if (scope.untrackedSet.has(file)) {
            return true;
        }
        const diffRes = scope.scopeMode === "working-tree"
            ? runGit(repoRoot, ["diff", "HEAD", "--", file])
            : runGit(repoRoot, ["show", "HEAD", "--", file]);
        const changedLines = diffRes.stdout
            .split(/\r?\n/)
            .filter((line) => (line.startsWith("+") || line.startsWith("-")) && !line.startsWith("+++") && !line.startsWith("---"));
        for (const line of changedLines) {
            if (/^[+-]\s*@(model|inject|using|inherits|functions|code|\{)/i.test(line)) {
                return true;
            }
        }
    }
    return false;
}

function runCommandSync(command, args, cwd) {
    try {
        const stdout = execFileSync(command, args, {
            cwd,
            encoding: "utf8",
            maxBuffer: 1024 * 1024 * 20,
            stdio: ["ignore", "pipe", "pipe"]
        });
        return { ok: true, code: 0, output: stdout };
    } catch (err) {
        const out = [err.stdout ? String(err.stdout) : "", err.stderr ? String(err.stderr) : ""]
            .filter(Boolean)
            .join("\n");
        return { ok: false, code: err.status ?? 1, output: out || err.message };
    }
}

function runStage1StaticChecks(repoRoot, scope, opts) {
    const errors = [];
    const warnings = [];
    const notes = [];
    let didBuild = false;

    // 1. Git diff whitespace & conflict marker check
    if (scope.scopeMode === "working-tree") {
        const diffCheck = runGit(repoRoot, ["diff", "--check"]);
        if (!diffCheck.ok && diffCheck.stdout.trim()) {
            errors.push(`git diff --check found whitespace/conflict issues:\n${diffCheck.stdout.trim()}`);
        }
        const cachedCheck = runGit(repoRoot, ["diff", "--cached", "--check"]);
        if (!cachedCheck.ok && cachedCheck.stdout.trim()) {
            errors.push(`git diff --cached --check found issues:\n${cachedCheck.stdout.trim()}`);
        }
    }

    // 2. .editorconfig line endings and trailing newline check on working-tree files
    if (scope.scopeMode === "working-tree") {
        for (const relPath of scope.files) {
            const ext = path.extname(relPath).toLowerCase();
            if (!TEXT_EXTENSIONS.has(ext)) {
                continue;
            }
            const absPath = path.join(repoRoot, relPath);
            if (!fs.existsSync(absPath) || !fs.statSync(absPath).isFile()) {
                continue;
            }
            const content = fs.readFileSync(absPath, "utf8");
            if (content.includes("\r\n")) {
                errors.push(`.editorconfig violation (CRLF line endings): ${relPath}`);
            }
            if (content.length > 0 && !content.endsWith("\n")) {
                errors.push(`.editorconfig violation (missing final newline): ${relPath}`);
            }
        }
    }

    // 3. Smart build gate
    const needsBuild = !opts.skipBuild && (
        opts.forceBuild ||
        scope.backendFiles.length > 0 ||
        checkCshtmlNeedsBuild(repoRoot, scope)
    );

    if (needsBuild) {
        console.log("[Stage 1] Running dotnet build (backend or Razor C# directives changed)...");
        const buildRes = runCommandSync(
            "dotnet",
            ["build", "Integrated Business System.sln", "-v", "minimal"],
            repoRoot
        );
        if (!buildRes.ok) {
            errors.push(`dotnet build failed:\n${buildRes.output.slice(-4000)}`);
        } else {
            didBuild = true;
            notes.push("dotnet build succeeded.");
        }
    } else if (opts.skipBuild) {
        notes.push("dotnet build skipped (--skip-build).");
    } else {
        notes.push("dotnet build skipped (no .cs/.csproj or Razor @model/@inject/@using/@{} edits detected).");
    }

    // 4. JavaScript syntax checks (.js and inline <script> inside .cshtml)
    const filesToCheckSyntax = scope.scopeMode === "working-tree"
        ? [...scope.scriptFiles, ...scope.razorFiles]
        : [...scope.scriptFiles, ...scope.razorFiles.filter((f) => !/Migrations/i.test(f))];

    for (const relPath of filesToCheckSyntax) {
        const absPath = path.join(repoRoot, relPath);
        if (!fs.existsSync(absPath) || !fs.statSync(absPath).isFile()) {
            continue;
        }
        const content = fs.readFileSync(absPath, "utf8");

        if (relPath.toLowerCase().endsWith(".js") || relPath.toLowerCase().endsWith(".mjs")) {
            if (/^\s*(import|export)\b/m.test(content)) {
                const nodeCheck = runCommandSync("node", ["--check", absPath], repoRoot);
                if (!nodeCheck.ok) {
                    errors.push(`JS syntax error in ${relPath}:\n${nodeCheck.output}`);
                }
            } else {
                try {
                    new vm.Script(content, { filename: relPath });
                } catch (err) {
                    errors.push(`JS syntax error in ${relPath}: ${err.message}`);
                }
            }
        } else if (relPath.toLowerCase().endsWith(".cshtml")) {
            const blocks = extractInlineScripts(content);
            for (const block of blocks) {
                const sanitized = sanitizeRazorScriptForSyntaxCheck(block.body);
                try {
                    new vm.Script(sanitized, { filename: `${relPath}:${block.lineNumber}` });
                } catch (err) {
                    if (/@(if|foreach|for|switch)\b/.test(block.body)) {
                        warnings.push(
                            `Inline <script> at ${relPath}:${block.lineNumber} contains Razor control flow and could not be statically parsed (${err.message}). Verify at runtime in Stage 2.`
                        );
                    } else {
                        errors.push(
                            `Inline <script> JS syntax error in ${relPath} (around line ${block.lineNumber}): ${err.message}`
                        );
                    }
                }
            }

            // Static DOM ID reference check (advisory warning for getElementById literals not declared locally)
            const declaredIds = new Set();
            for (const m of content.matchAll(/\bid\s*=\s*["']([A-Za-z0-9_-]+)["']/g)) {
                declaredIds.add(m[1]);
            }
            const layoutKnownIds = new Set(["hfUsername", "favicon-light", "favicon-dark"]);
            for (const block of blocks) {
                for (const m of block.body.matchAll(/document\.getElementById\(\s*["']([A-Za-z0-9_-]+)["']\s*\)/g)) {
                    const targetId = m[1];
                    if (!declaredIds.has(targetId) && !layoutKnownIds.has(targetId)) {
                        warnings.push(
                            `${relPath}: document.getElementById('${targetId}') not found as a static id in the same file (verify if rendered by a partial/layout).`
                        );
                    }
                }
            }
        }
    }

    // 5. MSAP_AGENTS.md house-component compliance checks
    for (const relPath of scope.msapFiles) {
        const lower = relPath.toLowerCase();
        if (!lower.endsWith(".cshtml") && !lower.endsWith(".js")) {
            continue;
        }
        const baseName = path.basename(lower);
        if (["modern-alert.js", "modern-table.js", "modern-select.js", "modern-navbar.js", "tutorial.js"].includes(baseName)) {
            continue;
        }
        const absPath = path.join(repoRoot, relPath);
        if (!fs.existsSync(absPath) || !fs.statSync(absPath).isFile()) {
            continue;
        }
        const content = fs.readFileSync(absPath, "utf8");
        const scriptsToInspect = lower.endsWith(".cshtml")
            ? extractInlineScripts(content).map((b) => b.body).join("\n")
            : content;
        const cleanJs = stripJsComments(scriptsToInspect);

        if (/\b(?:window\.)?(?:alert|confirm|prompt)\s*\(/.test(cleanJs)) {
            errors.push(`MSAP_AGENTS.md violation in ${relPath}: native alert/confirm/prompt detected; use ModernAlert.`);
        }
        if (/\bSwal\.fire\s*\(/.test(cleanJs)) {
            errors.push(`MSAP_AGENTS.md violation in ${relPath}: direct Swal.fire(...) detected; use ModernAlert.`);
        }
        if (/\btoastr\.(?:success|error|warning|info)\s*\(/.test(cleanJs)) {
            errors.push(`MSAP_AGENTS.md violation in ${relPath}: raw toastr call detected; use ModernAlert.`);
        }
        if (/\.DataTable\s*\(\s*\{/.test(cleanJs)) {
            errors.push(`MSAP_AGENTS.md violation in ${relPath}: raw .DataTable({...}) detected; use ModernTable.config(...).`);
        }
        if (/\$\([^)]+\)\.select2\s*\(/.test(cleanJs)) {
            errors.push(`MSAP_AGENTS.md violation in ${relPath}: direct .select2(...) detected; use .js-modern-select / ModernSelect.`);
        }

        if (lower.endsWith(".cshtml") && !baseName.startsWith("_") && !lower.includes("/shared/")) {
            if ((/@model\b/.test(content) || /ViewData\["Title"\]/.test(content)) && !/modern-layout/.test(content)) {
                warnings.push(`MSAP_AGENTS.md check in ${relPath}: top-level view does not reference .modern-layout.`);
            }
        }
    }

    return {
        passed: errors.length === 0,
        didBuild,
        errors,
        warnings,
        notes
    };
}

function inferRoutesFromChanges(scope) {
    const inferred = [];
    const seen = new Set();

    for (const relPath of scope.razorFiles) {
        const normalized = relPath.replace(/\\/g, "/");
        let route = null;

        const areaMatch = normalized.match(/^IBSWeb\/Areas\/([^/]+)\/Views\/([^/]+)\/([^/]+)\.cshtml$/i);
        if (areaMatch) {
            const [, area, controller, view] = areaMatch;
            if (!view.startsWith("_") && controller.toLowerCase() !== "shared") {
                if (view.toLowerCase() === "index") {
                    route = `/${area}/${controller}`;
                } else if (/^(create|import|upload|settings|dashboard)$/i.test(view)) {
                    route = `/${area}/${controller}/${view}`;
                } else {
                    route = `/${area}/${controller}`;
                }
            }
        }

        const identityMatch = normalized.match(/^IBSWeb\/Areas\/Identity\/Pages\/(.+)\.cshtml$/i);
        if (identityMatch) {
            const pagePath = identityMatch[1];
            const pageName = path.basename(pagePath);
            if (!pageName.startsWith("_")) {
                route = `/Identity/${pagePath}`;
            }
        }

        const rootViewMatch = normalized.match(/^IBSWeb\/Views\/([^/]+)\/([^/]+)\.cshtml$/i);
        if (rootViewMatch) {
            const [, controller, view] = rootViewMatch;
            if (!view.startsWith("_") && controller.toLowerCase() !== "shared") {
                route = view.toLowerCase() === "index" ? `/User/${controller}` : `/User/${controller}/${view}`;
            }
        }

        if (route && !seen.has(route)) {
            seen.add(route);
            inferred.push(route);
        }
    }

    if (inferred.length === 0 && (scope.razorFiles.length > 0 || scope.scriptFiles.length > 0 || scope.styleFiles.length > 0)) {
        inferred.push("/Identity/Account/Login");
    }

    return inferred;
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

function killProcessTree(proc) {
    if (!proc || !proc.pid) {
        return;
    }
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

async function ensureAppRunning(repoRoot, baseUrl, port, didBuild) {
    if (await checkHealth(baseUrl, 2000)) {
        console.log(`[Stage 2] App already running at ${baseUrl}`);
        return { startedProc: null, serverLogs: [] };
    }

    console.log(`[Stage 2] App not running. Starting IBSWeb on http://localhost:${port}...`);
    const args = [
        "run",
        "--project",
        "IBSWeb/IBSWeb.csproj",
        "--no-launch-profile",
        "--urls",
        `http://localhost:${port}`
    ];
    if (didBuild) {
        args.push("--no-build");
    }

    const serverLogs = [];
    const pushLog = (chunk) => {
        const lines = String(chunk).split(/\r?\n/).filter(Boolean);
        for (const line of lines) {
            serverLogs.push(line);
            if (serverLogs.length > 120) {
                serverLogs.shift();
            }
        }
    };

    const proc = spawn("dotnet", args, {
        cwd: repoRoot,
        env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Development" },
        stdio: ["ignore", "pipe", "pipe"]
    });

    proc.stdout.on("data", pushLog);
    proc.stderr.on("data", pushLog);

    let exitedEarly = false;
    let exitCode = null;
    proc.on("exit", (code) => {
        exitedEarly = true;
        exitCode = code;
    });

    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        if (exitedEarly) {
            throw new Error(`IBSWeb exited early (code ${exitCode}). Recent logs:\n${serverLogs.slice(-25).join("\n")}`);
        }
        if (await checkHealth(baseUrl, 1500)) {
            console.log(`[Stage 2] IBSWeb is healthy at ${baseUrl}`);
            return { startedProc: proc, serverLogs };
        }
        await new Promise((r) => setTimeout(r, 500));
    }

    killProcessTree(proc);
    throw new Error(`Timed out waiting for IBSWeb /health at ${baseUrl}. Recent logs:\n${serverLogs.slice(-25).join("\n")}`);
}

function findBrowserExecutable() {
    for (const candidate of BROWSER_CANDIDATES) {
        if (fs.existsSync(candidate)) {
            return candidate;
        }
    }
    throw new Error("Could not find Chrome or Microsoft Edge executable on this machine.");
}

class CdpSession {
    constructor(wsUrl) {
        this.wsUrl = wsUrl;
        this.ws = null;
        this.nextId = 1;
        this.pending = new Map();
        this.listeners = new Map();
    }

    async connect() {
        await new Promise((resolve, reject) => {
            const ws = new WebSocket(this.wsUrl);
            this.ws = ws;
            ws.addEventListener("open", () => resolve());
            ws.addEventListener("error", (err) => reject(new Error(`CDP WebSocket error: ${err.message || "connection failed"}`)));
            ws.addEventListener("message", (event) => {
                const msg = JSON.parse(String(event.data));
                if (msg.id) {
                    const entry = this.pending.get(msg.id);
                    if (entry) {
                        this.pending.delete(msg.id);
                        if (msg.error) {
                            entry.reject(new Error(`${entry.method}: ${msg.error.message}`));
                        } else {
                            entry.resolve(msg.result);
                        }
                    }
                } else if (msg.method) {
                    const handlers = this.listeners.get(msg.method);
                    if (handlers) {
                        for (const fn of handlers) {
                            fn(msg.params || {});
                        }
                    }
                }
            });
        });
    }

    on(eventName, handler) {
        if (!this.listeners.has(eventName)) {
            this.listeners.set(eventName, []);
        }
        this.listeners.get(eventName).push(handler);
    }

    off(eventName, handler) {
        const handlers = this.listeners.get(eventName);
        if (!handlers) {
            return;
        }
        this.listeners.set(
            eventName,
            handlers.filter((fn) => fn !== handler)
        );
    }

    send(method, params = {}) {
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
                resolve: (res) => {
                    clearTimeout(timer);
                    resolve(res);
                },
                reject: (err) => {
                    clearTimeout(timer);
                    reject(err);
                }
            });
            this.ws.send(JSON.stringify({ id, method, params }));
        });
    }

    async evaluate(expression) {
        const res = await this.send("Runtime.evaluate", {
            expression,
            returnByValue: true,
            awaitPromise: true
        });
        if (res.exceptionDetails) {
            throw new Error(res.exceptionDetails.text || "Runtime.evaluate exception");
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

async function launchBrowserWithCdp(profileDir, headless) {
    fs.mkdirSync(profileDir, { recursive: true });
    const exe = findBrowserExecutable();
    const args = [
        "--remote-debugging-port=0",
        `--user-data-dir=${profileDir}`,
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-networking",
        "--mute-audio"
    ];
    if (headless) {
        args.push("--headless=new", "--disable-gpu", "--window-size=1440,900");
    } else {
        args.push("--new-window");
    }
    args.push("about:blank");

    const proc = spawn(exe, args, { stdio: ["ignore", "pipe", "pipe"] });

    const devToolsPort = await new Promise((resolve, reject) => {
        let stderr = "";
        const timer = setTimeout(() => {
            reject(new Error(`Timed out waiting for DevTools port from ${exe}.\n${stderr}`));
        }, 15000);

        const onData = (chunk) => {
            stderr += String(chunk);
            const match = stderr.match(/DevTools listening on ws:\/\/127\.0\.0\.1:(\d+)\//);
            if (match) {
                clearTimeout(timer);
                resolve(Number.parseInt(match[1], 10));
            }
        };
        proc.stderr.on("data", onData);
        proc.on("exit", (code) => {
            clearTimeout(timer);
            reject(new Error(`Browser exited before DevTools ready (code ${code}).\n${stderr}`));
        });
    });

    let targets = [];
    for (let attempt = 0; attempt < 20; attempt++) {
        const resp = await fetch(`http://127.0.0.1:${devToolsPort}/json/list`);
        const list = await resp.json();
        targets = list.filter((t) => t.type === "page" && t.webSocketDebuggerUrl);
        if (targets.length > 0) {
            break;
        }
        await new Promise((r) => setTimeout(r, 150));
    }

    if (targets.length === 0) {
        const created = await fetch(`http://127.0.0.1:${devToolsPort}/json/new?about:blank`, { method: "PUT" });
        const page = await created.json();
        targets = [page];
    }

    const cdp = new CdpSession(targets[0].webSocketDebuggerUrl);
    await cdp.connect();
    await cdp.send("Page.enable");
    await cdp.send("Runtime.enable");
    await cdp.send("Log.enable");
    await cdp.send("Network.enable");

    return { proc, cdp, devToolsPort };
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

async function runInteractiveLogin(baseUrl, profileDir) {
    console.log(`[Auth] Launching visible browser at ${baseUrl}/Identity/Account/Login`);
    console.log("[Auth] Sign in and select Remember Me; the script will continue once you leave the login page (or close the browser).");
    const { proc, cdp } = await launchBrowserWithCdp(profileDir, false);
    try {
        await navigateAndWait(cdp, `${baseUrl}/Identity/Account/Login`);
        let closed = false;
        proc.on("exit", () => { closed = true; });
        const deadline = Date.now() + 180000;
        while (!closed && Date.now() < deadline) {
            try {
                const pathname = await cdp.evaluate("location.pathname");
                if (pathname && !pathname.toLowerCase().includes("/identity/account/login")) {
                    console.log(`[Auth] Authenticated! Current path: ${pathname}. Waiting 1.5s to flush cookies...`);
                    await new Promise((r) => setTimeout(r, 1500));
                    break;
                }
            } catch {
                break;
            }
            await new Promise((r) => setTimeout(r, 500));
        }
    } finally {
        cdp.close();
        killProcessTree(proc);
    }
}

async function attemptAutoLogin(cdp, baseUrl, username, password) {
    if (!username || !password) {
        return false;
    }
    console.log("[Stage 2] Redirected to Login; attempting automated credential sign-in...");
    const result = await cdp.evaluate(`(() => {
        const userEl = document.querySelector("#username, input[name='Input.Username']");
        const passEl = document.querySelector("#password, input[name='Input.Password']");
        const remEl = document.querySelector("input[name='Input.RememberMe']");
        const submitEl = document.querySelector("#login-submit, button[type='submit']");
        if (!userEl || !passEl || !submitEl) return false;
        userEl.value = ${JSON.stringify(username)};
        passEl.value = ${JSON.stringify(password)};
        if (remEl) remEl.checked = true;
        submitEl.click();
        return true;
    })()`);

    if (!result) {
        return false;
    }
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        await new Promise((r) => setTimeout(r, 300));
        const pathname = await cdp.evaluate("location.pathname").catch(() => "");
        if (pathname && !pathname.toLowerCase().includes("/identity/account/login")) {
            return true;
        }
    }
    return false;
}

function slugifyRoute(route) {
    const clean = route.replace(/^https?:\/\/[^/]+/i, "").replace(/^\/+|\/+$/g, "");
    return (clean || "home").replace(/[^a-zA-Z0-9_-]+/g, "-").toLowerCase();
}

async function runStage2RuntimeProof(repoRoot, runDir, opts, routes, didBuild) {
    const profileDir = path.join(repoRoot, "artifacts", "proof-verify", ".browser-profile");
    let startedProc = null;
    let browserProc = null;
    let cdp = null;

    const results = {
        passed: true,
        baseUrl: opts.baseUrl,
        routes: [],
        screenshots: [],
        warnings: [],
        errors: []
    };

    try {
        const ignoreCheck = runGit(repoRoot, ["check-ignore", "-q", "artifacts/proof-verify"]);
        if (!ignoreCheck.ok) {
            results.errors.push("Security gate: artifacts/proof-verify is not ignored by .gitignore; refusing to persist browser cookies.");
            results.passed = false;
            return results;
        }

        const server = await ensureAppRunning(repoRoot, opts.baseUrl, opts.port, didBuild);
        startedProc = server.startedProc;

        if (opts.interactiveLogin) {
            await runInteractiveLogin(opts.baseUrl, profileDir);
        }

        if (routes.length === 0) {
            results.warnings.push("No routes specified or inferred for runtime verification.");
            return results;
        }

        const browser = await launchBrowserWithCdp(profileDir, true);
        browserProc = browser.proc;
        cdp = browser.cdp;

        const requestStatuses = new Map();
        const pageErrors = [];
        const consoleErrors = [];
        const failedRequests = [];

        cdp.on("Network.responseReceived", (params) => {
            const url = params.response?.url || "";
            const status = params.response?.status || 0;
            requestStatuses.set(params.requestId, { url, status });
            if (status >= 400 && !url.includes("favicon")) {
                failedRequests.push({ url, status });
            }
        });

        cdp.on("Network.loadingFailed", (params) => {
            if (!params.canceled) {
                failedRequests.push({
                    url: params.type || "unknown",
                    status: 0,
                    error: params.errorText || "loadingFailed"
                });
            }
        });

        cdp.on("Runtime.exceptionThrown", (params) => {
            const desc = params.exceptionDetails?.exception?.description || params.exceptionDetails?.text || "Uncaught exception";
            pageErrors.push(desc);
        });

        cdp.on("Runtime.consoleAPICalled", (params) => {
            if (params.type === "error") {
                const txt = (params.args || []).map((a) => a.value ?? a.description ?? "").join(" ");
                consoleErrors.push(txt || "console.error");
            }
        });

        for (const route of routes) {
            const fullUrl = route.startsWith("http") ? route : `${opts.baseUrl}${route.startsWith("/") ? "" : "/"}${route}`;
            const slug = slugifyRoute(route);
            const routeResult = {
                route,
                url: fullUrl,
                finalUrl: fullUrl,
                authBlocked: false,
                viewports: []
            };

            for (const vp of VIEWPORTS) {
                pageErrors.length = 0;
                consoleErrors.length = 0;
                failedRequests.length = 0;
                requestStatuses.clear();

                await cdp.send("Emulation.setDeviceMetricsOverride", {
                    width: vp.width,
                    height: vp.height,
                    deviceScaleFactor: vp.deviceScaleFactor,
                    mobile: vp.mobile
                });

                console.log(`[Stage 2] Capturing ${route} @ ${vp.name} (${vp.width}x${vp.height})...`);
                await navigateAndWait(cdp, fullUrl);

                let finalPath = await cdp.evaluate("location.pathname + location.search");
                const isLoginTarget = route.toLowerCase().includes("/identity/account/login");
                if (!isLoginTarget && String(finalPath).toLowerCase().includes("/identity/account/login")) {
                    const loggedIn = await attemptAutoLogin(cdp, opts.baseUrl, opts.user, opts.pass);
                    if (loggedIn) {
                        await navigateAndWait(cdp, fullUrl);
                        finalPath = await cdp.evaluate("location.pathname + location.search");
                    } else {
                        routeResult.authBlocked = true;
                        results.warnings.push(
                            `AUTH_REQUIRED for ${route}: redirected to ${finalPath}. Set IBS_VERIFY_USER & IBS_VERIFY_PASS or run --interactive-login.`
                        );
                    }
                }

                routeResult.finalUrl = await cdp.evaluate("location.href");
                await cdp.evaluate("document.fonts ? document.fonts.ready.catch(() => {}) : Promise.resolve()");
                await new Promise((r) => setTimeout(r, 400));

                const domAudit = await cdp.evaluate(`(() => {
                    const docEl = document.documentElement;
                    const innerW = window.innerWidth;
                    const scrollW = docEl.scrollWidth;
                    const overflowing = [];
                    for (const el of document.querySelectorAll("body *")) {
                        const rect = el.getBoundingClientRect();
                        if (rect.width > 0 && rect.height > 0 && rect.right > innerW + 2) {
                            overflowing.push({
                                tag: el.tagName.toLowerCase(),
                                id: el.id || "",
                                cls: String(el.className || "").slice(0, 60),
                                right: Math.round(rect.right),
                                overflowPx: Math.round(rect.right - innerW)
                            });
                            if (overflowing.length >= 5) break;
                        }
                    }
                    const brokenImages = [];
                    for (const img of document.querySelectorAll("img")) {
                        if (!img.complete || img.naturalWidth === 0) {
                            brokenImages.push(img.getAttribute("src") || "(no src)");
                        }
                    }
                    const title = document.title || "";
                    const bodyText = (document.body?.innerText || "").slice(0, 1500);
                    const hasServerError = /An unhandled exception occurred|HTTP ERROR 500|Server Error in/i.test(bodyText);
                    return {
                        title,
                        innerWidth: innerW,
                        scrollWidth: scrollW,
                        horizontalOverflow: scrollW > innerW + 2,
                        overflowingElements: overflowing,
                        brokenImages,
                        hasServerError
                    };
                })()`);

                const shotFile = path.join(runDir, `${slug}-${vp.name}.png`);
                const shotData = await cdp.send("Page.captureScreenshot", { format: "png" });
                fs.writeFileSync(shotFile, Buffer.from(shotData.data, "base64"));
                results.screenshots.push(shotFile);

                const localFailedRequests = failedRequests.filter((r) =>
                    r.url.startsWith(opts.baseUrl) || r.status >= 500
                );

                const vpRecord = {
                    viewport: vp.name,
                    size: `${vp.width}x${vp.height}`,
                    screenshot: shotFile,
                    domAudit,
                    pageErrors: [...pageErrors],
                    consoleErrors: [...consoleErrors],
                    failedRequests: [...localFailedRequests]
                };
                routeResult.viewports.push(vpRecord);

                if (domAudit.hasServerError) {
                    results.errors.push(`${route} (${vp.name}): Rendered ASP.NET server error page.`);
                }
                if (pageErrors.length > 0) {
                    results.errors.push(`${route} (${vp.name}): ${pageErrors.length} uncaught JS exception(s): ${pageErrors[0]}`);
                }
                if (localFailedRequests.length > 0) {
                    results.errors.push(
                        `${route} (${vp.name}): ${localFailedRequests.length} failed request(s), e.g. ${localFailedRequests[0].status} ${localFailedRequests[0].url}`
                    );
                }
                if (domAudit.horizontalOverflow) {
                    results.errors.push(
                        `${route} (${vp.name}): Horizontal overflow detected (scrollWidth=${domAudit.scrollWidth} > innerWidth=${domAudit.innerWidth}).`
                    );
                }
                if (domAudit.brokenImages.length > 0) {
                    results.warnings.push(`${route} (${vp.name}): ${domAudit.brokenImages.length} broken image(s): ${domAudit.brokenImages.join(", ")}`);
                }

                // If MSAP route and authenticated, also capture dark mode desktop proof
                if (vp.name === "desktop" && !routeResult.authBlocked && /\/msap\b/i.test(route)) {
                    await cdp.evaluate("document.documentElement.setAttribute('data-bs-theme', 'dark')");
                    await new Promise((r) => setTimeout(r, 300));
                    const darkShot = path.join(runDir, `${slug}-desktop-dark.png`);
                    const darkData = await cdp.send("Page.captureScreenshot", { format: "png" });
                    fs.writeFileSync(darkShot, Buffer.from(darkData.data, "base64"));
                    results.screenshots.push(darkShot);
                    await cdp.evaluate("document.documentElement.setAttribute('data-bs-theme', 'light')");
                }
            }

            results.routes.push(routeResult);
        }
    } catch (err) {
        results.errors.push(`Stage 2 runtime error: ${err.message}`);
    } finally {
        cdp?.close();
        killProcessTree(browserProc);
        if (startedProc) {
            console.log("[Stage 2] Stopping auto-started IBSWeb server...");
            killProcessTree(startedProc);
        }
    }

    results.passed = results.errors.length === 0;
    return results;
}

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

function runOpencode(repoRoot, model, prompt, fileAttachments = [], timeoutMs = 120000) {
    const { cmd, prefixArgs } = resolveOpencodeBinary();
    const args = [...prefixArgs, "run", "-m", model];
    for (const filePath of fileAttachments) {
        args.push("-f", filePath);
    }
    args.push(prompt);

    return new Promise((resolve) => {
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
                output: stdout.trim() || stderr.trim()
            });
        });

        proc.on("error", (err) => {
            clearTimeout(timer);
            resolve({ ok: false, timedOut: false, code: 1, output: err.message });
        });
    });
}

function parseVerdict(text) {
    if (!text) return "UNKNOWN";
    const match = text.match(/\bVERDICT\s*:\s*(PASS|FAIL)\b/i);
    if (match) return match[1].toUpperCase();
    if (/\bFAIL\b/i.test(text) && !/\bPASS\b/i.test(text)) return "FAIL";
    return "PASS";
}

async function runStage3OpenCodeJudges(repoRoot, runDir, opts, scope, stage1, stage2, diffText) {
    const result = {
        passed: true,
        codeJudge: null,
        visionJudge: null
    };

    // 1. Code/Diff Judge (only if there are changed files / diff)
    if (scope.files.length > 0 && diffText.trim().length > 0) {
        const contextPath = path.join(runDir, "context.md");
        const contextBody = [
            "# Verification Context",
            `Scope mode: ${scope.scopeMode}`,
            `Changed files (${scope.files.length}):`,
            ...scope.files.map((f) => `- ${f}`),
            "",
            "## Stage 1 Static Results",
            `Passed: ${stage1.passed}`,
            `Notes: ${stage1.notes.join(" | ")}`,
            `Errors: ${stage1.errors.join(" | ") || "None"}`,
            `Warnings: ${stage1.warnings.join(" | ") || "None"}`,
            "",
            "## Diff",
            "```diff",
            diffText,
            "```"
        ].join("\n");
        fs.writeFileSync(contextPath, contextBody, "utf8");

        const modelChain = [opts.model, ...DEFAULT_TEXT_MODELS.filter((m) => m !== opts.model)];
        const prompt =
            "Do NOT run shell commands or read unrelated repo files; review ONLY from the attached Verification Context (ASP.NET Core MVC, Razor .cshtml, EF Core PostgreSQL, MSAP UI). " +
            "Check for real correctness bugs, accounting/data integrity defects, EF Core risks, missing auth/validation, or MSAP UI violations. " +
            "Ignore style nitpicks. Start your reply with VERDICT: PASS or VERDICT: FAIL, followed by concise bullet findings (max 6 bullets).";

        for (const model of modelChain) {
            console.log(`[Stage 3] Running OpenCode code judge (${model})...`);
            const runRes = await runOpencode(repoRoot, model, prompt, [contextPath], 120000);
            if (runRes.ok) {
                result.codeJudge = {
                    model,
                    verdict: parseVerdict(runRes.output),
                    output: runRes.output
                };
                break;
            }
            console.warn(`[Stage 3] Model ${model} failed or timed out; trying fallback...`);
        }

        if (!result.codeJudge) {
            result.codeJudge = {
                model: modelChain.join(", "),
                verdict: "SKIPPED",
                output: "All free text models failed or timed out."
            };
        } else if (result.codeJudge.verdict === "FAIL") {
            result.passed = false;
        }
    } else {
        console.log("[Stage 3] No file diff detected; skipping code diff judge.");
    }

    // 2. Vision/Layout Judge on Desktop + Mobile screenshots
    if (stage2 && stage2.screenshots.length > 0) {
        const batch = stage2.screenshots.slice(0, 4);
        const visionPrompt =
            "Inspect the attached Desktop (1440x900) and Mobile (390x844) browser screenshots of IBSWeb. " +
            "Verify visual layout quality: check for overlapping text, clipped buttons/inputs, broken CSS/missing styles, " +
            "horizontal scrolling/overflow on mobile, unreadable contrast, or server error pages. " +
            "Note: if a login page is shown because auth was required, evaluate the login page layout itself. " +
            "Start your response with `VERDICT: PASS` or `VERDICT: FAIL`, then list concrete observations for Desktop and Mobile.";

        console.log(`[Stage 3] Running OpenCode vision judge (${opts.visionModel}) on ${batch.length} screenshot(s)...`);
        const visRes = await runOpencode(repoRoot, opts.visionModel, visionPrompt, batch, 90000);
        if (visRes.ok) {
            result.visionJudge = {
                model: opts.visionModel,
                screenshots: batch,
                verdict: parseVerdict(visRes.output),
                output: visRes.output
            };
            if (result.visionJudge.verdict === "FAIL") {
                result.passed = false;
            }
        } else {
            result.visionJudge = {
                model: opts.visionModel,
                screenshots: batch,
                verdict: "SKIPPED",
                output: `Vision model did not complete (${visRes.timedOut ? "timeout" : `code ${visRes.code}`}): ${visRes.output}`
            };
        }
    }

    return result;
}

function writeReports(repoRoot, runDir, report) {
    const mdLines = [
        "# Proof-Based Verification Report",
        "",
        `- **Timestamp:** ${report.timestamp}`,
        `- **Overall Verdict:** ${report.overallPassed ? "PASS" : "FAIL"}`,
        `- **Scope Mode:** ${report.scope.scopeMode} (${report.scope.files.length} file(s))`,
        "",
        "## Stage 1: Deterministic Static & .cshtml Gate",
        `- **Status:** ${report.stage1.passed ? "PASS" : "FAIL"}`,
        ...report.stage1.notes.map((n) => `- Note: ${n}`),
        ...(report.stage1.errors.length ? report.stage1.errors.map((e) => `- **ERROR:** ${e}`) : ["- Errors: None"]),
        ...(report.stage1.warnings.length ? report.stage1.warnings.map((w) => `- Warning: ${w}`) : []),
        ""
    ];

    if (report.stage2) {
        mdLines.push("## Stage 2: Runtime & Desktop/Mobile Browser Proof");
        mdLines.push(`- **Status:** ${report.stage2.passed ? "PASS" : "FAIL"}`);
        mdLines.push(`- **Base URL:** ${report.stage2.baseUrl}`);
        for (const r of report.stage2.routes) {
            mdLines.push(`- **Route:** \`${r.route}\` -> \`${r.finalUrl}\`${r.authBlocked ? " (AUTH_REQUIRED)" : ""}`);
            for (const vp of r.viewports) {
                mdLines.push(
                    `  - **${vp.viewport} (${vp.size}):** overflow=${vp.domAudit.horizontalOverflow} (scroll=${vp.domAudit.scrollWidth}/inner=${vp.domAudit.innerWidth}), pageErrors=${vp.pageErrors.length}, consoleErrors=${vp.consoleErrors.length}, failedHttp=${vp.failedRequests.length}, screenshot=\`${vp.screenshot}\``
                );
            }
        }
        if (report.stage2.errors.length) {
            for (const e of report.stage2.errors) mdLines.push(`- **ERROR:** ${e}`);
        }
        if (report.stage2.warnings.length) {
            for (const w of report.stage2.warnings) mdLines.push(`- Warning: ${w}`);
        }
        mdLines.push("");
    }

    if (report.stage3) {
        mdLines.push("## Stage 3: OpenCode Free-Model Proof Judge");
        if (report.stage3.codeJudge) {
            mdLines.push(`### Code Judge (${report.stage3.codeJudge.model}) — ${report.stage3.codeJudge.verdict}`);
            mdLines.push(report.stage3.codeJudge.output);
            mdLines.push("");
        }
        if (report.stage3.visionJudge) {
            mdLines.push(`### Vision Judge (${report.stage3.visionJudge.model}) — ${report.stage3.visionJudge.verdict}`);
            mdLines.push(report.stage3.visionJudge.output);
            mdLines.push("");
        }
    }

    const mdContent = mdLines.join("\n") + "\n";
    const jsonContent = JSON.stringify(report, null, 2) + "\n";

    const runMd = path.join(runDir, "report.md");
    const runJson = path.join(runDir, "report.json");
    const latestMd = path.join(repoRoot, "artifacts", "proof-verify", "latest-report.md");
    const latestJson = path.join(repoRoot, "artifacts", "proof-verify", "latest-report.json");

    fs.writeFileSync(runMd, mdContent, "utf8");
    fs.writeFileSync(runJson, jsonContent, "utf8");
    fs.writeFileSync(latestMd, mdContent, "utf8");
    fs.writeFileSync(latestJson, jsonContent, "utf8");

    return { runMd, latestMd, latestJson };
}

function ensureNodeRuntime() {
    const major = Number.parseInt(process.versions.node.split(".")[0], 10);
    if (!Number.isFinite(major) || major < 21 || typeof WebSocket === "undefined") {
        throw new Error(`Node.js v21+ with global WebSocket is required for CDP browser proof (detected ${process.version}).`);
    }
}

function detectRelatedIssueId(repoRoot, opts, scope) {
    if (opts.linearIssue) {
        return opts.linearIssue.toUpperCase();
    }
    const branchRes = runGit(repoRoot, ["rev-parse", "--abbrev-ref", "HEAD"]);
    if (branchRes.ok) {
        const match = branchRes.stdout.match(/\b(IBS-\d+)\b/i);
        if (match) {
            return match[1].toUpperCase();
        }
    }
    if (scope.scopeMode === "last-commit") {
        const logRes = runGit(repoRoot, ["log", "-1", "--pretty=%B"]);
        if (logRes.ok) {
            const match = logRes.stdout.match(/\b(IBS-\d+)\b/i);
            if (match) {
                return match[1].toUpperCase();
            }
        }
    }
    return null;
}

function extractBulletFindings(text) {
    if (!text) {
        return [];
    }
    return text
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => /^[-*]\s+/.test(line))
        .map((line) => line.replace(/^[-*]\s+/, "").trim())
        .filter((line) => line.length > 0 && !/^(none|no defects|no issues|no overlapping)/i.test(line));
}

function buildLinearPayload(repoRoot, runDir, opts, scope, report) {
    const stage1Errors = report.stage1?.errors || [];
    const stage1Warnings = report.stage1?.warnings || [];
    const stage2Errors = report.stage2?.errors || [];
    const stage2Warnings = report.stage2?.warnings || [];
    const codeJudge = report.stage3?.codeJudge || null;
    const visionJudge = report.stage3?.visionJudge || null;

    const codeBullets = codeJudge && codeJudge.verdict !== "SKIPPED" ? extractBulletFindings(codeJudge.output) : [];
    const visionBullets = visionJudge && visionJudge.verdict === "FAIL" ? extractBulletFindings(visionJudge.output) : [];

    const hasBlockingDefects = !report.overallPassed
        || stage1Errors.length > 0
        || stage2Errors.length > 0
        || codeJudge?.verdict === "FAIL"
        || visionJudge?.verdict === "FAIL";

    const hasDetectedIssues = hasBlockingDefects
        || stage1Warnings.length > 0
        || stage2Warnings.length > 0
        || codeBullets.length > 0
        || visionBullets.length > 0;

    const labels = new Set(["Validation"]);
    if (hasBlockingDefects) {
        labels.add("Bug");
    } else if (hasDetectedIssues) {
        labels.add("Improvement");
    }

    const uiRelevant = scope.msapFiles.length > 0
        || scope.razorFiles.length > 0
        || scope.styleFiles.length > 0
        || stage2Errors.length > 0
        || visionJudge?.verdict === "FAIL"
        || stage1Errors.some((e) => /MSAP_AGENTS|ModernAlert|ModernTable|ModernSelect|overflow/i.test(e));
    if (uiRelevant && (hasBlockingDefects || scope.msapFiles.length > 0)) {
        labels.add("UIAlignment");
    }

    const relatedIssueId = detectRelatedIssueId(repoRoot, opts, scope);
    const failedStages = [];
    if (!report.stage1.passed) failedStages.push("Stage 1 Static");
    if (report.stage2 && !report.stage2.passed) failedStages.push("Stage 2 Runtime/Layout");
    if (codeJudge?.verdict === "FAIL") failedStages.push("Code Judge");
    if (visionJudge?.verdict === "FAIL") failedStages.push("Vision Judge");

    const routeSummary = report.stage2?.routes?.map((r) => r.route).slice(0, 2).join(", ");
    const fileSummary = scope.files.slice(0, 2).map((f) => path.basename(f)).join(", ");
    const targetSummary = routeSummary || fileSummary || "current changes";
    const statusPrefix = hasBlockingDefects
        ? `[proof-verify] Fix ${failedStages.join(" + ") || "verification"} failure on ${targetSummary}`
        : `[proof-verify] Verification findings for ${targetSummary}`;
    const suggestedTitle = statusPrefix.length > 110 ? `${statusPrefix.slice(0, 107)}...` : statusPrefix;

    const screenshots = (report.stage2?.screenshots || [])
        .filter((p) => fs.existsSync(p))
        .map((p) => ({
            filename: path.basename(p),
            path: p,
            size: fs.statSync(p).size,
            contentType: "image/png"
        }));

    const md = [
        `## Proof-Verify ${report.overallPassed ? "Summary" : "Detected Issues"} (${report.timestamp})`,
        "",
        `- **Overall Verdict:** ${report.overallPassed ? "PASS" : "FAIL"}`,
        `- **Scope:** \`${scope.scopeMode}\` (${scope.files.length} file(s))`,
        ...(relatedIssueId ? [`- **Related Issue:** ${relatedIssueId}`] : []),
        ""
    ];

    if (scope.files.length > 0) {
        md.push("### Changed Files");
        for (const f of scope.files.slice(0, 15)) {
            md.push(`- \`${f}\``);
        }
        if (scope.files.length > 15) {
            md.push(`- ...and ${scope.files.length - 15} more`);
        }
        md.push("");
    }

    md.push("### Stage 1: Static & .cshtml / MSAP Gate");
    md.push(`- **Status:** ${report.stage1.passed ? "PASS" : "FAIL"}`);
    for (const e of stage1Errors) md.push(`- **ERROR:** ${e}`);
    for (const w of stage1Warnings) md.push(`- **Warning:** ${w}`);
    if (stage1Errors.length === 0 && stage1Warnings.length === 0) {
        md.push("- No static errors or warnings.");
    }
    md.push("");

    if (report.stage2) {
        md.push("### Stage 2: Runtime & Desktop/Mobile Proof");
        md.push(`- **Status:** ${report.stage2.passed ? "PASS" : "FAIL"}`);
        for (const r of report.stage2.routes) {
            md.push(`- **Route:** \`${r.route}\` (${r.authBlocked ? "AUTH_REQUIRED" : r.finalUrl})`);
            for (const vp of r.viewports) {
                md.push(
                    `  - \`${vp.viewport} (${vp.size})\`: overflow=${vp.domAudit.horizontalOverflow} (${vp.domAudit.scrollWidth}/${vp.domAudit.innerWidth}), pageErrors=${vp.pageErrors.length}, consoleErrors=${vp.consoleErrors.length}, failedHttp=${vp.failedRequests.length}`
                );
            }
        }
        for (const e of stage2Errors) md.push(`- **ERROR:** ${e}`);
        for (const w of stage2Warnings) md.push(`- **Warning:** ${w}`);
        md.push("");
    }

    if (codeJudge || visionJudge) {
        md.push("### Stage 3: OpenCode Free-Model Judges");
        if (codeJudge) {
            md.push(`#### Code Judge (\`${codeJudge.model}\`) — ${codeJudge.verdict}`);
            md.push(codeJudge.output);
            md.push("");
        }
        if (visionJudge) {
            md.push(`#### Vision Judge (\`${visionJudge.model}\`) — ${visionJudge.verdict}`);
            md.push(visionJudge.output);
            md.push("");
        }
    }

    if (screenshots.length > 0) {
        md.push("### Visual Proofs Captured");
        for (const shot of screenshots) {
            md.push(`- \`${shot.filename}\` (${shot.size} bytes) — \`${shot.path}\``);
        }
        md.push("");
    }

    let resolvedAction = "none";
    if (opts.linearMode === "comment" || (opts.linearMode === "auto" && relatedIssueId)) {
        resolvedAction = relatedIssueId ? "comment" : (hasDetectedIssues ? "issue" : "none");
    } else if (opts.linearMode === "issue" || (opts.linearMode === "auto" && hasDetectedIssues)) {
        resolvedAction = "issue";
    }

    const payload = {
        hasDetectedIssues,
        hasBlockingDefects,
        overallPassed: report.overallPassed,
        team: opts.linearTeam || DEFAULT_LINEAR_TEAM,
        mode: opts.linearMode,
        recommendedAction: resolvedAction,
        relatedIssueId,
        suggestedTitle,
        labels: [...labels],
        priority: hasBlockingDefects ? 2 : 3,
        markdownBody: md.join("\n"),
        screenshots
    };

    const jsonText = JSON.stringify(payload, null, 2) + "\n";
    const runPayloadPath = path.join(runDir, "linear-payload.json");
    const latestPayloadPath = path.join(repoRoot, "artifacts", "proof-verify", "latest-linear-payload.json");
    fs.writeFileSync(runPayloadPath, jsonText, "utf8");
    fs.writeFileSync(latestPayloadPath, jsonText, "utf8");

    return { payload, latestPayloadPath };
}

async function linearGraphql(apiKey, query, variables) {
    const resp = await fetch("https://api.linear.app/graphql", {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Authorization": apiKey
        },
        body: JSON.stringify({ query, variables })
    });
    const body = await resp.json();
    if (!resp.ok || body.errors?.length) {
        const msg = body.errors?.map((e) => e.message).join("; ") || `HTTP ${resp.status}`;
        throw new Error(`Linear API error: ${msg}`);
    }
    return body.data;
}

async function postToLinearApiIfConfigured(opts, payload, payloadPath) {
    if (!opts.logLinear) {
        return;
    }
    if (payload.recommendedAction === "none") {
        console.log("[Stage 4] No detected issues and no --linear-issue target; skipping Linear logging.");
        return;
    }

    const apiKey = process.env.LINEAR_API_KEY || "";
    if (!apiKey) {
        console.log(`[Stage 4] LINEAR_API_KEY is not set; Linear payload written for MCP submission: ${payloadPath}`);
        console.log(`[Stage 4] Next: use linear_linear MCP (${payload.recommendedAction === "comment" ? `save_comment on ${payload.relatedIssueId}` : `save_issue on team ${payload.team}`}) + prepare_file_upload for screenshots.`);
        return;
    }

    if (payload.recommendedAction === "comment" && payload.relatedIssueId) {
        const issueData = await linearGraphql(
            apiKey,
            `query($id: String!) { issue(id: $id) { id identifier url } }`,
            { id: payload.relatedIssueId }
        );
        const issueUuid = issueData?.issue?.id;
        if (!issueUuid) {
            throw new Error(`Linear issue ${payload.relatedIssueId} not found.`);
        }
        const created = await linearGraphql(
            apiKey,
            `mutation($input: CommentCreateInput!) { commentCreate(input: $input) { success comment { id url } } }`,
            { input: { issueId: issueUuid, body: payload.markdownBody } }
        );
        console.log(`[Stage 4] Posted Linear comment to ${issueData.issue.identifier}: ${created?.commentCreate?.comment?.url || issueData.issue.url}`);
        return;
    }

    const teamData = await linearGraphql(
        apiKey,
        `query($name: String!) { teams(filter: { name: { eqIgnoreCase: $name } }) { nodes { id name key labels { nodes { id name } } } } }`,
        { name: payload.team }
    );
    const team = teamData?.teams?.nodes?.[0];
    if (!team) {
        throw new Error(`Linear team "${payload.team}" not found.`);
    }

    const labelIds = (team.labels?.nodes || [])
        .filter((l) => payload.labels.some((target) => target.toLowerCase() === l.name.toLowerCase()))
        .map((l) => l.id);

    const created = await linearGraphql(
        apiKey,
        `mutation($input: IssueCreateInput!) { issueCreate(input: $input) { success issue { id identifier url } } }`,
        {
            input: {
                teamId: team.id,
                title: payload.suggestedTitle,
                description: payload.markdownBody,
                priority: payload.priority,
                labelIds
            }
        }
    );
    const issue = created?.issueCreate?.issue;
    console.log(`[Stage 4] Created Linear issue ${issue?.identifier || ""}: ${issue?.url || "success"}`);
}

async function main() {
    ensureNodeRuntime();
    const opts = parseArgs(process.argv.slice(2));
    if (opts.help) {
        printUsage();
        process.exit(0);
    }

    const repoRoot = getRepoRoot();
    const runId = new Date().toISOString().replace(/[:.]/g, "-");
    const runDir = path.join(repoRoot, "artifacts", "proof-verify", runId);
    fs.mkdirSync(runDir, { recursive: true });

    console.log(`[proof-verify] Repo: ${repoRoot}`);
    const scope = getChangeScope(repoRoot);
    console.log(`[proof-verify] Scope: ${scope.scopeMode} (${scope.files.length} changed file(s))`);

    // Stage 1
    const stage1 = runStage1StaticChecks(repoRoot, scope, opts);
    console.log(`[Stage 1] ${stage1.passed ? "PASS" : "FAIL"} (${stage1.errors.length} error(s), ${stage1.warnings.length} warning(s))`);

    // Stage 2
    let stage2 = null;
    const routes = opts.urls.length > 0 ? opts.urls : inferRoutesFromChanges(scope);
    if (!opts.skipRuntime && (routes.length > 0 || opts.interactiveLogin)) {
        stage2 = await runStage2RuntimeProof(repoRoot, runDir, opts, routes, stage1.didBuild);
        console.log(`[Stage 2] ${stage2.passed ? "PASS" : "FAIL"} (${stage2.screenshots.length} screenshot(s))`);
    } else {
        console.log("[Stage 2] Skipped (no frontend/route targets or --skip-runtime).");
    }

    // Stage 3
    let stage3 = null;
    if (!opts.skipAi) {
        const diffText = getFilteredDiff(repoRoot, scope);
        stage3 = await runStage3OpenCodeJudges(repoRoot, runDir, opts, scope, stage1, stage2, diffText);
        console.log(`[Stage 3] ${stage3.passed ? "PASS" : "FAIL"}`);
    } else {
        console.log("[Stage 3] Skipped (--skip-ai).");
    }

    const overallPassed = stage1.passed && (stage2 ? stage2.passed : true) && (stage3 ? stage3.passed : true);
    const report = {
        timestamp: new Date().toISOString(),
        overallPassed,
        scope: {
            scopeMode: scope.scopeMode,
            files: scope.files
        },
        stage1,
        stage2,
        stage3
    };

    const { latestMd } = writeReports(repoRoot, runDir, report);
    const { payload: linearPayload, latestPayloadPath } = buildLinearPayload(repoRoot, runDir, opts, scope, report);
    await postToLinearApiIfConfigured(opts, linearPayload, latestPayloadPath);

    console.log(`\n[proof-verify] OVERALL: ${overallPassed ? "PASS" : "FAIL"}`);
    console.log(`[proof-verify] Report: ${latestMd}`);
    console.log(`[proof-verify] Linear payload: ${latestPayloadPath} (action=${linearPayload.recommendedAction}, issues=${linearPayload.hasDetectedIssues})`);
    if (stage2 && stage2.screenshots.length > 0) {
        console.log("[proof-verify] Screenshots:");
        for (const s of stage2.screenshots) {
            console.log(`  - ${s}`);
        }
    }

    process.exit(overallPassed ? 0 : 1);
}

main().catch((err) => {
    console.error(`[proof-verify] Fatal error: ${err.stack || err.message}`);
    process.exit(1);
});
