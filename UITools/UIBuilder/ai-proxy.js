#!/usr/bin/env node
/*
 * ai-proxy.js —— UI Builder 本地 AI 代理（绕过页面 CSP 外联白名单）
 *
 * 用途：
 *   页面所在环境（如受限 iframe / 沙箱预览）的 Content-Security-Policy
 *   只放行 http://127.0.0.1:* 等白名单域名，导致浏览器无法直连大模型接口。
 *   本服务监听 127.0.0.1:8788，把请求转发到真实接口（OpenAI 兼容 /chat/completions），
 *   并返回 CORS 头，页面请求走 127.0.0.1 即可绕过白名单。
 *
 * 用法：
 *   node ai-proxy.js
 *   然后在页面「AI 接口设置」勾选「本地代理模式」即可（无需改任何地址/Key）。
 *
 * 可选环境变量：
 *   AI_PROXY_PORT   监听端口（默认 8788）
 */
const http = require("http");
const https = require("https");

const PORT = Number(process.env.AI_PROXY_PORT || 8788);

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
  "Access-Control-Max-Age": "86400",
};

function sendJSON(res, status, obj) {
  res.writeHead(status, Object.assign({ "Content-Type": "application/json; charset=utf-8" }, CORS));
  res.end(JSON.stringify(obj));
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, "http://127.0.0.1:" + PORT);

  // CORS 预检
  if (req.method === "OPTIONS") {
    res.writeHead(204, CORS);
    res.end();
    return;
  }

  // 目标接口地址由 ?u= 携带（前端已 encodeURIComponent）
  const target = url.searchParams.get("u");
  if (!target) {
    sendJSON(res, 400, { error: "缺少 ?u= 参数（上游接口地址）。请使用页面里的「本地代理模式」，或手动请求 /proxy?u=<上游>/v1/chat/completions" });
    return;
  }
  let up;
  try { up = new URL(target); } catch (e) {
    sendJSON(res, 400, { error: "?u= 不是合法地址: " + target });
    return;
  }

  const chunks = [];
  req.on("data", c => chunks.push(c));
  req.on("end", () => {
    const body = Buffer.concat(chunks);
    const headers = { "Content-Type": req.headers["content-type"] || "application/json" };
    if (req.headers.authorization) headers.Authorization = req.headers.authorization;
    headers.Host = up.host;
    headers["Content-Length"] = body.length;

    const transport = up.protocol === "https:" ? https : http;
    const out = transport.request(
      { hostname: up.hostname, port: up.port || (up.protocol === "https:" ? 443 : 80), path: up.pathname + up.search, method: req.method, headers },
      r => {
        const rChunks = [];
        r.on("data", c => rChunks.push(c));
        r.on("end", () => {
          const outBody = Buffer.concat(rChunks);
          const outHeaders = Object.assign({}, CORS, { "Content-Type": r.headers["content-type"] || "application/json" });
          res.writeHead(r.statusCode || 502, outHeaders);
          res.end(outBody);
          console.log("[" + new Date().toLocaleTimeString() + "] " + req.method + " " + up.host + up.pathname + " -> " + (r.statusCode || 502) + " (" + outBody.length + "B)");
        });
      }
    );
    out.setTimeout(90000, () => out.destroy(new Error("upstream timeout")));
    out.on("error", e => {
      sendJSON(res, 502, { error: "转发上游失败: " + e.message });
      console.log("[" + new Date().toLocaleTimeString() + "] UPSTREAM ERROR " + up.host + " : " + e.message);
    });
    out.write(body);
    out.end();
  });
});

server.listen(PORT, "127.0.0.1", () => {
  console.log("UI Builder AI 代理已启动: http://127.0.0.1:" + PORT + "/proxy?u=<上游接口地址>");
  console.log("在页面「AI 接口设置」勾选「本地代理模式」即可使用。Ctrl+C 停止。");
});
