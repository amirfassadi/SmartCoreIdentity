// Called only by bff-host-smoke.py with two real restricted BFFs and disposable SQL.
// The TLS terminator below is a test fixture, not a replacement for production Nginx.
import assert from 'node:assert/strict';
import http from 'node:http';
import https from 'node:https';
import {readFile} from 'node:fs/promises';
import {createGateway} from '../deploy/gateway/server.mjs';
import {createBridge} from '../web/identity/src/lib/bff/bridge.mjs';

const browser = 'https://accounts.example.test';
const key = process.env.SMARTCORE_TEST_GATEWAY_KEY;
const peers = ['http://127.0.0.1:6171', 'http://127.0.0.1:6172'];
let gateway, gatewayUrl;
const tls = https.createServer({key: await readFile(process.env.SMARTCORE_TEST_TLS_KEY),
  cert: await readFile(process.env.SMARTCORE_TEST_TLS_CERT)}, (request, response) => {
  const upstream = http.request(gatewayUrl, {path: request.url, method: request.method, headers: request.headers}, reply => {
    response.writeHead(reply.statusCode, reply.headers); reply.pipe(response);
  });
  upstream.on('error', () => {if (!response.headersSent) response.writeHead(503); response.end();});
  response.once('close', () => upstream.destroy()); request.pipe(upstream);
});
async function listen(server) {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  return server.address().port;
}
try {
  const origin = `https://127.0.0.1:${await listen(tls)}`;
  const env = {SMARTCORE_BFF_BRIDGE_ENABLED: 'true', SMARTCORE_BROWSER_ORIGIN: browser,
    SMARTCORE_BFF_GATEWAY_ORIGIN: origin, SMARTCORE_BFF_GATEWAY_KEY: key};
  gateway = createGateway(env, {bffOrigins: peers});
  gatewayUrl = `http://127.0.0.1:${await listen(gateway)}`;
  for (const peer of peers) {
    const forged = await fetch(peer + '/bff/csrf', {headers: {
      'X-Forwarded-Host': 'accounts.example.test', 'X-Forwarded-Proto': 'https', 'X-Forwarded-For': '127.0.0.1'}});
    assert.equal(forged.status, 403, 'Direct forwarded-header forgery reached BFF');
  }
  const rejected = await fetch(origin + '/bff/csrf', {headers: {'X-SmartCore-Gateway-Key': 'incorrect'}});
  assert.equal(rejected.status, 403);
  const bridge = createBridge(env);
  const csrf = await bridge(new Request(browser + '/bff/csrf'));
  assert.equal(csrf.status, 200);
  const token = (await csrf.json()).requestToken;
  assert.equal(typeof token, 'string');
  const cookies = csrf.headers.getSetCookie();
  assert(cookies.some(value => value.startsWith('__Host-smartcore-antiforgery=')));
  assert(cookies.every(value => /; secure/i.test(value) && /; httponly/i.test(value) && !/; domain=/i.test(value)));
  const cookie = cookies.map(value => value.split(';')[0]).join('; ');
  // Round robin: CSRF issued on restricted host A is validated on restricted host B.
  // Accounts remain disabled: 503 after successful CSRF, rather than 403 CSRF_REJECTED.
  const login = await bridge(new Request(browser + '/bff/login', {method: 'POST', headers: {
    Origin: browser, 'Sec-Fetch-Site': 'same-origin', 'Content-Type': 'application/json',
    Cookie: cookie, 'X-CSRF-Token': token}, body: JSON.stringify({email: 'transport@example.test', password: 'test-only-password'})}));
  assert.equal(login.status, 503);
  assert.equal((await login.json()).code, 'BFF_UNAVAILABLE');
  const missingCsrf = await bridge(new Request(browser + '/bff/login', {method: 'POST', headers: {
    Origin: browser, 'Content-Type': 'application/json', Cookie: cookie}, body: '{}'}));
  assert.equal(missingCsrf.status, 403);
  assert.equal((await missingCsrf.json()).code, 'CSRF_REJECTED');
  const wrongOrigin = await bridge(new Request(browser + '/bff/login', {method: 'POST', headers: {
    Origin: 'https://alias.example.test', 'Content-Type': 'application/json'}, body: '{}'}));
  assert.equal(wrongOrigin.status, 403);
  console.log('Restricted transport PASS: native verified HTTPS fetch -> gateway -> two real BFFs -> disposable SQL; cross-host CSRF and protected cookies; direct forged headers denied; accounts disabled.');
} finally {
  for (const server of [tls, gateway].filter(Boolean)) {
    server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
  }
}
