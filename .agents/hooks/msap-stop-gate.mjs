#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

const MSAP_CODE_PREFIXES = [
    "ibsweb/areas/msap/",
    "ibsweb/wwwroot/msap/",
    "ibs.models/msap/",
    "ibs.services/msap/",
    "ibs.dataaccess/msap/",
    "ibs.dtos/msap/",
    "ibs.utility/msap/"
];

const CODE_EXTENSIONS = new Set([
    ".cs", ".cshtml", ".js", ".mjs", ".cjs", ".css"
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

function isMsapCodeFile(relPath) {
    const normalized = relPath.replace(/\\/g, "/").toLowerCase();
    const ext = path.extname(normalized);
    if (!CODE_EXTENSIONS.has(ext)) {
        return false;
    }
    return MSAP_CODE_PREFIXES.some((prefix) => normalized.startsWith(prefix));
}

function getModifiedMsapFiles(cwd) {
    try {
        const output = execFileSync("git", ["status", "--porcelain"], {
            cwd,
            encoding: "utf8",
            stdio: ["ignore", "pipe", "ignore"]
        });
        const files = [];
        for (const line of output.split(/\r?\n/)) {
            if (!line || line.length < 4) {
                continue;
            }
            const status = line.slice(0, 2);
            if (status.includes("D")) {
                continue;
            }
            let relPath = line.slice(3).trim();
            if (relPath.includes(" -> ")) {
                relPath = relPath.split(" -> ").pop().trim();
            }
            relPath = relPath.replace(/^"|"$/g, "");
            if (isMsapCodeFile(relPath)) {
                files.push(relPath);
            }
        }
        return files;
    } catch {
        return [];
    }
}

function hasFreshPassingStage1(cwd, modifiedFiles) {
    const reportPath = path.join(cwd, "artifacts", "proof-verify", "latest-report.json");
    if (!fs.existsSync(reportPath)) {
        return false;
    }

    try {
        const reportStat = fs.statSync(reportPath);
        const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));
        if (!report?.stage1?.passed) {
            return false;
        }

        const reportTime = report.timestamp
            ? Date.parse(report.timestamp)
            : reportStat.mtimeMs;

        if (!Number.isFinite(reportTime)) {
            return false;
        }

        for (const relPath of modifiedFiles) {
            const absPath = path.join(cwd, relPath);
            if (!fs.existsSync(absPath)) {
                continue;
            }
            const fileMtime = fs.statSync(absPath).mtimeMs;
            if (fileMtime > reportTime + 1500) {
                return false;
            }
        }

        return true;
    } catch {
        return false;
    }
}

async function main() {
    const raw = await readStdin();
    if (!raw.trim()) {
        console.log(JSON.stringify({}));
        return;
    }

    let payload;
    try {
        payload = JSON.parse(raw);
    } catch {
        console.log(JSON.stringify({}));
        return;
    }

    if (payload.continuationActive === true) {
        console.log(JSON.stringify({}));
        return;
    }

    if (payload.terminationReason && payload.terminationReason !== "NO_TOOL_CALL") {
        console.log(JSON.stringify({}));
        return;
    }

    const cwd = payload.cwd || process.cwd();
    const modifiedMsapFiles = getModifiedMsapFiles(cwd);
    if (modifiedMsapFiles.length === 0) {
        console.log(JSON.stringify({}));
        return;
    }

    if (hasFreshPassingStage1(cwd, modifiedMsapFiles)) {
        console.log(JSON.stringify({}));
        return;
    }

    const preview = modifiedMsapFiles.slice(0, 5).join(", ");
    const suffix = modifiedMsapFiles.length > 5
        ? ` (+${modifiedMsapFiles.length - 5} more)`
        : "";

    console.log(JSON.stringify({
        decision: "continue",
        reason: `Unverified MSAP changes detected (${preview}${suffix}). Run \`node .agents/skills/proof-verify/scripts/proof-verify.mjs --skip-runtime --skip-ai\` (or full proof-verify) and fix any Stage 1 failures before finishing.`
    }));
}

main().catch(() => {
    console.log(JSON.stringify({}));
});
