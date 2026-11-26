#!/usr/bin/env node
/**
 * Reproducible MSAP 42-Scenario Test Data Seeder (0 LLM tokens)
 *
 * Works on BOTH:
 *  - A fresh / empty MSAP database (seeds rich master files first), AND
 *  - A database with imported real master files (reuses existing Customers,
 *    Vessels, Ports, Terminals, Tugboats, Services, Tug Masters, Bank Accounts,
 *    and only creates missing prerequisites such as Representatives, Principals,
 *    or Subcon tugboats if absent).
 *
 * Usage:
 *   node .agents/skills/proof-verify/scripts/seed-msap-scenarios.mjs
 *   node .agents/skills/proof-verify/scripts/seed-msap-scenarios.mjs --start-date 2026-04-01
 *   node .agents/skills/proof-verify/scripts/seed-msap-scenarios.mjs --only S01,S25,S41
 *   node .agents/skills/proof-verify/scripts/seed-msap-scenarios.mjs --skip S01,S02 --headed
 *   node .agents/skills/proof-verify/scripts/seed-msap-scenarios.mjs --skip-master-seed
 */

import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { createRequire } from "node:module";

const require = createRequire(new URL("../package.json", import.meta.url));
const { chromium } = require("playwright-core");

function parseArgs(argv) {
    const args = {
        baseUrl: "http://localhost:5073",
        startDate: "2026-06-01",
        only: null,
        skip: null,
        skipMasterSeed: false,
        headed: false,
        concurrency: 3
    };
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        if (a === "--base-url" && argv[i + 1]) args.baseUrl = argv[++i].replace(/\/$/, "");
        else if (a.startsWith("--base-url=")) args.baseUrl = a.slice(11).replace(/\/$/, "");
        else if (a === "--start-date" && argv[i + 1]) args.startDate = argv[++i];
        else if (a.startsWith("--start-date=")) args.startDate = a.slice(13);
        else if (a === "--only" && argv[i + 1]) args.only = new Set(argv[++i].split(",").map((s) => s.trim()).filter(Boolean));
        else if (a.startsWith("--only=")) args.only = new Set(a.slice(7).split(",").map((s) => s.trim()).filter(Boolean));
        else if (a === "--skip" && argv[i + 1]) args.skip = new Set(argv[++i].split(",").map((s) => s.trim()).filter(Boolean));
        else if (a.startsWith("--skip=")) args.skip = new Set(a.slice(7).split(",").map((s) => s.trim()).filter(Boolean));
        else if (a === "--concurrency" && argv[i + 1]) args.concurrency = Math.max(1, Math.min(4, Number(argv[++i]) || 1));
        else if (a.startsWith("--concurrency=")) args.concurrency = Math.max(1, Math.min(4, Number(a.slice(14)) || 1));
        else if (a === "--skip-master-seed") args.skipMasterSeed = true;
        else if (a === "--headed") args.headed = true;
    }
    return args;
}

const OPTS = parseArgs(process.argv.slice(2));
const BASE = OPTS.baseUrl;
const CREDS_PATH = path.resolve("artifacts/proof-verify/test-accounts.local.json");
if (!fs.existsSync(CREDS_PATH)) {
    throw new Error(`Missing ${CREDS_PATH}. Configure test accounts before running.`);
}
const CREDS = JSON.parse(fs.readFileSync(CREDS_PATH, "utf8"));
const CORE_PASS = CREDS.passwordAllCoreAccounts;

