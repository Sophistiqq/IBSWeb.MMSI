#!/usr/bin/env node

import path from "node:path";

const EXEMPT_FILES = new Set([
    "modern-alert.js",
    "modern-table.js",
    "modern-select.js",
    "modern-navbar.js",
    "tutorial.js"
]);

function readStdin() {
    return new Promise((resolve) => {
        let data = "";
        process.stdin.setEncoding("utf8");
        process.stdin.on("data", (chunk) => {
            data += chunk;
        });
        process.stdin.on("end", () => resolve(data));
        process.stdin.on("error", () => resolve(""));
    });
}

function stripJsComments(source) {
    return source
        .replace(/\/\*[\s\S]*?\*\//g, "")
        .replace(/(^|[^:\\])\/\/[^\n]*/g, "$1");
}

function extractInlineScripts(html) {
    const blocks = [];
    const regex = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
    let match;
    while ((match = regex.exec(html)) !== null) {
        if (!/\bsrc\s*=/i.test(match[1])) {
            blocks.push(match[2]);
        }
    }
    return blocks;
}

function isMsapUiFile(filePath) {
    if (!filePath || typeof filePath !== "string") {
        return false;
    }
    const normalized = filePath.replace(/\\/g, "/").toLowerCase();
    const isMsapPath = normalized.includes("/ibsweb/areas/msap/")
        || normalized.includes("/ibsweb/wwwroot/msap/")
        || normalized.startsWith("ibsweb/areas/msap/")
        || normalized.startsWith("ibsweb/wwwroot/msap/");

    if (!isMsapPath) {
        return false;
    }
    if (!normalized.endsWith(".cshtml") && !normalized.endsWith(".js")) {
        return false;
    }
    const baseName = path.basename(normalized);
    return !EXEMPT_FILES.has(baseName);
}

function findViolations(filePath, content, toolName) {
    if (!content || typeof content !== "string") {
        return [];
    }

    const lower = filePath.replace(/\\/g, "/").toLowerCase();
    let scriptSource = content;

    if (lower.endsWith(".cshtml")) {
        const inlineBlocks = extractInlineScripts(content);
        if (inlineBlocks.length > 0) {
            scriptSource = inlineBlocks.join("\n");
        } else if (toolName === "write_file") {
            return [];
        }
    }

    const cleanJs = stripJsComments(scriptSource);
    const violations = [];

    if (/(?<![.\w$])(?:window\.)?(?:alert|confirm|prompt)\s*\(/.test(cleanJs)) {
        violations.push("native alert/confirm/prompt detected (use ModernAlert)");
    }
    if (/\bSwal\.fire\s*\(/.test(cleanJs)) {
        violations.push("direct Swal.fire(...) detected (use ModernAlert)");
    }
    if (/\btoastr\.(?:success|error|warning|info)\s*\(/.test(cleanJs)) {
        violations.push("raw toastr.* call detected (use ModernAlert)");
    }
    if (/\.DataTable\s*\(\s*\{/.test(cleanJs)) {
        violations.push("raw .DataTable({...}) detected (use ModernTable.config(...))");
    }
    if (/\$\([^)]+\)\.select2\s*\(/.test(cleanJs)) {
        violations.push("direct .select2(...) detected (use .js-modern-select / ModernSelect)");
    }

    return violations;
}

async function main() {
    const raw = await readStdin();
    if (!raw.trim()) {
        console.log(JSON.stringify({ decision: "allow" }));
        return;
    }

    let payload;
    try {
        payload = JSON.parse(raw);
    } catch {
        console.log(JSON.stringify({ decision: "allow" }));
        return;
    }

    const toolCall = payload.toolCall || {};
    const toolName = String(toolCall.name || "");
    const args = toolCall.args || {};
    const targetPath = args.Path || args.path || "";

    if (!isMsapUiFile(targetPath)) {
        console.log(JSON.stringify({ decision: "allow" }));
        return;
    }

    const incomingText = toolName === "write_file"
        ? (args.Content ?? args.content ?? "")
        : (args.Replacement ?? args.replacement ?? "");

    const violations = findViolations(targetPath, incomingText, toolName);
    if (violations.length > 0) {
        console.log(JSON.stringify({
            decision: "deny",
            reason: `MSAP_AGENTS.md guard blocked ${toolName} on ${path.basename(targetPath)}: ${violations.join("; ")}. Use MSAP house utilities per MSAP_AGENTS.md.`
        }));
        return;
    }

    console.log(JSON.stringify({ decision: "allow" }));
}

main().catch(() => {
    console.log(JSON.stringify({ decision: "allow" }));
});
