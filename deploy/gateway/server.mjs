// Authenticated loopback gateway behind an operator-managed HTTPS Nginx vhost.
// Never log requests, credentials, exception messages, response bodies or cookies.
import http from 'node:http';
import {createHash, timingSafeEqual} from 'node:crypto';
import {readFile, lstat} from 'node:fs/promises';
import {dirname, resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
import {browserRoutes, filterRequestCookies, filterResponseCookie} from '../../web/identity/src/lib/bff/bridge.mjs';

const securityHeaders = {'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY', 'Referrer-Policy': 'no-referrer'};
function httpsOrigin(value) {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.pathname !== '/' || url.search || url.hash || url.username || url.password
    || !url.hostname.includes('.') || url.hostname.includes('replace_')) throw new Error('Fixed HTTPS origin required');
  return url;
}
export async function readSettings(path) {
  const file = resolve(path);
  for (const target of [dirname(file), file]) {
    const info = await lstat(target);
    if (info.isSymbolicLink() || info.uid !== process.getuid() || (info.mode & 0o077)
      || (target === file ? !info.isFile() : !info.isDirectory()) || target === file && info.size > 16384)
      throw new Error('Private settings file/directory required');
  }
  const env = {};
  for (const raw of (await readFile(file, 'utf8')).split(/\r?\n/)) {
    const line = raw.trim(); if (!line || line.startsWith('#')) continue;
    const match = /^([A-Z][A-Z0-9_]*)=([^\s'"`$\\]*)$/.exec(line);
    if (!match || Object.hasOwn(env, match[1])) throw new Error('Invalid settings syntax');
    env[match[1]] = match[2];
  }
  return env;
}
export function createGateway(env, {bffOrigins = ['http://127.0.0.1:5171'], timeoutMs = 45000} = {}) {
  if (env.SMARTCORE_BFF_BRIDGE_ENABLED !== 'true') throw new Error('Gateway activation is disabled');
  const browser = httpsOrigin(env.SMARTCORE_BROWSER_ORIGIN);
  const gateway = httpsOrigin(env.SMARTCORE_BFF_GATEWAY_ORIGIN);
  if (browser.origin === gateway.origin || !/^[A-Za-z0-9_-]{43,128}$/.test(env.SMARTCORE_BFF_GATEWAY_KEY ?? ''))
    throw new Error('Independent gateway configuration required');
  const digest = value => createHash('sha256').update(value).digest();
  const expectedKey = digest(env.SMARTCORE_BFF_GATEWAY_KEY);
  if (bffOrigins.length < 1 || bffOrigins.length > 2) throw new Error('One or two BFF peers required');
  const peers = bffOrigins.map(value => {
    const url = new URL(value);
    if (url.protocol !== 'http:' || !['127.0.0.1', '[::1]'].includes(url.hostname)
      || url.pathname !== '/' || url.search || url.hash || url.username || url.password) throw new Error('Loopback BFF roots required');
    return url;
  });
  let turn = 0;
  const server = http.createServer(async (request, response) => {
    function fail(status, code, extra = {}) {
      if (response.headersSent) {response.destroy(); return;}
      const body = JSON.stringify(status === 503 ? {code, retryAfterSeconds: 60} : {code});
      response.writeHead(status, {...securityHeaders, 'Connection': 'close', 'Content-Type': 'application/json; charset=utf-8',
        'Content-Length': String(Buffer.byteLength(body)), ...(status === 503 ? {'Retry-After': '60'} : {}), ...extra});
      response.end(body); request.resume();
    }
    // Reject all public listeners/connections, including accidental imports into another server.
    if (!['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(request.socket.remoteAddress)) {fail(403, 'ORIGIN_REJECTED'); return;}
    const supplied = request.headers['x-smartcore-gateway-key'];
    if (typeof supplied !== 'string' || !timingSafeEqual(digest(supplied), expectedKey)) {fail(403, 'ORIGIN_REJECTED'); return;}
    if (request.headers.host !== gateway.host) {fail(403, 'ORIGIN_REJECTED'); return;}
    // Raw exact route lookup: encoded separators, dot segments and query strings never dispatch.
    const method = browserRoutes.get(request.url);
    if (!method) {fail(404, 'NOT_FOUND'); return;}
    if (request.method !== method) {fail(405, 'METHOD_NOT_ALLOWED', {Allow: method}); return;}
    const chunks = []; let size = 0;
    try {
      for await (const chunk of request.iterator({destroyOnReturn: false})) {
        size += chunk.length;
        if (size > 16384) {fail(413, 'REQUEST_TOO_LARGE'); return;}
        chunks.push(chunk);
      }
    } catch {if (!response.destroyed) fail(503, 'BFF_UNAVAILABLE'); return;}
    if (method === 'POST' && !/^application\/json(?:\s*;\s*charset\s*=\s*(?:utf-8|"utf-8"))?\s*$/i.test(request.headers['content-type'] ?? '')) {
      fail(415, 'UNSUPPORTED_MEDIA_TYPE'); return;
    }
    const origin = request.headers.origin, site = request.headers['sec-fetch-site'];
    if ((method === 'POST' ? origin !== browser.origin : origin !== undefined && origin !== browser.origin)
      || site !== undefined && site !== 'same-origin' && site !== 'none') {fail(403, 'ORIGIN_REJECTED'); return;}
    const target = peers[turn++ % peers.length];
    const headers = {host: target.host, 'x-forwarded-host': browser.host, 'x-forwarded-proto': 'https',
      'x-forwarded-for': '127.0.0.1'};
    for (const name of ['accept', 'accept-language', 'content-type', 'origin', 'sec-fetch-site', 'sec-fetch-mode', 'sec-fetch-dest', 'x-csrf-token']) {
      if (request.headers[name] !== undefined) headers[name] = request.headers[name];
    }
    const cookie = filterRequestCookies(request.headers.cookie); if (cookie) headers.cookie = cookie;
    headers['content-length'] = String(size);
    const upstream = http.request(target, {method, path: request.url, headers, agent: false}, incoming => {
      const parts = []; let used = 0;
      incoming.on('error', () => fail(503, 'BFF_UNAVAILABLE'));
      incoming.on('data', chunk => {
        used += chunk.length;
        if (used > 1048576) {incoming.destroy(); fail(503, 'BFF_UNAVAILABLE'); return;}
        parts.push(chunk);
      });
      incoming.on('end', () => {
        if (response.writableEnded || response.destroyed) return;
        try {
          const status = incoming.statusCode ?? 502;
          if (status >= 300 && status < 400 || status < 200
            || incoming.headers['content-encoding'] && incoming.headers['content-encoding'] !== 'identity'
            || status !== 204 && !/^application\/json(?:;|$)/i.test(incoming.headers['content-type'] ?? ''))
            throw new Error('Unexpected BFF response');
          const outgoing = {...securityHeaders};
          for (const name of ['content-type', 'retry-after', 'allow']) if (incoming.headers[name] !== undefined) outgoing[name] = incoming.headers[name];
          const cookies = (incoming.headers['set-cookie'] ?? []).map(filterResponseCookie).filter(Boolean);
          if (cookies.length) outgoing['set-cookie'] = cookies;
          response.writeHead(status, outgoing); response.end(status === 204 ? undefined : Buffer.concat(parts));
        } catch {fail(503, 'BFF_UNAVAILABLE');}
      });
    });
    const deadline = setTimeout(() => {upstream.destroy(); fail(503, 'BFF_UNAVAILABLE');}, timeoutMs);
    deadline.unref();
    response.once('close', () => {clearTimeout(deadline); upstream.destroy();});
    upstream.on('error', () => fail(503, 'BFF_UNAVAILABLE'));
    // A lost reply is ambiguous. Never retry the selected peer or fail over.
    upstream.end(Buffer.concat(chunks));
  });
  server.requestTimeout = 60000; server.headersTimeout = 10000;
  return server;
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const env = await readSettings(process.argv[2] ?? '/home/amir/.config/smartcore-identity/.env');
    const server = createGateway(env);
    server.on('error', () => {console.error('Gateway listener failed; details suppressed.'); process.exitCode = 1;});
    server.listen(5181, '127.0.0.1', () => console.log('Authenticated gateway listening on loopback.'));
    for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => server.close(() => process.exit(0)));
  } catch {console.error('Gateway configuration rejected; details suppressed.'); process.exitCode = 1;}
}
