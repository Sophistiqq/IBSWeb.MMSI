#!/usr/bin/env node

import http from "node:http";
import crypto from "node:crypto";

const PORT = Number.parseInt(process.env.GUIDED_HANDS_BRIDGE_PORT || "17373", 10);
const HOST = "127.0.0.1";
const WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

let extSocket = null;
let nextRpcId = 1;
const pending = new Map();

function encodeWsTextFrame(text) {
    const payload = Buffer.from(text, "utf8");
    const len = payload.length;
    let header;
    if (len < 126) {
        header = Buffer.alloc(2);
        header[0] = 0x81;
        header[1] = len;
    } else if (len < 65536) {
        header = Buffer.alloc(4);
        header[0] = 0x81;
        header[1] = 126;
        header.writeUInt16BE(len, 2);
    } else {
        header = Buffer.alloc(10);
        header[0] = 0x81;
        header[1] = 127;
        header.writeBigUInt64BE(BigInt(len), 2);
    }
    return Buffer.concat([header, payload]);
}

function encodeWsPongFrame(payload = Buffer.alloc(0)) {
    const header = Buffer.from([0x8a, payload.length]);
    return Buffer.concat([header, payload]);
}

function attachWsDecoder(socket, onMessage, onClose) {
    let buf = Buffer.alloc(0);

    socket.on("data", (chunk) => {
        buf = Buffer.concat([buf, chunk]);
        while (buf.length >= 2) {
            const first = buf[0];
            const second = buf[1];
            const opcode = first & 0x0f;
            const masked = (second & 0x80) !== 0;
            let payloadLen = second & 0x7f;
            let offset = 2;

            if (payloadLen === 126) {
                if (buf.length < 4) return;
                payloadLen = buf.readUInt16BE(2);
                offset = 4;
            } else if (payloadLen === 127) {
                if (buf.length < 10) return;
                payloadLen = Number(buf.readBigUInt64BE(2));
                offset = 10;
            }

            const maskLen = masked ? 4 : 0;
            if (buf.length < offset + maskLen + payloadLen) return;

            const maskKey = masked ? buf.subarray(offset, offset + 4) : null;
            offset += maskLen;
            const payload = Buffer.from(buf.subarray(offset, offset + payloadLen));
            buf = buf.subarray(offset + payloadLen);

            if (masked && maskKey) {
                for (let i = 0; i < payload.length; i++) {
                    payload[i] ^= maskKey[i % 4];
                }
            }

            if (opcode === 0x8) {
                socket.end();
                return;
            }
            if (opcode === 0x9) {
                socket.write(encodeWsPongFrame(payload));
                continue;
            }
            if (opcode === 0x1) {
                onMessage(payload.toString("utf8"));
            }
        }
    });

    socket.on("close", onClose);
    socket.on("error", onClose);
}

function sendRpcToExtension(action, params, timeoutMs = 30000) {
    if (!extSocket || extSocket.destroyed) {
        return Promise.reject(new Error("Guided Hands Chrome extension is not connected. Load the unpacked extension and click Reconnect."));
    }
    const id = nextRpcId++;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            if (pending.has(id)) {
                pending.delete(id);
                reject(new Error(`Extension RPC timeout for "${action}"`));
            }
        }, timeoutMs);

        pending.set(id, {
            resolve: (val) => { clearTimeout(timer); resolve(val); },
            reject: (err) => { clearTimeout(timer); reject(err); }
        });

        extSocket.write(encodeWsTextFrame(JSON.stringify({ id, action, params })));
    });
}

function readJsonBody(req) {
    return new Promise((resolve, reject) => {
        let raw = "";
        req.on("data", (c) => { raw += String(c); });
        req.on("end", () => {
            try {
                resolve(raw ? JSON.parse(raw) : {});
            } catch (err) {
                reject(err);
            }
        });
        req.on("error", reject);
    });
}

const server = http.createServer(async (req, res) => {
    res.setHeader("Content-Type", "application/json");
    if (req.method === "GET" && req.url === "/status") {
        res.end(JSON.stringify({
            ok: true,
            extensionConnected: Boolean(extSocket && !extSocket.destroyed),
            port: PORT
        }));
        return;
    }

    if (req.method === "POST" && req.url === "/rpc") {
        try {
            const body = await readJsonBody(req);
            const result = await sendRpcToExtension(body.action, body.params || {}, body.timeoutMs || 30000);
            res.end(JSON.stringify({ ok: true, result }));
        } catch (err) {
            res.statusCode = 500;
            res.end(JSON.stringify({ ok: false, error: err.message }));
        }
        return;
    }

    res.statusCode = 404;
    res.end(JSON.stringify({ ok: false, error: "Not found" }));
});

server.on("upgrade", (req, socket) => {
    if (req.url !== "/extension") {
        socket.destroy();
        return;
    }
    const key = req.headers["sec-websocket-key"];
    if (!key) {
        socket.destroy();
        return;
    }
    const accept = crypto.createHash("sha1").update(key + WS_GUID).digest("base64");
    socket.write(
        "HTTP/1.1 101 Switching Protocols\r\n"
        + "Upgrade: websocket\r\n"
        + "Connection: Upgrade\r\n"
        + `Sec-WebSocket-Accept: ${accept}\r\n\r\n`
    );

    if (extSocket && !extSocket.destroyed) {
        extSocket.destroy();
    }
    extSocket = socket;
    console.log(`[bridge] Chrome extension connected (${new Date().toISOString()})`);

    attachWsDecoder(
        socket,
        (text) => {
            let msg;
            try {
                msg = JSON.parse(text);
            } catch {
                return;
            }
            if (msg.type === "ping") {
                socket.write(encodeWsTextFrame(JSON.stringify({ type: "pong" })));
                return;
            }
            if (msg.id && pending.has(msg.id)) {
                const entry = pending.get(msg.id);
                pending.delete(msg.id);
                if (msg.ok) entry.resolve(msg.result);
                else entry.reject(new Error(msg.error || "Extension command failed"));
            }
        },
        () => {
            if (extSocket === socket) {
                extSocket = null;
                console.log(`[bridge] Chrome extension disconnected (${new Date().toISOString()})`);
            }
            for (const [, entry] of pending) {
                entry.reject(new Error("Chrome extension disconnected during RPC"));
            }
            pending.clear();
        }
    );
});

server.listen(PORT, HOST, () => {
    console.log(`[bridge] Guided Hands local bridge listening on http://${HOST}:${PORT} (WS: /extension)`);
});