// Ephemeral valid PDF & PNG upload fixtures
const FIX_DIR = fs.mkdtempSync(path.join(os.tmpdir(), "msap-seed-fixtures-"));
const PDF_PATH = path.join(FIX_DIR, "auth-memo.pdf");
const PNG_PATH = path.join(FIX_DIR, "ticket-proof.png");
fs.writeFileSync(PDF_PATH, Buffer.from("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
fs.writeFileSync(
    PNG_PATH,
    Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==", "base64")
);

function getWeekStart(dateStr) {
    const d = new Date(`${dateStr}T00:00:00Z`);
    const day = d.getUTCDay();
    const diff = (day + 5) % 7; // Tuesday week start
    d.setUTCDate(d.getUTCDate() - diff);
    return d.toISOString().slice(0, 10);
}

function addDays(dateStr, days) {
    const d = new Date(`${dateStr}T00:00:00Z`);
    d.setUTCDate(d.getUTCDate() + days);
    return d.toISOString().slice(0, 10);
}

async function clickSwalConfirm(page, timeoutMs = 4000) {
    try {
        const btn = page.locator(".swal2-popup .swal2-confirm");
        await btn.waitFor({ state: "visible", timeout: timeoutMs });
        await btn.click();
        await page.waitForTimeout(550);
        return true;
    } catch {
        return false;
    }
}

async function postForm(page, url, fields) {
    return page.evaluate(async ({ url, fields }) => {
        const getRes = await fetch(url, { credentials: "same-origin" });
        const html = await getRes.text();
        const doc = new DOMParser().parseFromString(html, "text/html");
        const form = Array.from(doc.querySelectorAll("form")).find((f) => !(f.getAttribute("action") || "").includes("Logout"));
        if (!form) return { ok: false, error: `no form at ${url}` };
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
        const fd = new URLSearchParams();
        if (token) fd.append("__RequestVerificationToken", token);
        for (const [k, v] of Object.entries(fields)) {
            if (v === undefined || v === null) continue;
            fd.append(k, typeof v === "boolean" ? (v ? "true" : "false") : String(v));
        }
        const postUrl = form.getAttribute("action") || url;
        const r = await fetch(postUrl, {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: fd.toString(),
            redirect: "follow"
        });
        const text = await r.text();
        const redirected = r.url !== `${location.origin}${url}` && !r.url.includes("/Create");
        const errDoc = new DOMParser().parseFromString(text, "text/html");
        const errs = Array.from(errDoc.querySelectorAll(".field-validation-error, .validation-summary-errors, .alert-danger"))
            .map((e) => e.textContent.trim())
            .filter(Boolean);
        return { ok: r.ok && redirected, status: r.status, finalUrl: r.url, errs };
    }, { url, fields });
}

async function getSelectOptions(page, url, selectId) {
    return page.evaluate(async ({ url, selectId }) => {
        const html = await (await fetch(url, { credentials: "same-origin" })).text();
        const doc = new DOMParser().parseFromString(html, "text/html");
        const sel = doc.querySelector(`#${selectId}`) || doc.querySelector(`select[name="${selectId}"]`);
        if (!sel) return [];
        return Array.from(sel.options).filter((o) => o.value).map((o) => ({ id: o.value, name: o.textContent.trim() }));
    }, { url, selectId });
}

async function inspectCurrentLookups(page) {
    return page.evaluate(async () => {
        const html = await (await fetch("/MSAP/VesselSchedule/Create", { credentials: "same-origin" })).text();
        const doc = new DOMParser().parseFromString(html, "text/html");
        const opts = (sel) => Array.from(doc.querySelectorAll(`${sel} option`))
            .filter((o) => o.value)
            .map((o) => ({ id: o.value, name: o.textContent.trim() }));

        const customers = opts("#CustomerId");
        const vessels = opts("#VesselId");
        const ports = opts("#PortId");

        const locations = [];
        for (const p of ports) {
            const r = await fetch(`/MSAP/VesselSchedule/GetTerminalsByPort?portId=${encodeURIComponent(p.id)}`, { credentials: "same-origin" });
            const terms = r.ok ? await r.json() : [];
            for (const t of terms) {
                locations.push({
                    portId: String(p.id),
                    portName: p.name,
                    terminalId: String(t.value ?? t.terminalId ?? t.id),
                    terminalName: t.text ?? t.terminalName ?? t.name ?? "",
                    isIoc: Boolean(t.isIoc),
                    isSubic: /subic/i.test(p.name) || /subic/i.test(t.text ?? "")
                });
            }
        }

        const tugboats = [];
        const tugBlock = html.match(/name="SelectedTugboatIds"[\s\S]*?<\/select>/i)?.[0] || "";
        for (const m of tugBlock.matchAll(/<option[^>]*value="(\d+)"([^>]*)>([^<]+)<\/option>/gi)) {
            tugboats.push({ id: m[1], name: m[3].trim(), isSubcon: /subcon|charter|3rd/i.test(m[3]) || /data-company-owned="false"/i.test(m[2]) });
        }

        const services = [];
        const svcBlock = html.match(/js-service-select[\s\S]*?<\/select>/i)?.[0] || "";
        for (const m of svcBlock.matchAll(/<option[^>]*value="(\d+)"[^>]*>([^<]+)<\/option>/gi)) {
            services.push({ id: m[1], name: m[2].trim() });
        }

        const customerDiscounts = {};
        for (const c of customers.slice(0, 20)) {
            const r = await fetch(`/MSAP/VesselSchedule/GetCustomerDiscount?customerId=${encodeURIComponent(c.id)}`, { credentials: "same-origin" });
            if (r.ok) {
                const d = await r.json();
                customerDiscounts[c.id] = Number(d?.discount ?? d?.defaultDiscount ?? d?.customerDiscount ?? 0);
            }
        }

        return { customers, vessels, ports, locations, tugboats, services, customerDiscounts };
    });
}

async function ensureMasterFiles(adminPage) {
    let lookups = await inspectCurrentLookups(adminPage);
    if (OPTS.skipMasterSeed) {
        console.log("[master] --skip-master-seed enabled; using existing master files.");
        return lookups;
    }

    // 1. Ports (only seed if no ports exist) & Terminals (only seed if no terminals exist)
    if (lookups.ports.length === 0) {
        console.log("[master] Seeding Ports...");
        for (const p of [
            { PortNumber: "P01", PortName: "BATANGAS" },
            { PortNumber: "P02", PortName: "SUBIC" },
            { PortNumber: "P03", PortName: "MANILA" },
            { PortNumber: "P04", PortName: "CEBU" }
        ]) await postForm(adminPage, "/MSAP/Port/Create", p);
    }

    if (lookups.locations.length === 0) {
        console.log("[master] Seeding Terminals...");
        const portList = await getSelectOptions(adminPage, "/MSAP/Terminal/Create", "PortId");
        const byPort = Object.fromEntries(portList.map((p) => [p.name.toUpperCase(), p.id]));
        const fallbackPort = portList[0]?.id;
        for (const t of [
            { TerminalNumber: "001", TerminalName: "JG Summit Petrochem Jetty", PortId: byPort.BATANGAS || fallbackPort, IsIoc: true },
            { TerminalNumber: "002", TerminalName: "Batangas International Port", PortId: byPort.BATANGAS || fallbackPort, IsIoc: false },
            { TerminalNumber: "003", TerminalName: "Shell Tabangao Terminal", PortId: byPort.BATANGAS || fallbackPort, IsIoc: true },
            { TerminalNumber: "004", TerminalName: "Subic Bay Naval Supply Depot", PortId: byPort.SUBIC || fallbackPort, IsIoc: true },
            { TerminalNumber: "005", TerminalName: "Subic New Container Terminal", PortId: byPort.SUBIC || fallbackPort, IsIoc: false },
            { TerminalNumber: "006", TerminalName: "Manila South Harbor Pier 15", PortId: byPort.MANILA || fallbackPort, IsIoc: false },
            { TerminalNumber: "007", TerminalName: "Manila North Harbor", PortId: byPort.MANILA || fallbackPort, IsIoc: false },
            { TerminalNumber: "008", TerminalName: "Cebu International Port", PortId: byPort.CEBU || fallbackPort, IsIoc: false }
        ]) {
            const res = await postForm(adminPage, "/MSAP/Terminal/Create", t);
            if (!res.ok) {
                throw new Error(`Terminal/Create failed for ${t.TerminalName}: ${JSON.stringify(res)} (ensure IBSWeb was restarted with the Terminal.Port [ValidateNever] fix)`);
            }
        }
        lookups = await inspectCurrentLookups(adminPage);
    }

    // 2. Tugboats (seed if none, or add a Subcon tugboat if none exists)
    const hasSubcon = lookups.tugboats.some((t) => t.isSubcon);
    if (lookups.tugboats.length === 0 || !hasSubcon) {
        console.log("[master] Seeding Tugboat Owners & Tugboats...");
        let ownerList = await getSelectOptions(adminPage, "/MSAP/Tugboat/Create", "companyNameSelect");
        if (ownerList.length === 0) {
            await postForm(adminPage, "/MSAP/TugboatOwner/Create", {
                TugboatOwnerNumber: "O01",
                TugboatOwnerName: "Pacific Charter Marine Corp.",
                Address: "Batangas City, Batangas",
                Tin: "123-456-789-00000",
                VatType: "Vatable",
                TaxType: "Withholding Tax",
                WithholdingTaxPercent: "0.02"
            });
            ownerList = await getSelectOptions(adminPage, "/MSAP/Tugboat/Create", "companyNameSelect");
        }
        const firstPortId = lookups.ports[0]?.id;
        if (lookups.tugboats.length === 0) {
            for (const tb of [
                { TugboatNumber: "B01", TugboatName: "M/T MMSI Titan", PortId: firstPortId, IsCompanyOwned: true },
                { TugboatNumber: "B02", TugboatName: "M/T MMSI Vanguard", PortId: lookups.ports[1]?.id || firstPortId, IsCompanyOwned: true },
                { TugboatNumber: "B03", TugboatName: "M/T MMSI Pioneer", PortId: lookups.ports[2]?.id || firstPortId, IsCompanyOwned: true },
                { TugboatNumber: "B04", TugboatName: "M/T MMSI Sentinel", PortId: lookups.ports[3]?.id || firstPortId, IsCompanyOwned: true }
            ]) await postForm(adminPage, "/MSAP/Tugboat/Create", tb);
        }
        if (!hasSubcon && ownerList[0]?.id) {
            await postForm(adminPage, "/MSAP/Tugboat/Create", {
                TugboatNumber: "B99",
                TugboatName: "M/T Pacific Hawk (Subcon)",
                PortId: firstPortId,
                IsCompanyOwned: false,
                TugboatOwnerId: ownerList[0].id
            });
        }
    }

    // 3. Tug Masters (always ensure at least 3 exist)
    for (const m of [
        { TugMasterNumber: "TM001", TugMasterName: "Capt. Roberto Santos", IsActive: true },
        { TugMasterNumber: "TM002", TugMasterName: "Capt. Eduardo Reyes", IsActive: true },
        { TugMasterNumber: "TM003", TugMasterName: "Capt. Danilo Mendoza", IsActive: true }
    ]) await postForm(adminPage, "/MSAP/TugMaster/Create", m);

    // 4. Services (seed if empty)
    if (lookups.services.length === 0) {
        console.log("[master] Seeding Maritime Services...");
        for (const s of [
            { ServiceNumber: "S01", ServiceName: "Docking" },
            { ServiceNumber: "S02", ServiceName: "Undocking" },
            { ServiceNumber: "S03", ServiceName: "Shifting" },
            { ServiceNumber: "S04", ServiceName: "Escorting" },
            { ServiceNumber: "S05", ServiceName: "Watering" },
            { ServiceNumber: "S06", ServiceName: "Standby" }
        ]) await postForm(adminPage, "/MSAP/MaritimeService/Create", s);
    }

    // 5. Vessels (seed if empty)
    if (lookups.vessels.length === 0) {
        console.log("[master] Seeding Vessels...");
        for (const v of [
            { VesselNumber: "V001", VesselName: "M/V Pacific Horizon", VesselType: "Bulk Carrier" },
            { VesselNumber: "V002", VesselName: "M/T Petron Star", VesselType: "Oil Tanker" },
            { VesselNumber: "V003", VesselName: "M/V Archipelago Trader", VesselType: "General Cargo" },
            { VesselNumber: "V004", VesselName: "M/T Oriental Voyager", VesselType: "Chemical Tanker" },
            { VesselNumber: "V005", VesselName: "M/V Emerald Carrier", VesselType: "Container Ship" },
            { VesselNumber: "V006", VesselName: "M/T Luzon Express", VesselType: "LPG Carrier" },
            { VesselNumber: "V007", VesselName: "M/V Mindanao Pride", VesselType: "Bulk Carrier" },
            { VesselNumber: "V008", VesselName: "M/T Coral Sea", VesselType: "Product Tanker" },
            { VesselNumber: "V009", VesselName: "M/V Southern Cross", VesselType: "Container Ship" },
            { VesselNumber: "V010", VesselName: "M/T Manila Bay", VesselType: "Oil Tanker" }
        ]) await postForm(adminPage, "/MSAP/Vessel/Create", v);
    }

    // 6. Customers (seed if empty)
    if (lookups.customers.length === 0) {
        console.log("[master] Seeding Customers...");
        for (const c of [
            { CustomerName: "Apex Maritime Logistics Inc.", CustomerAddress: "Pier 4, Batangas City", ZipCode: "4200", CustomerTin: "101-202-303-00000", BusinessStyle: "Shipping Logistics", CustomerType: "Industrial", DefaultCosDiscountPercent: "0", CustomerTerms: "30D", VatType: "Vatable", WithHoldingVat: false, WithHoldingTax: true, PrintDiscount: false },
            { CustomerName: "Pacific Tanker Lines Corp.", CustomerAddress: "Tabangao, Batangas City", ZipCode: "4200", CustomerTin: "102-203-304-00000", BusinessStyle: "Tanker Operations", CustomerType: "Industrial", DefaultCosDiscountPercent: "5", CustomerTerms: "15D", VatType: "Vatable", WithHoldingVat: true, WithHoldingTax: true, PrintDiscount: true },
            { CustomerName: "Subic Bulk Shipping Corp.", CustomerAddress: "Subic Bay Freeport Zone, Zambales", ZipCode: "2222", CustomerTin: "103-204-305-00000", BusinessStyle: "Bulk Shipping", CustomerType: "Industrial", DefaultCosDiscountPercent: "10", CustomerTerms: "30D", VatType: "Vatable", WithHoldingVat: false, WithHoldingTax: true, PrintDiscount: true },
            { CustomerName: "Orient Container Carriers Inc.", CustomerAddress: "South Harbor, Port Area, Manila", ZipCode: "1018", CustomerTin: "104-205-306-00000", BusinessStyle: "International Shipping", CustomerType: "Industrial", DefaultCosDiscountPercent: "8", CustomerTerms: "20D", VatType: "Zero-Rated", WithHoldingVat: false, WithHoldingTax: false, PrintDiscount: false },
            { CustomerName: "Archipelago Petrochem Transport", CustomerAddress: "Simlong, Batangas City", ZipCode: "4200", CustomerTin: "105-206-307-00000", BusinessStyle: "Petrochemical Shipping", CustomerType: "Industrial", DefaultCosDiscountPercent: "0", CustomerTerms: "15D", VatType: "Vatable", WithHoldingVat: false, WithHoldingTax: true, PrintDiscount: false },
            { CustomerName: "Cebu Interisland Freight Corp.", CustomerAddress: "North Reclamation Area, Cebu City", ZipCode: "6000", CustomerTin: "106-207-308-00000", BusinessStyle: "Domestic Freight", CustomerType: "Retail", DefaultCosDiscountPercent: "3", CustomerTerms: "10D", VatType: "Exempt", WithHoldingVat: false, WithHoldingTax: false, PrintDiscount: true },
            { CustomerName: "Manila Global Shipping Agency", CustomerAddress: "Intramuros, Manila", ZipCode: "1002", CustomerTin: "107-208-309-00000", BusinessStyle: "Shipping Agency", CustomerType: "Reseller", DefaultCosDiscountPercent: "12", CustomerTerms: "30D", VatType: "Vatable", WithHoldingVat: true, WithHoldingTax: true, PrintDiscount: true },
            { CustomerName: "Southern Seas Marine Services", CustomerAddress: "Mabini, Batangas", ZipCode: "4202", CustomerTin: "108-209-310-00000", BusinessStyle: "Marine Support", CustomerType: "Industrial", DefaultCosDiscountPercent: "0", CustomerTerms: "15PDC", VatType: "Vatable", WithHoldingVat: false, WithHoldingTax: false, PrintDiscount: false }
        ]) await postForm(adminPage, "/MSAP/Customer/Create", c);
    }

    lookups = await inspectCurrentLookups(adminPage);

    // 7. Principals (ensure at least 1 principal exists for Principal-billed scenarios)
    const custForPrincipal = lookups.customers[4]?.id || lookups.customers[0]?.id;
    const custForReps = lookups.customers[6]?.id || lookups.customers[1]?.id || lookups.customers[0]?.id;
    if (custForPrincipal) {
        await postForm(adminPage, "/MSAP/Principal/Create", {
            PrincipalNumber: "PR01",
            PrincipalName: "JG Petrochem Global Holdings",
            CustomerId: custForPrincipal,
            Address: "BGC, Taguig City",
            TIN: "301-402-503-00000",
            BusinessType: "Petrochemical",
            Terms: "30D",
            IsVatable: true,
            IsActive: true
        });
    }
    if (custForReps && custForReps !== custForPrincipal) {
        await postForm(adminPage, "/MSAP/Principal/Create", {
            PrincipalNumber: "PR02",
            PrincipalName: "Pacific Rim Charterers Ltd.",
            CustomerId: custForReps,
            Address: "Makati CBD, Makati City",
            TIN: "302-403-504-00000",
            BusinessType: "International Charterer",
            Terms: "30D",
            IsVatable: true,
            IsActive: true
        });
    }

    // 8. Representatives (imported master files typically omit reps; ensure 2 exist)
    for (const r of [
        { RepresentativeName: "Carlos Mendoza", Category: "AgentCommission", RateMode: "Percentage", RateValue: "2", CalculationBase: "NetOfDiscount", IsActive: true, Address: "Makati City", Tin: "401-502-603-00000", VatType: "Vatable", TaxType: "Withholding Tax", WithholdingTaxPercent: "0.02" },
        { RepresentativeName: "Elena Cruz", Category: "AgencyIncentive", RateMode: "Flat", RateValue: "1500", CalculationBase: "Gross", IsActive: true, Address: "Batangas City", Tin: "402-503-604-00000", VatType: "Vatable", TaxType: "Exempt", WithholdingTaxPercent: "" }
    ]) await postForm(adminPage, "/MSAP/Representative/Create", r);

    // 9. Bank Accounts (Bank <= 10 chars; idempotent by unique AccountNo)
    for (const bk of [
        { BankAccountCode: "BDO1", Bank: "BDO", Branch: "Batangas", AccountNo: "0012345678", AccountName: "MMSI Operations Account", Company: "MMSI" },
        { BankAccountCode: "BPI1", Bank: "BPI", Branch: "Makati", AccountNo: "9876543210", AccountName: "MMSI Treasury Account", Company: "MMSI" }
    ]) await postForm(adminPage, "/MSAP/BankAccount/Create", bk);

    return inspectCurrentLookups(adminPage);
}

// Launch 1 browser, 5 persistent role contexts
const browser = await chromium.launch({ channel: "chrome", headless: !OPTS.headed });
const roles = {};

async function initRole(name, username, password) {
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await ctx.newPage();
    page.setDefaultTimeout(12000);
    page.setDefaultNavigationTimeout(15000);
    await page.goto(`${BASE}/Identity/Account/Login`, { waitUntil: "load" });
    await page.fill("#username", username);
    await page.fill("#password", password);
    await Promise.all([
        page.waitForNavigation({ waitUntil: "load" }).catch(() => {}),
        page.click("#login-submit")
    ]);
    roles[name] = { ctx, page };
}

try {
    await Promise.all([
        initRole("OPS", "OPERATIONS", CORE_PASS),
        initRole("REV", "rml1", CORE_PASS),
        initRole("ENC", "ENCODER", CORE_PASS),
        initRole("FIN", "FINANCE", CORE_PASS),
        initRole("ADM", CREDS.ADMIN.username, CREDS.ADMIN.password)
    ]);
    console.log("Logged in all 5 roles!");

    const lookups = await ensureMasterFiles(roles.ADM.page);
    console.log("Resolved lookups:", {
        customers: lookups.customers.length,
        vessels: lookups.vessels.length,
        locations: lookups.locations.length,
        tugboats: lookups.tugboats.length,
        services: lookups.services.length
    });
    if (!lookups.customers.length || !lookups.vessels.length || !lookups.locations.length || !lookups.tugboats.length || !lookups.services.length) {
        throw new Error("Master files are incomplete; cannot generate scenarios.");
    }

    // Dynamic pickers so script adapts to BOTH fresh DB and imported real master files
    const pickCust = (i) => lookups.customers[i % lookups.customers.length].id;
    const pickVes = (i) => lookups.vessels[i % lookups.vessels.length].id;
    const pickLoc = (i) => lookups.locations[i % lookups.locations.length];
    const subicLoc = lookups.locations.find((l) => l.isSubic) || lookups.locations[0];
    const companyTugs = lookups.tugboats.filter((t) => !t.isSubcon);
    const subconTugs = lookups.tugboats.filter((t) => t.isSubcon);
    const pickTug = (i) => (companyTugs.length ? companyTugs[i % companyTugs.length].id : lookups.tugboats[i % lookups.tugboats.length].id);
    const pickSubcon = () => (subconTugs[0] || lookups.tugboats[lookups.tugboats.length - 1]).id;
    const pickSvc = (name, fallbackIdx = 0) =>
        (lookups.services.find((s) => s.name.toLowerCase().includes(name.toLowerCase())) || lookups.services[fallbackIdx % lookups.services.length]).id;

    const discountedCust = Object.entries(lookups.customerDiscounts).find(([, pct]) => pct > 0)?.[0] || pickCust(1);
    const principalCust = lookups.customers[4]?.id || pickCust(0);
    const repCust = lookups.customers[6]?.id || pickCust(1);

    // ---- Transactional helpers (scoped per worker tab set, sharing the 5 single-session role contexts) ----
    function createWorkerHelpers(w) {
    async function createSchedule({ dateStr, startHour, durationHrs = 2, customerId, vesselId, portId, terminalId, tugServices, voyage, notes }) {
        const page = w.OPS;
        const pad = (n) => String(n).padStart(2, "0");
        const startDt = `${dateStr}T${pad(startHour)}:00`;
        const endDt = `${dateStr}T${pad(startHour + durationHrs)}:00`;
        const res = await page.evaluate(async ({ dateStr, startDt, endDt, customerId, vesselId, portId, terminalId, tugServices, voyage, notes }) => {
            const html = await (await fetch(`/MSAP/VesselSchedule/Create?date=${dateStr}`, { credentials: "same-origin" })).text();
            const doc = new DOMParser().parseFromString(html, "text/html");
            const form = doc.querySelector("#scheduleForm");
            if (!form) return { ok: false, error: "no #scheduleForm" };
            const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            const fd = new URLSearchParams();
            fd.append("__RequestVerificationToken", token);
            fd.append("CustomerId", String(customerId));
            fd.append("VesselId", String(vesselId));
            fd.append("PortId", String(portId));
            fd.append("TerminalId", String(terminalId));
            fd.append("VoyageNumber", voyage);
            fd.append("PlannedStart", startDt);
            fd.append("PlannedEnd", endDt);
            fd.append("Notes", notes || "");
            for (const t of [...new Set(tugServices.map((ts) => String(ts.tugboatId)))]) fd.append("SelectedTugboatIds", t);
            tugServices.forEach((ts, i) => {
                const k = `row_${i}`;
                fd.append("ServiceEntries.Index", k);
                fd.append(`ServiceEntries[${k}].VesselScheduleServiceEntryId`, "0");
                fd.append(`ServiceEntries[${k}].TugboatId`, String(ts.tugboatId));
                fd.append(`ServiceEntries[${k}].ServiceId`, String(ts.serviceId));
            });
            const r = await fetch("/MSAP/VesselSchedule/Create", {
                method: "POST",
                credentials: "same-origin",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: fd.toString(),
                redirect: "follow"
            });
            const m = r.url.match(/\/VesselSchedule\/Details\/(\d+)/i);
            if (m) return { ok: true, scheduleId: Number(m[1]) };
            const txt = await r.text();
            const errDoc = new DOMParser().parseFromString(txt, "text/html");
            const errs = Array.from(errDoc.querySelectorAll(".field-validation-error, .validation-summary-errors, .text-error"))
                .map((e) => e.textContent.trim())
                .filter(Boolean);
            return { ok: false, status: r.status, url: r.url, errs: errs.slice(0, 4) };
        }, { dateStr, startDt, endDt, customerId, vesselId, portId, terminalId, tugServices, voyage, notes });
        if (!res.ok) throw new Error(`createSchedule failed: ${JSON.stringify(res)}`);
        return res.scheduleId;
    }

    async function approveRates(scheduleId, dateStr, { basicRate = 18500, bafRate = 2500, forceUpdate = false } = {}) {
        const page = w.REV;
        const weekStart = getWeekStart(dateStr);
        await page.goto(`${BASE}/MSAP/RateUpload/Editor?mode=combined&asOfDate=${weekStart}&scheduleId=${scheduleId}`, { waitUntil: "load" });
        await page.waitForTimeout(350);
        await page.evaluate(({ basicRate, bafRate, forceUpdate }) => {
            document.querySelectorAll("#rateEditorRows tr").forEach((tr) => {
                tr.querySelectorAll('input[type="text"], input[type="number"]').forEach((inp) => {
                    if (inp.disabled || inp.readOnly) return;
                    const aria = (inp.getAttribute("aria-label") || "").toLowerCase();
                    if (!aria.includes("amount")) return;
                    const val = (inp.value || "").trim();
                    if (!forceUpdate && val !== "" && Number(val.replace(/,/g, "")) > 0) return;
                    inp.value = String(aria.includes("baf") ? bafRate : basicRate);
                    inp.dispatchEvent(new Event("input", { bubbles: true }));
                    inp.dispatchEvent(new Event("change", { bubbles: true }));
                });
            });
        }, { basicRate, bafRate, forceUpdate });
        const btn = page.locator("#rateApprove");
        if (await btn.isVisible().catch(() => false) && await btn.isEnabled().catch(() => false)) {
            await btn.click();
            await page.waitForTimeout(500);
            await clickSwalConfirm(page, 4000);
            await page.waitForLoadState("load").catch(() => {});
            await page.waitForTimeout(500);
        }
    }

    async function approveSchedule(scheduleId) {
        const page = w.REV;
        await page.goto(`${BASE}/MSAP/VesselSchedule/Details/${scheduleId}`, { waitUntil: "load" });
        await page.locator('#confirmSchedule button[type="submit"]').click();
        await page.waitForTimeout(500);
        await clickSwalConfirm(page, 4000);
        await page.waitForTimeout(1000);
        await clickSwalConfirm(page, 1500);
        await page.waitForLoadState("load").catch(() => {});
        let m = page.url().match(/\/COS\/Details\/(\d+)/i);
        if (!m) {
            const href = await page.locator('a[href*="/COS/Details/"]').first().getAttribute("href").catch(() => "");
            m = (href || "").match(/\/COS\/Details\/(\d+)/i);
        }
        if (!m) throw new Error(`approveSchedule(${scheduleId}) failed (url=${page.url()})`);
        return Number(m[1]);
    }

    async function cancelSchedule(scheduleId, reason) {
        const page = w.OPS;
        await page.goto(`${BASE}/MSAP/VesselSchedule/Details/${scheduleId}`, { waitUntil: "load" });
        await page.evaluate(async ({ scheduleId, reason }) => {
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            const fd = new URLSearchParams({ __RequestVerificationToken: token, id: String(scheduleId), reason });
            await fetch(`/MSAP/VesselSchedule/Cancel/${scheduleId}`, {
                method: "POST", credentials: "same-origin",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: fd.toString(), redirect: "follow"
            });
        }, { scheduleId, reason });
    }

    async function reviseSchedule(scheduleId, newVoyage, newNotes) {
        const page = w.OPS;
        await page.goto(`${BASE}/MSAP/VesselSchedule/Edit/${scheduleId}`, { waitUntil: "load" });
        await page.fill("#VoyageNumber", newVoyage);
        await page.fill("#Notes", newNotes);
        await page.click('button[type="submit"]:has-text("Save")');
        await page.waitForTimeout(700);
        await clickSwalConfirm(page, 2500);
        await page.waitForTimeout(700);
        await clickSwalConfirm(page, 1500);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function urgentEntry(scheduleId) {
        const page = w.OPS;
        await page.goto(`${BASE}/MSAP/VesselSchedule/Details/${scheduleId}`, { waitUntil: "load" });
        await Promise.all([
            page.waitForNavigation({ waitUntil: "load" }).catch(() => {}),
            page.locator('button:has-text("Record urgent service")').click()
        ]);
        const m = page.url().match(/\/COS\/Details\/(\d+)/i);
        return m ? Number(m[1]) : null;
    }

    async function adoptTableRates(scheduleId) {
        const page = w.REV;
        await page.goto(`${BASE}/MSAP/VesselSchedule/Details/${scheduleId}`, { waitUntil: "load" });
        const btns = page.locator('button[data-adopt-kind], button:has-text("Adopt table rate"), button:has-text("Use table rate")');
        const count = await btns.count();
        for (let i = 0; i < count; i++) {
            const b = btns.first();
            if (await b.isVisible().catch(() => false)) {
                await b.click();
                await page.waitForTimeout(450);
                await clickSwalConfirm(page, 2000);
                await page.waitForTimeout(500);
            }
        }
        return count;
    }

    async function recordTickets(cosId, dateStr, { maxTickets = 4, startHour = 8, ticketPrefix = "DT" } = {}) {
        const page = w.OPS;
        let recorded = 0;
        for (let i = 0; i < maxTickets; i++) {
            await page.goto(`${BASE}/MSAP/COS/Details/${cosId}`, { waitUntil: "load" });
            const pendingBefore = await page.locator("tr.ui-attention .dropdown-trigger").count();
            if (pendingBefore === 0) break;
            await page.locator("tr.ui-attention .dropdown-trigger").first().click();
            await page.waitForTimeout(250);
            await Promise.all([
                page.waitForNavigation({ waitUntil: "load" }).catch(() => {}),
                page.locator('tr.ui-attention a[href*="expectedServiceEntryId"]').first().click()
            ]);
            await page.waitForTimeout(350);
            const skipBtn = page.locator("button[data-skip-photo]");
            if (await skipBtn.isVisible().catch(() => false)) {
                await skipBtn.click();
                await page.waitForTimeout(250);
            }
            const fileInput = page.locator("#cosTicketImage");
            if (await fileInput.count() > 0) await fileInput.setInputFiles(PNG_PATH).catch(() => {});

            const pad = (n) => String(n).padStart(2, "0");
            const h1 = startHour + i * 2;
            const dtNum = `${ticketPrefix}-${cosId}-${i + 1}`;
            await page.evaluate(({ dtNum, dateStr, t1, t2, masterIdx }) => {
                const form = document.querySelector("#actualTicketEntry form");
                if (!form) return;
                const setVal = (name, val) => {
                    const el = form.querySelector(`[name="${name}"]`);
                    if (!el || el.disabled) return;
                    el.value = String(val);
                    el.dispatchEvent(new Event("input", { bubbles: true }));
                    el.dispatchEvent(new Event("change", { bubbles: true }));
                };
                setVal("DispatchNumber", dtNum);
                setVal("Date", dateStr);
                setVal("DateLeft", dateStr);
                setVal("TimeLeft", t1);
                setVal("DateArrived", dateStr);
                setVal("TimeArrived", t2);
                setVal("Remarks", `Recorded actual service ticket ${dtNum}.`);
                const tm = form.querySelector('select[name="TugMasterId"]');
                if (tm) {
                    const opts = Array.from(tm.options).filter((o) => o.value);
                    if (opts.length) {
                        tm.value = opts[masterIdx % opts.length].value;
                        tm.dispatchEvent(new Event("change", { bubbles: true }));
                    }
                }
                const tons = form.querySelector('input[name="Tons"]');
                if (tons && tons.offsetParent !== null) setVal("Tons", 15 + (masterIdx % 5) * 5);
                const rate = form.querySelector('input[name="DispatchRate"]');
                if (rate && rate.offsetParent !== null && (!rate.value || Number(rate.value) === 0)) setVal("DispatchRate", 550);
            }, { dtNum, dateStr, t1: `${pad(h1)}:00`, t2: `${pad(h1 + 2)}:00`, masterIdx: i + cosId });

            await page.click("button.cos-ticket-save-btn");
            await page.waitForTimeout(1000);
            await clickSwalConfirm(page, 1500);
            await page.waitForLoadState("load").catch(() => {});
            const pendingAfter = await page.locator("tr.ui-attention .dropdown-trigger").count();
            if (pendingAfter >= pendingBefore) {
                throw new Error(`recordTickets(cos=${cosId}, i=${i}) did not save ticket.`);
            }
            recorded++;
        }
        return recorded;
    }

    async function cancelCosService(cosId, reason) {
        const page = w.OPS;
        await page.goto(`${BASE}/MSAP/COS/Details/${cosId}`, { waitUntil: "load" });
        await page.evaluate(async ({ cosId, reason }) => {
            const link = document.querySelector('tr.ui-attention a[href*="expectedServiceEntryId"]');
            const entryId = link?.getAttribute("href")?.match(/expectedServiceEntryId=(\d+)/)?.[1];
            if (!entryId) return;
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            const fd = new URLSearchParams({ __RequestVerificationToken: token, id: String(cosId), entryId, reason });
            await fetch(`/MSAP/CustomerOrderSlip/CancelService/${cosId}`, {
                method: "POST", credentials: "same-origin",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: fd.toString(), redirect: "follow"
            });
        }, { cosId, reason });
    }

    async function cancelCos(cosId, reason) {
        const page = w.REV;
        await page.goto(`${BASE}/MSAP/COS/Details/${cosId}`, { waitUntil: "load" });
        await page.evaluate(async ({ cosId, reason }) => {
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
            const fd = new URLSearchParams({ __RequestVerificationToken: token, id: String(cosId), reason });
            await fetch(`/MSAP/CustomerOrderSlip/Cancel/${cosId}`, {
                method: "POST", credentials: "same-origin",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: fd.toString(), redirect: "follow"
            });
        }, { cosId, reason });
    }

    async function createBilling(cosId, { dateStr, isUndocumented = false, separateBaf = false, sbmaEnabled = false, billToPrincipal = false, useReps = false, remarks = "" } = {}) {
        const page = w.ENC;
        await page.goto(`${BASE}/MSAP/Billing/Create?cosId=${cosId}`, { waitUntil: "load" });
        await page.waitForSelector("#saveBilling:not([disabled])", { timeout: 12000 });
        await page.waitForTimeout(450);

        await page.evaluate(({ dateStr, isUndocumented, separateBaf, sbmaEnabled, billToPrincipal, useReps, remarks }) => {
            const setCheck = (sel, val) => {
                const el = document.querySelector(sel);
                if (!el || el.disabled || el.checked === val) return;
                el.checked = val;
                el.dispatchEvent(new Event("change", { bubbles: true }));
            };
            if (dateStr) {
                const d = document.querySelector("#Date");
                if (d) { d.value = dateStr; d.dispatchEvent(new Event("change", { bubbles: true })); }
            }
            if (isUndocumented) setCheck("#IsUndocumented", true);
            if (separateBaf) setCheck("#SeparateBaf", true);
            if (sbmaEnabled) setCheck("#SbmaEnabled", true);
            if (billToPrincipal) {
                const bt = document.querySelector("#BilledTo");
                const pOpt = bt && Array.from(bt.options).find((o) => /principal/i.test(o.value) || /principal/i.test(o.textContent));
                if (pOpt) { bt.value = pOpt.value; bt.dispatchEvent(new Event("change", { bubbles: true })); }
            }
            if (useReps) {
                const ag = document.querySelector("#AgencyIncentiveSelect");
                if (ag && ag.options.length > 1) { ag.value = ag.options[1].value; ag.dispatchEvent(new Event("change", { bubbles: true })); }
                const ac = document.querySelector("#AgentCommissionSelect");
                if (ac && ac.options.length > 1) { ac.value = ac.options[1].value; ac.dispatchEvent(new Event("change", { bubbles: true })); }
            }
            const rem = document.querySelector("#Remarks");
            if (rem && remarks) { rem.value = remarks; rem.dispatchEvent(new Event("input", { bubbles: true })); }
        }, { dateStr, isUndocumented, separateBaf, sbmaEnabled, billToPrincipal, useReps, remarks });

        await page.waitForTimeout(500);
        if (billToPrincipal) {
            await page.evaluate(() => {
                const ps = document.querySelector("#PrincipalSelect");
                const opt = ps && !ps.disabled && Array.from(ps.options).find((o) => o.value);
                if (opt) { ps.value = opt.value; ps.dispatchEvent(new Event("change", { bubbles: true })); }
            });
            await page.waitForTimeout(350);
        }

        await page.click("#saveBilling");
        await page.waitForTimeout(550);
        await clickSwalConfirm(page, 4000);
        await page.waitForTimeout(1400);
        await clickSwalConfirm(page, 6000);
        await page.waitForLoadState("load").catch(() => {});
        await page.waitForTimeout(450);

        const m = page.url().match(/\/Billing\/Review\/(\d+)/i);
        if (!m) throw new Error(`createBilling(cos=${cosId}) failed, url=${page.url()}`);
        const billingId = Number(m[1]);
        let bafBillingId = null;
        if (separateBaf) {
            await page.goto(`${BASE}/MSAP/COS/Details/${cosId}`, { waitUntil: "load" });
            const links = await page.evaluate(() => Array.from(document.querySelectorAll('a[href*="/Billing/Review/"]')).map((a) => a.getAttribute("href")));
            const ids = [...new Set(links.map((h) => Number(h.match(/\/Billing\/Review\/(\d+)/)?.[1])).filter(Boolean))];
            bafBillingId = ids.find((id) => id !== billingId) || null;
        }
        return { billingId, bafBillingId };
    }

    async function endorseBilling(billingId) {
        const page = w.REV;
        await page.goto(`${BASE}/MSAP/Billing/Review/${billingId}`, { waitUntil: "load" });
        await page.click('#endorseReviewedBilling button[type="submit"]');
        await page.waitForTimeout(450);
        await clickSwalConfirm(page, 4000);
        await page.waitForTimeout(1300);
        await clickSwalConfirm(page, 2000);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function approveBilling(billingId) {
        const page = w.FIN;
        await page.goto(`${BASE}/MSAP/Billing/Review/${billingId}`, { waitUntil: "load" });
        await page.click('#postReviewedBilling button[type="submit"]');
        await page.waitForTimeout(450);
        await clickSwalConfirm(page, 4000);
        await page.waitForTimeout(1300);
        await clickSwalConfirm(page, 2000);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function rejectBilling(billingId, reason) {
        const page = w.REV;
        await page.goto(`${BASE}/MSAP/Billing/Review/${billingId}`, { waitUntil: "load" });
        await page.click("#btnRejectBilling");
        await page.waitForSelector(".swal2-popup", { timeout: 4000 });
        await page.fill(".swal2-popup input, .swal2-popup textarea", reason);
        await clickSwalConfirm(page, 3000);
        await page.waitForTimeout(1300);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function reopenBilling(billingId, reason) {
        const page = w.FIN;
        await page.goto(`${BASE}/MSAP/Billing/Review/${billingId}`, { waitUntil: "load" });
        await page.click("#btnReopenBilling");
        await page.waitForSelector(".swal2-popup", { timeout: 4000 });
        const fileInp = page.locator("#swalAdjustmentFile");
        if (await fileInp.count() > 0) await fileInp.setInputFiles(PDF_PATH).catch(() => {});
        await page.fill("#swalAdjustmentReason, .swal2-popup textarea", reason);
        await clickSwalConfirm(page, 3000);
        await page.waitForTimeout(1300);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function adjustBilling(billingId, { basicRate, bafRate, discountMode, discountPercent, flatTariff, flatBaf, scope, includeSubcon, reason } = {}) {
        const page = w.ENC;
        await page.goto(`${BASE}/MSAP/Billing/Adjust/${billingId}`, { waitUntil: "load" });
        await page.waitForTimeout(350);
        await page.evaluate(({ basicRate, bafRate, discountMode, discountPercent, flatTariff, flatBaf, scope, includeSubcon, reason }) => {
            if (basicRate !== undefined) {
                const inp = document.querySelector('#adjustTicketsTable tbody tr:first-child input[name*="DispatchRate"]');
                if (inp) { inp.value = String(basicRate); inp.dispatchEvent(new Event("input", { bubbles: true })); }
            }
            if (bafRate !== undefined) {
                const inp = document.querySelector('#adjustTicketsTable tbody tr:first-child input[name*="BAFRate"]');
                if (inp) { inp.value = String(bafRate); inp.dispatchEvent(new Event("input", { bubbles: true })); }
            }
            if (discountMode) {
                const apply = document.querySelector("#adjustApplyDiscount");
                if (apply && !apply.checked) { apply.checked = true; apply.dispatchEvent(new Event("change", { bubbles: true })); }
                document.querySelector(`#discountModeGroup .billing-segmented-btn[data-mode="${discountMode}"]`)?.click();
            }
            if (scope) document.querySelector(`#discountScopeGroup .billing-segmented-btn[data-scope="${scope}"]`)?.click();
            if (discountPercent !== undefined) {
                const pct = document.querySelector("#adjustDiscountPercent");
                if (pct) { pct.value = String(discountPercent); pct.dispatchEvent(new Event("input", { bubbles: true })); }
            }
            if (flatTariff !== undefined) {
                const ft = document.querySelector("#adjustFlatTariff");
                if (ft) { ft.value = String(flatTariff); ft.dispatchEvent(new Event("input", { bubbles: true })); }
            }
            if (flatBaf !== undefined) {
                const fb = document.querySelector("#adjustFlatBaf");
                if (fb) { fb.value = String(flatBaf); fb.dispatchEvent(new Event("input", { bubbles: true })); }
            }
            if (includeSubcon) {
                const sub = document.querySelector("#adjustIncludeSubcon");
                if (sub && sub.type === "checkbox" && !sub.checked) { sub.checked = true; sub.dispatchEvent(new Event("change", { bubbles: true })); }
            }
            const rEl = document.querySelector('#adjustReason, textarea[name="Reason"], textarea[name="AdjustmentReason"]');
            if (rEl && reason) { rEl.value = reason; rEl.dispatchEvent(new Event("input", { bubbles: true })); }
        }, { basicRate, bafRate, discountMode, discountPercent, flatTariff, flatBaf, scope, includeSubcon, reason });

        await page.setInputFiles("#adjustAuthFile", PDF_PATH);
        await page.waitForTimeout(350);
        await page.click('button:has-text("Save adjustment")');
        await page.waitForTimeout(550);
        await clickSwalConfirm(page, 4000);
        await page.waitForTimeout(1300);
        await clickSwalConfirm(page, 2500);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function createCollection(cosId, { dateStr, receiptNo, refNo, checkNo, isCash = false, isUndocumented = false, selectAll = false, remarks = "" } = {}) {
        const page = w.ENC;
        await page.goto(`${BASE}/MSAP/Collection/Create?customerOrderSlipId=${cosId}`, { waitUntil: "load" });
        await page.waitForSelector('#collectionBillings input[type="checkbox"]:not([disabled])', { timeout: 10000 });
        if (selectAll) {
            const boxes = page.locator('#collectionBillings input[type="checkbox"]:not(:checked):not([disabled])');
            const c = await boxes.count();
            for (let i = 0; i < c; i++) { await boxes.first().click(); await page.waitForTimeout(200); }
        } else {
            const first = page.locator('#collectionBillings input[type="checkbox"]:not(:checked):not([disabled])').first();
            if (await first.isVisible().catch(() => false)) { await first.click(); await page.waitForTimeout(350); }
        }
        if (isCash) {
            const cashBtn = page.locator('button[data-payment-mode="cash"], button:has-text("Cash")').first();
            if (await cashBtn.isVisible().catch(() => false)) { await cashBtn.click(); await page.waitForTimeout(300); }
        }
        await page.evaluate(({ dateStr, receiptNo, refNo, checkNo, isCash, isUndocumented, remarks }) => {
            const set = (sel, v) => {
                const el = document.querySelector(sel);
                if (!el || el.disabled) return;
                el.value = String(v);
                el.dispatchEvent(new Event("input", { bubbles: true }));
                el.dispatchEvent(new Event("change", { bubbles: true }));
            };
            const undoc = document.querySelector("#IsUndocumented");
            if (undoc && undoc.checked !== isUndocumented) { undoc.checked = isUndocumented; undoc.dispatchEvent(new Event("change", { bubbles: true })); }
            if (receiptNo) set("#MsapCollectionNumber", receiptNo);
            if (dateStr) set("#Date", dateStr);
            if (refNo) set("#ReferenceNo", refNo);
            if (!isCash) {
                if (checkNo) set("#CheckNumber", checkNo);
                if (dateStr) set("#CheckDate", dateStr);
            }
            if (remarks) set("#Remarks", remarks);
        }, { dateStr, receiptNo, refNo, checkNo, isCash, isUndocumented, remarks });

        await page.waitForSelector("#saveCollection:not([disabled])", { timeout: 8000 });
        await page.click("#saveCollection");
        await page.waitForTimeout(550);
        await clickSwalConfirm(page, 4000);
        await page.waitForTimeout(1300);
        await clickSwalConfirm(page, 2000);
        await page.waitForLoadState("load").catch(() => {});
        const m = page.url().match(/\/Collection\/Preview\/(\d+)/i);
        if (!m) throw new Error(`createCollection(cos=${cosId}) failed, url=${page.url()}`);
        return Number(m[1]);
    }

    async function editCollection(collectionId, newRefNo, newRemarks) {
        const page = w.ENC;
        await page.goto(`${BASE}/MSAP/Collection/Edit/${collectionId}`, { waitUntil: "load" });
        await page.waitForTimeout(350);
        await page.fill("#ReferenceNo", newRefNo);
        await page.fill("#Remarks", newRemarks);
        await page.click("#saveCollection");
        await page.waitForTimeout(550);
        await clickSwalConfirm(page, 4000);
        await page.waitForTimeout(1200);
        await clickSwalConfirm(page, 2000);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function collectionBanking(collectionId, actionLabel, { bankIndex = 0, reason = "DAIF - Drawn Against Insufficient Funds" } = {}) {
        const page = w.ENC;
        await page.goto(`${BASE}/MSAP/Collection/Preview/${collectionId}`, { waitUntil: "load" });
        await page.locator(`a[data-collection-banking="${actionLabel}"]`).click();
        await page.waitForSelector("#collectionBankingForm", { timeout: 8000 });
        await page.evaluate(({ bankIndex, reason }) => {
            const bankSel = document.querySelector('#collectionBankingForm select[name="bankId"]');
            if (bankSel) {
                const opts = Array.from(bankSel.options).filter((o) => o.value);
                if (opts.length) { bankSel.value = opts[bankIndex % opts.length].value; bankSel.dispatchEvent(new Event("change", { bubbles: true })); }
            }
            const rInp = document.querySelector('#collectionBankingForm textarea[name="reason"], #collectionBankingForm input[name="reason"]');
            if (rInp && !rInp.value) { rInp.value = reason; rInp.dispatchEvent(new Event("input", { bubbles: true })); }
        }, { bankIndex, reason });
        await page.waitForTimeout(250);
        await clickSwalConfirm(page, 3000);
        await page.waitForTimeout(1300);
        await clickSwalConfirm(page, 2000);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function revertCollectionBanking(collectionId, reason) {
        const page = w.ADM;
        await page.goto(`${BASE}/MSAP/Collection/Preview/${collectionId}`, { waitUntil: "load" });
        await page.click("#btnRevertBanking");
        await page.waitForSelector(".swal2-popup", { timeout: 4000 });
        await page.fill(".swal2-popup input, .swal2-popup textarea", reason);
        await clickSwalConfirm(page, 3000);
        await page.waitForTimeout(1300);
        await clickSwalConfirm(page, 2000);
        await page.waitForLoadState("load").catch(() => {});
    }

    async function voidCollection(collectionId, reason) {
        const page = w.ADM;
        await page.goto(`${BASE}/MSAP/Collection/Preview/${collectionId}`, { waitUntil: "load" });
        await page.click("#btnVoidCollection");
        await page.waitForSelector(".swal2-popup", { timeout: 4000 });
        await page.fill(".swal2-popup input, .swal2-popup textarea", reason);
        await clickSwalConfirm(page, 3000);
        await page.waitForTimeout(1300);
        await clickSwalConfirm(page, 2000);
        await page.waitForLoadState("load").catch(() => {});
    }

    return {
        createSchedule, approveRates, approveSchedule, cancelSchedule, reviseSchedule,
        urgentEntry, adoptTableRates, recordTickets, cancelCosService, cancelCos,
        createBilling, endorseBilling, approveBilling, rejectBilling, reopenBilling,
        adjustBilling, createCollection, editCollection, collectionBanking,
        revertCollectionBanking, voidCollection
    };
    }

    const SVC = {
        Docking: pickSvc("Docking", 0),
        Undocking: pickSvc("Undocking", 1),
        Shifting: pickSvc("Shifting", 2),
        Escorting: pickSvc("Escorting", 3),
        Watering: pickSvc("Watering", 4),
        Standby: pickSvc("Standby", 5)
    };

    const SCENARIOS = [
        { code: "S01", desc: "Pending Review schedule (Docking+Undocking)", cust: pickCust(0), ves: pickVes(0), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickTug(0), serviceId: SVC.Undocking }], flow: "schedule-draft" },
        { code: "S02", desc: "Pending Review schedule (Company+Subcon 2 tugs)", cust: pickCust(3), ves: pickVes(4), loc: pickLoc(5), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Docking }, { tugboatId: pickSubcon(), serviceId: SVC.Escorting }], flow: "schedule-draft" },
        { code: "S03", desc: "Urgent Entry schedule (urgent COS before approval)", cust: pickCust(1), ves: pickVes(1), loc: pickLoc(2), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Shifting }], flow: "schedule-urgent" },
        { code: "S04", desc: "Cancelled Schedule while pending review", cust: pickCust(5), ves: pickVes(6), loc: pickLoc(7), tugs: [{ tugboatId: pickTug(3), serviceId: SVC.Standby }], flow: "schedule-cancel" },
        { code: "S05", desc: "Approved -> Revised Schedule (Pending Re-approval)", cust: pickCust(7), ves: pickVes(8), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }], flow: "schedule-revised-pending" },
        { code: "S06", desc: "Approved -> Revised -> Re-approved Schedule", cust: principalCust, ves: pickVes(3), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickTug(0), serviceId: SVC.Undocking }], flow: "schedule-revised-reapproved" },
        { code: "S07", desc: "Outdated Rate on Pending Schedule (unresolved)", cust: pickCust(0), ves: pickVes(2), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Shifting }], flow: "outdated-schedule-pending" },
        { code: "S08", desc: "Outdated Rate on Schedule -> Adopt Table Rate -> Approved", cust: pickCust(1), ves: pickVes(5), loc: pickLoc(2), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Escorting }], flow: "outdated-schedule-adopted" },
        { code: "S09", desc: "Approved COS (awaiting dispatch tickets)", cust: pickCust(2), ves: pickVes(7), loc: subicLoc, tugs: [{ tugboatId: pickTug(1), serviceId: SVC.Docking }, { tugboatId: pickTug(1), serviceId: SVC.Watering }], flow: "cos-approved" },
        { code: "S10", desc: "Approved COS with Partial Tickets (1 of 2 recorded)", cust: repCust, ves: pickVes(9), loc: pickLoc(6), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Docking }, { tugboatId: pickTug(2), serviceId: SVC.Undocking }], flow: "cos-partial-tickets" },
        { code: "S11", desc: "Approved COS with Full Tickets (Docking+Watering)", cust: pickCust(0), ves: pickVes(0), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickTug(0), serviceId: SVC.Watering }], flow: "cos-full-tickets" },
        { code: "S12", desc: "COS with 1 Cancelled Service + 1 Recorded Ticket", cust: pickCust(5), ves: pickVes(6), loc: pickLoc(7), tugs: [{ tugboatId: pickTug(3), serviceId: SVC.Docking }, { tugboatId: pickTug(3), serviceId: SVC.Standby }], flow: "cos-cancel-service" },
        { code: "S13", desc: "Cancelled COS after approval", cust: pickCust(7), ves: pickVes(2), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Shifting }], flow: "cos-cancel" },
        { code: "S14", desc: "Outdated Rate on Recorded Tickets", cust: pickCust(3), ves: pickVes(4), loc: pickLoc(5), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Escorting }], flow: "outdated-tickets" },
        { code: "S15", desc: "Billing - For Commercial Review (unendorsed)", cust: pickCust(0), ves: pickVes(1), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }], flow: "billing-review" },
        { code: "S16", desc: "Billing - Endorsed (pending Finance final approval)", cust: pickCust(7), ves: pickVes(3), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Undocking }], flow: "billing-endorsed" },
        { code: "S17", desc: "Billing - Approved (standard)", cust: pickCust(0), ves: pickVes(5), loc: pickLoc(2), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickTug(0), serviceId: SVC.Watering }], flow: "billing-approved" },
        { code: "S18", desc: "Billing - Percent Discount (customer default)", cust: discountedCust, ves: pickVes(7), loc: pickLoc(2), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickTug(0), serviceId: SVC.Undocking }], flow: "billing-approved" },
        { code: "S19", desc: "Billing - Percent Discount + SBMA Share Enabled", cust: pickCust(2), ves: pickVes(8), loc: subicLoc, tugs: [{ tugboatId: pickTug(1), serviceId: SVC.Docking }, { tugboatId: pickTug(1), serviceId: SVC.Shifting }], flow: "billing-sbma" },
        { code: "S20", desc: "Billing - Zero-Rated Customer", cust: pickCust(3), ves: pickVes(9), loc: pickLoc(5), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Docking }], flow: "billing-approved" },
        { code: "S21", desc: "Billing - Exempt Customer", cust: pickCust(5), ves: pickVes(0), loc: pickLoc(7), tugs: [{ tugboatId: pickTug(3), serviceId: SVC.Docking }, { tugboatId: pickTug(3), serviceId: SVC.Escorting }], flow: "billing-approved" },
        { code: "S22", desc: "Billing - Undocumented Billing (IsUndocumented=true)", cust: pickCust(7), ves: pickVes(1), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Standby }], flow: "billing-undocumented" },
        { code: "S23", desc: "Billing - Billed to Principal", cust: principalCust, ves: pickVes(2), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }], flow: "billing-principal" },
        { code: "S24", desc: "Billing - Principal + Agency Incentive & Agent Commission Reps", cust: repCust, ves: pickVes(3), loc: pickLoc(6), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Docking }, { tugboatId: pickTug(2), serviceId: SVC.Undocking }], flow: "billing-reps" },
        { code: "S25", desc: "Billing - Split Billing (SeparateBaf=true) -> Both Approved", cust: pickCust(1), ves: pickVes(4), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickTug(0), serviceId: SVC.Shifting }], flow: "billing-split" },
        { code: "S26", desc: "Billing - Rejected -> Adjusted (7.5% Tariff-Only + Rate Override) -> Approved", cust: pickCust(2), ves: pickVes(5), loc: subicLoc, tugs: [{ tugboatId: pickTug(1), serviceId: SVC.Docking }], flow: "billing-reject-adjust-percent" },
        { code: "S27", desc: "Billing - Rejected -> Adjusted (Flat Rate Discount Basic+BAF) -> Approved", cust: pickCust(0), ves: pickVes(6), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickTug(0), serviceId: SVC.Undocking }], flow: "billing-reject-adjust-flat" },
        { code: "S28", desc: "Billing - Reopened -> Subcon + IncludeSubconInDiscount + Flat Discount -> Re-approved", cust: repCust, ves: pickVes(7), loc: pickLoc(2), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }, { tugboatId: pickSubcon(), serviceId: SVC.Escorting }], flow: "billing-reopen-adjust-subcon-flat" },
        { code: "S29", desc: "Collection - Check Undeposited", cust: pickCust(0), ves: pickVes(8), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }], flow: "coll-undeposited" },
        { code: "S30", desc: "Collection - Check Deposited", cust: pickCust(1), ves: pickVes(9), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Undocking }], flow: "coll-deposited" },
        { code: "S31", desc: "Collection - Check Cleared (Full 9-Stage)", cust: pickCust(2), ves: pickVes(0), loc: subicLoc, tugs: [{ tugboatId: pickTug(1), serviceId: SVC.Docking }, { tugboatId: pickTug(1), serviceId: SVC.Watering }], flow: "coll-cleared" },
        { code: "S32", desc: "Collection - Cash Payment -> Cleared", cust: pickCust(5), ves: pickVes(1), loc: pickLoc(7), tugs: [{ tugboatId: pickTug(3), serviceId: SVC.Shifting }], flow: "coll-cash-cleared" },
        { code: "S33", desc: "Collection - Undocumented Collection -> Cleared", cust: pickCust(7), ves: pickVes(2), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Escorting }], flow: "coll-undocumented-cleared" },
        { code: "S34", desc: "Collection - Edited Receipt (Collection/Edit) -> Cleared", cust: pickCust(3), ves: pickVes(3), loc: pickLoc(5), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Docking }], flow: "coll-edited-cleared" },
        { code: "S35", desc: "Collection - Returned Check (DAIF)", cust: principalCust, ves: pickVes(4), loc: pickLoc(2), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }], flow: "coll-returned" },
        { code: "S36", desc: "Collection - Returned Check -> Redeposited -> Cleared", cust: repCust, ves: pickVes(5), loc: pickLoc(6), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Undocking }], flow: "coll-returned-redeposit-cleared" },
        { code: "S37", desc: "Collection - Banking Reversal (Deposited -> Undeposited)", cust: pickCust(0), ves: pickVes(6), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Shifting }], flow: "coll-revert-deposited" },
        { code: "S38", desc: "Collection - Banking Reversal (Cleared -> Deposited)", cust: pickCust(1), ves: pickVes(7), loc: pickLoc(2), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }], flow: "coll-revert-cleared" },
        { code: "S39", desc: "Collection - Voided Receipt", cust: pickCust(2), ves: pickVes(8), loc: subicLoc, tugs: [{ tugboatId: pickTug(1), serviceId: SVC.Standby }], flow: "coll-voided" },
        { code: "S40", desc: "Collection - Voided Receipt -> Re-collected & Cleared", cust: pickCust(7), ves: pickVes(9), loc: pickLoc(1), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Docking }], flow: "coll-void-recollect-cleared" },
        { code: "S41", desc: "Split Billing (Basic+BAF) -> Combined Multi-Billing Collection -> Cleared", cust: pickCust(3), ves: pickVes(0), loc: pickLoc(5), tugs: [{ tugboatId: pickTug(2), serviceId: SVC.Docking }, { tugboatId: pickTug(2), serviceId: SVC.Undocking }], flow: "coll-split-combined-cleared" },
        { code: "S42", desc: "Billing - Active Revision Requested (Rejected)", cust: principalCust, ves: pickVes(1), loc: pickLoc(0), tugs: [{ tugboatId: pickTug(0), serviceId: SVC.Escorting }], flow: "billing-rejected-active" }
    ];

    const results = [];
    const runTag = String(Date.now()).slice(-4);
    const items = SCENARIOS
        .map((sc, idx) => ({ sc, idx }))
        .filter(({ sc }) => (!OPTS.only || OPTS.only.has(sc.code)) && (!OPTS.skip || !OPTS.skip.has(sc.code)));

    const workerCount = Math.min(OPTS.concurrency, Math.max(1, items.length));
    const chunkSize = Math.ceil(items.length / workerCount);
    const chunks = [];
    for (let i = 0; i < items.length; i += chunkSize) {
        chunks.push(items.slice(i, i + chunkSize));
    }

    async function newTab(roleName) {
        const p = await roles[roleName].ctx.newPage();
        p.setDefaultTimeout(15000);
        p.setDefaultNavigationTimeout(18000);
        await p.goto(`${BASE}/MSAP`, { waitUntil: "domcontentloaded" }).catch(() => {});
        return p;
    }

    console.log(`Running ${items.length} scenario(s) across ${chunks.length} concurrent worker(s) (sharing single-session role contexts)...`);

    await Promise.all(chunks.map(async (chunk, wIdx) => {
        const w = wIdx === 0
            ? { OPS: roles.OPS.page, REV: roles.REV.page, ENC: roles.ENC.page, FIN: roles.FIN.page, ADM: roles.ADM.page }
            : {
                OPS: await newTab("OPS"),
                REV: await newTab("REV"),
                ENC: await newTab("ENC"),
                FIN: await newTab("FIN"),
                ADM: await newTab("ADM")
            };
        const {
            createSchedule, approveRates, approveSchedule, cancelSchedule, reviseSchedule,
            urgentEntry, adoptTableRates, recordTickets, cancelCosService, cancelCos,
            createBilling, endorseBilling, approveBilling, rejectBilling, reopenBilling,
            adjustBilling, createCollection, editCollection, collectionBanking,
            revertCollectionBanking, voidCollection
        } = createWorkerHelpers(w);

        for (const { sc, idx } of chunk) {
        const dateStr = addDays(OPTS.startDate, idx);
        const startHour = 6 + ((idx * 3) % 12);
        const rec = { code: sc.code, desc: sc.desc, date: dateStr, startHour, status: "OK" };

        try {
            const scheduleId = await createSchedule({
                dateStr,
                startHour,
                customerId: sc.cust,
                vesselId: sc.ves,
                portId: sc.loc.portId,
                terminalId: sc.loc.terminalId,
                tugServices: sc.tugs,
                voyage: `VOY-${sc.code}-${dateStr.replace(/-/g, "")}`,
                notes: `[${sc.code}] ${sc.desc}`
            });
            rec.scheduleId = scheduleId;

            const baseRate = 16000 + (idx % 7) * 1500;
            const baseBaf = 2200 + (idx % 5) * 300;

            if (sc.flow === "schedule-draft") {
                // done
            } else if (sc.flow === "schedule-urgent") {
                rec.cosId = await urgentEntry(scheduleId);
            } else if (sc.flow === "schedule-cancel") {
                await cancelSchedule(scheduleId, `[${sc.code}] Voyage cancelled by customer.`);
            } else if (sc.flow === "schedule-revised-pending") {
                await approveRates(scheduleId, dateStr, { basicRate: baseRate, bafRate: baseBaf });
                rec.cosId = await approveSchedule(scheduleId);
                await reviseSchedule(scheduleId, `VOY-${sc.code}-REV1`, `[${sc.code}] Revised voyage pending re-approval.`);
            } else if (sc.flow === "schedule-revised-reapproved") {
                await approveRates(scheduleId, dateStr, { basicRate: baseRate, bafRate: baseBaf });
                rec.cosId = await approveSchedule(scheduleId);
                await reviseSchedule(scheduleId, `VOY-${sc.code}-REV1`, `[${sc.code}] Revised voyage and re-approved.`);
                await approveSchedule(scheduleId);
            } else if (sc.flow === "outdated-schedule-pending") {
                await approveRates(scheduleId, dateStr, { basicRate: 17000, bafRate: 2100, forceUpdate: true });
                await approveRates(scheduleId, dateStr, { basicRate: 20500, bafRate: 2750, forceUpdate: true });
            } else if (sc.flow === "outdated-schedule-adopted") {
                await approveRates(scheduleId, dateStr, { basicRate: 17500, bafRate: 2200, forceUpdate: true });
                await approveRates(scheduleId, dateStr, { basicRate: 21000, bafRate: 2900, forceUpdate: true });
                rec.adoptedCount = await adoptTableRates(scheduleId);
                rec.cosId = await approveSchedule(scheduleId);
            } else {
                await approveRates(scheduleId, dateStr, { basicRate: baseRate, bafRate: baseBaf });
                const cosId = await approveSchedule(scheduleId);
                rec.cosId = cosId;

                if (sc.flow === "cos-approved") {
                    // done
                } else if (sc.flow === "cos-cancel") {
                    await cancelCos(cosId, `[${sc.code}] COS cancelled before service.`);
                } else if (sc.flow === "cos-partial-tickets") {
                    rec.tickets = await recordTickets(cosId, dateStr, { maxTickets: 1, startHour, ticketPrefix: sc.code });
                } else if (sc.flow === "cos-full-tickets") {
                    rec.tickets = await recordTickets(cosId, dateStr, { startHour, ticketPrefix: sc.code });
                } else if (sc.flow === "cos-cancel-service") {
                    rec.tickets = await recordTickets(cosId, dateStr, { maxTickets: 1, startHour, ticketPrefix: sc.code });
                    await cancelCosService(cosId, `[${sc.code}] Secondary service cancelled.`);
                } else if (sc.flow === "outdated-tickets") {
                    rec.tickets = await recordTickets(cosId, dateStr, { startHour, ticketPrefix: sc.code });
                    await approveRates(scheduleId, dateStr, { basicRate: baseRate + 4000, bafRate: baseBaf + 700, forceUpdate: true });
                } else {
                    rec.tickets = await recordTickets(cosId, dateStr, { startHour, ticketPrefix: sc.code });
                    const { billingId, bafBillingId } = await createBilling(cosId, {
                        dateStr,
                        isUndocumented: sc.flow === "billing-undocumented",
                        separateBaf: sc.flow === "billing-split" || sc.flow === "coll-split-combined-cleared",
                        sbmaEnabled: sc.flow === "billing-sbma",
                        billToPrincipal: sc.flow === "billing-principal" || sc.flow === "billing-reps",
                        useReps: sc.flow === "billing-reps",
                        remarks: `[${sc.code}] ${sc.desc}`
                    });
                    rec.billingId = billingId;
                    if (bafBillingId) rec.bafBillingId = bafBillingId;

                    if (sc.flow === "billing-review") {
                        // done
                    } else if (sc.flow === "billing-endorsed") {
                        await endorseBilling(billingId);
                    } else if (sc.flow === "billing-rejected-active") {
                        await rejectBilling(billingId, `[${sc.code}] Please verify rate & discount before endorsement.`);
                    } else if (sc.flow === "billing-reject-adjust-percent") {
                        await rejectBilling(billingId, `[${sc.code}] Apply 7.5% Tariff-only discount override.`);
                        await adjustBilling(billingId, {
                            basicRate: baseRate + 1200,
                            bafRate: baseBaf + 200,
                            discountMode: "Percent",
                            discountPercent: 7.5,
                            scope: "tariff",
                            reason: `[${sc.code}] Authorized 7.5% Tariff-only discount & rate update.`
                        });
                        await endorseBilling(billingId);
                        await approveBilling(billingId);
                    } else if (sc.flow === "billing-reject-adjust-flat") {
                        await rejectBilling(billingId, `[${sc.code}] Apply flat rate discount per commercial memo.`);
                        await adjustBilling(billingId, {
                            discountMode: "Flat",
                            flatTariff: 2500,
                            flatBaf: 500,
                            scope: "both",
                            reason: `[${sc.code}] Authorized flat discount (Basic 2,500 + BAF 500).`
                        });
                        await endorseBilling(billingId);
                        await approveBilling(billingId);
                    } else if (sc.flow === "billing-reopen-adjust-subcon-flat") {
                        await endorseBilling(billingId);
                        await approveBilling(billingId);
                        await reopenBilling(billingId, `[${sc.code}] Reopen to include Subcon ticket in flat discount.`);
                        await adjustBilling(billingId, {
                            discountMode: "Flat",
                            flatTariff: 3000,
                            flatBaf: 400,
                            scope: "both",
                            includeSubcon: true,
                            reason: `[${sc.code}] Post-approval reopen: included Subcon ticket in flat discount.`
                        });
                        await endorseBilling(billingId);
                        await approveBilling(billingId);
                    } else {
                        await endorseBilling(billingId);
                        await approveBilling(billingId);
                        if (bafBillingId) {
                            await endorseBilling(bafBillingId);
                            await approveBilling(bafBillingId);
                        }

                        if (sc.flow.startsWith("coll-")) {
                            const cOpts = {
                                dateStr,
                                receiptNo: `C${sc.code.slice(1)}-${runTag}`,
                                refNo: `REF-${sc.code}-${runTag}`,
                                checkNo: `CHK-${sc.code}-${runTag}`,
                                isCash: sc.flow === "coll-cash-cleared",
                                isUndocumented: sc.flow === "coll-undocumented-cleared",
                                selectAll: sc.flow === "coll-split-combined-cleared",
                                remarks: `[${sc.code}] ${sc.desc}`
                            };
                            const collId = await createCollection(cosId, cOpts);
                            rec.collectionId = collId;

                            if (sc.flow === "coll-undeposited") {
                                // done
                            } else if (sc.flow === "coll-deposited") {
                                await collectionBanking(collId, "Record deposit", { bankIndex: 0 });
                            } else if (["coll-cleared", "coll-cash-cleared", "coll-undocumented-cleared", "coll-split-combined-cleared"].includes(sc.flow)) {
                                await collectionBanking(collId, "Record deposit", { bankIndex: idx % 2 });
                                await collectionBanking(collId, "Record clearing");
                            } else if (sc.flow === "coll-edited-cleared") {
                                await editCollection(collId, `REF-${sc.code}-EDT`, `[${sc.code}] Edited receipt details prior to deposit.`);
                                await collectionBanking(collId, "Record deposit", { bankIndex: 1 });
                                await collectionBanking(collId, "Record clearing");
                            } else if (sc.flow === "coll-returned") {
                                await collectionBanking(collId, "Record deposit", { bankIndex: 0 });
                                await collectionBanking(collId, "Record returned check", { reason: "DAIF - Drawn Against Insufficient Funds" });
                            } else if (sc.flow === "coll-returned-redeposit-cleared") {
                                await collectionBanking(collId, "Record deposit", { bankIndex: 0 });
                                await collectionBanking(collId, "Record returned check", { reason: "DAIF - First presentation bounced" });
                                await collectionBanking(collId, "Record redeposit", { bankIndex: 1 });
                                await collectionBanking(collId, "Record clearing");
                            } else if (sc.flow === "coll-revert-deposited") {
                                await collectionBanking(collId, "Record deposit", { bankIndex: 0 });
                                await revertCollectionBanking(collId, `[${sc.code}] Reverted deposit to correct bank account.`);
                            } else if (sc.flow === "coll-revert-cleared") {
                                await collectionBanking(collId, "Record deposit", { bankIndex: 1 });
                                await collectionBanking(collId, "Record clearing");
                                await revertCollectionBanking(collId, `[${sc.code}] Reverted clearing back to Deposited.`);
                            } else if (sc.flow === "coll-voided") {
                                await voidCollection(collId, `[${sc.code}] Voided receipt due to wrong check number.`);
                            } else if (sc.flow === "coll-void-recollect-cleared") {
                                await voidCollection(collId, `[${sc.code}] Voided initial receipt to re-issue.`);
                                const collId2 = await createCollection(cosId, {
                                    ...cOpts,
                                    receiptNo: `R${sc.code.slice(1)}-${runTag}`,
                                    refNo: `REF-${sc.code}-RE`,
                                    checkNo: `CHK-${sc.code}-B`,
                                    remarks: `[${sc.code}] Replacement receipt after voiding #${collId}.`
                                });
                                rec.replacementCollectionId = collId2;
                                await collectionBanking(collId2, "Record deposit", { bankIndex: 0 });
                                await collectionBanking(collId2, "Record clearing");
                            }
                        }
                    }
                }
            }
            console.log(`PASS ${sc.code} (${dateStr} ${String(startHour).padStart(2, "0")}:00) | sched=${rec.scheduleId} cos=${rec.cosId || "-"} bill=${rec.billingId || "-"}${rec.bafBillingId ? `+${rec.bafBillingId}` : ""} coll=${rec.collectionId || "-"}`);
        } catch (err) {
            rec.status = "FAIL";
            rec.error = err.message;
            console.error(`FAIL ${sc.code} (${sc.flow}): ${err.message}`);
        }
        results.push(rec);
        }
    }));
    results.sort((a, b) => a.code.localeCompare(b.code));

    const outPath = path.resolve("artifacts/proof-verify/latest-seed-scenarios.json");
    fs.mkdirSync(path.dirname(outPath), { recursive: true });
    fs.writeFileSync(outPath, JSON.stringify({ startDate: OPTS.startDate, total: results.length, passed: results.filter((r) => r.status === "OK").length, failed: results.filter((r) => r.status === "FAIL").length, results }, null, 2));
    const passed = results.filter((r) => r.status === "OK").length;
    const failed = results.filter((r) => r.status === "FAIL").length;
    console.log(`\nDONE: ${passed}/${results.length} PASSED, ${failed} FAILED -> ${outPath}`);
    if (failed > 0) process.exitCode = 1;
} finally {
    await browser.close();
    fs.rmSync(FIX_DIR, { recursive: true, force: true });
}
