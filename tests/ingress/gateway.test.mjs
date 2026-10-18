import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import {mkdtemp, writeFile, chmod, rm, symlink} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createGateway, readSettings} from '../../deploy/gateway/server.mjs';
import {createBridge} from '../../web/identity/src/lib/bff/bridge.mjs';

const env = {SMARTCORE_BFF_BRIDGE_ENABLED: 'true', SMARTCORE_BROWSER_ORIGIN: 'https://identity.example.test',
  SMARTCORE_BFF_GATEWAY_ORIGIN: 'https://gateway.example.test', SMARTCORE_BFF_GATEWAY_KEY: 'K'.repeat(43)};
const servers = [];
async function listen(server) {
  servers.push(server); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  return `http://127.0.0.1:${server.address().port}`;
}
async function call(target, path = '/bff/login', headers = {}, body = '{}', method = 'POST') {
  const url = new URL(target);
  return new Promise((resolve, reject) => {
    const request = http.request({hostname: url.hostname, port: url.port, path, method, agent: false,
      headers: {Host: 'gateway.example.test', 'X-SmartCore-Gateway-Key': env.SMARTCORE_BFF_GATEWAY_KEY,
        Origin: env.SMARTCORE_BROWSER_ORIGIN, 'Sec-Fetch-Site': 'same-origin', 'Content-Type': 'application/json', ...headers}}, response => {
      const chunks = []; response.on('data', x => chunks.push(x)); response.on('error', reject);
      response.on('end', () => resolve({status: response.statusCode, headers: response.headers, body: Buffer.concat(chunks).toString()}));
    });
    request.on('error', reject); request.end(body);
  });
}
test('VPS gateway validates settings and authenticated transport over real HTTP sockets', async () => {
  const seen = [];
  const peers = [];
  try {
    for (const peer of ['a', 'b']) peers.push(await listen(http.createServer((request, response) => {
      const chunks = []; request.on('data', x => chunks.push(x)); request.on('end', () => {
        seen.push({peer, headers: request.headers, body: Buffer.concat(chunks).toString(), path: request.url});
        if (request.url === '/bff/password/change') {request.socket.destroy(); return;}
        if (request.url === '/bff/me') {response.writeHead(200, {'Content-Type': 'application/json'}); response.write('{'); return;}
        if (request.url === '/bff/recovery/enroll') {response.writeHead(200, {'Content-Type': 'application/json'}); response.end('x'.repeat(1048577)); return;}
        if (request.url === '/bff/me/action') {response.writeHead(302, {Location: 'https://evil.test'}); response.end(); return;}
        if (request.url === '/bff/register') {response.writeHead(200, {'Content-Type': 'text/html'}); response.end('<html>bad upstream</html>'); return;}
        response.setHeader('Content-Type', 'application/json');
        if (request.url === '/bff/csrf') response.setHeader('Set-Cookie', [
          '__Host-smartcore-antiforgery=proof; Path=/; Secure; HttpOnly; SameSite=Lax',
          '__Host-smartcore-registration=proof2; Path=/; Secure; HttpOnly; SameSite=Lax',
          'unknown-cookie=blocked; Path=/']);
        if (request.url === '/bff/register/resend') response.setHeader('Set-Cookie', '__Host-smartcore-session=bad; Path=/; HttpOnly; SameSite=Lax');
        if (request.url === '/bff/password/reset') {response.statusCode = 429; response.setHeader('Retry-After', '21600');}
        response.end(JSON.stringify({code: 'SYNTHETIC_BFF', peer}));
      });
    })));
    const target = await listen(createGateway(env, {bffOrigins: peers}));
    for (const invalid of [{SMARTCORE_BFF_BRIDGE_ENABLED: 'false'}, {SMARTCORE_BFF_GATEWAY_KEY: 'short'},
      {SMARTCORE_BFF_GATEWAY_ORIGIN: 'http://gateway.test'}, {SMARTCORE_BFF_GATEWAY_ORIGIN: 'https://REPLACE_WITH_BACKEND_HOSTNAME'}])
      assert.throws(() => createGateway({...env, ...invalid}));
    assert.throws(() => createGateway(env, {bffOrigins: ['http://public.test:5171']}));
    for (const headers of [{'X-SmartCore-Gateway-Key': ''}, {'X-SmartCore-Gateway-Key': 'forged'},
      {'X-SmartCore-Gateway-Key': [env.SMARTCORE_BFF_GATEWAY_KEY, env.SMARTCORE_BFF_GATEWAY_KEY]}, {Host: 'evil.test'}])
      assert.equal((await call(target, '/bff/login', headers)).status, 403);
    for (const path of ['/auth/login', '/dev/inbox', '/operator/recovery', '/bff/../auth/login', '/bff/%6cogin', '/bff/login?next=evil'])
      assert.equal((await call(target, path)).status, 404);
    assert.equal((await call(target, '/bff/csrf')).status, 405);
    assert.equal((await call(target, '/bff/login', {Origin: 'https://evil.test'})).status, 403);
    assert.equal((await call(target, '/bff/login', {'Content-Type': 'text/plain', Origin: 'https://evil.test'}, 'x'.repeat(16385))).status, 413);
    assert.equal((await call(target, '/bff/login', {'Content-Type': 'text/plain'})).status, 415);
    assert.equal(seen.length, 0, 'All local rejections must make zero BFF calls');
    const reply = await call(target, '/bff/login', {
      Cookie: `smartcore-locale=fa; unknown=secret; __Host-smartcore-session=${'A'.repeat(43)}; __Host-smartcore-antiforgery=proof`,
      Authorization: 'Bearer forged', 'X-CSRF-Token': 'proof', 'X-Forwarded-Host': 'evil.test', 'X-Forwarded-Proto': 'http',
      'X-Forwarded-For': '203.0.113.1', 'X-Bff-Client-Key': 'forged', 'X-Dev-Inbox-Key': 'forged'});
    assert.equal(reply.status, 200); assert.equal(reply.headers['cache-control'], 'no-store');
    const forwarded = seen.at(-1).headers;
    for (const name of ['authorization', 'x-smartcore-gateway-key', 'x-bff-client-key', 'x-dev-inbox-key']) assert.equal(forwarded[name], undefined);
    assert.equal(forwarded['x-forwarded-host'], 'identity.example.test'); assert.equal(forwarded['x-forwarded-proto'], 'https');
    assert.equal(forwarded['x-forwarded-for'], '127.0.0.1'); assert.equal(forwarded['x-csrf-token'], 'proof');
    assert.equal(forwarded.cookie, `__Host-smartcore-session=${'A'.repeat(43)}; __Host-smartcore-antiforgery=proof`);
    const csrf = await call(target, '/bff/csrf', {}, '', 'GET'); assert.equal(csrf.headers['set-cookie'].length, 2);
    assert(seen.some(x => x.peer === 'a') && seen.some(x => x.peer === 'b'));
    assert.equal((await call(target, '/bff/register/resend')).status, 503);
    assert.equal((await call(target, '/bff/me/action')).status, 503);
    assert.equal((await call(target, '/bff/register')).status, 503);
    const before = seen.length;
    assert.equal((await call(target, '/bff/password/change')).status, 503);
    assert.equal(seen.length, before + 1, 'No retry/failover after ambiguous connection loss');
    const cooldown = await call(target, '/bff/password/reset'); assert.equal(cooldown.status, 429); assert.equal(cooldown.headers['retry-after'], '21600');
    assert.equal((await call(target, '/bff/recovery/enroll')).status, 503);
    const shortBudget = await listen(createGateway(env, {bffOrigins: peers, timeoutMs: 30}));
    const slowBefore = seen.length;
    assert.equal((await call(shortBudget, '/bff/me')).status, 503);
    assert.equal(seen.length, slowBefore + 1, 'Deadline covers response body; no failover');
    // Exercise the actual bridge + actual HTTP gateway together. Only the TLS hop is
    // substituted by this explicit test adapter; no claim of public TLS/.NET acceptance.
    const bridge = createBridge(env, async (url, options) => {
      assert.equal(url.origin, 'https://gateway.example.test');
      const result = await call(target, url.pathname, Object.fromEntries(options.headers),
        options.body ? Buffer.from(options.body) : '', options.method);
      const headers = new Headers();
      for (const [name, value] of Object.entries(result.headers)) {
        if (Array.isArray(value)) for (const item of value) headers.append(name, item);
        else if (value !== undefined) headers.set(name, value);
      }
      return new Response(result.body, {status: result.status, headers});
    });
    const bridged = await bridge(new Request(env.SMARTCORE_BROWSER_ORIGIN + '/bff/csrf'));
    assert.equal(bridged.status, 200); assert.equal(bridged.headers.getSetCookie().length, 2);
  } finally {
    await Promise.all(servers.splice(0).map(server => new Promise(resolve => {server.closeAllConnections(); server.close(resolve);}))); 
  }
});
test('env loader rejects public, duplicate, executable and symlink settings without evaluating them', async () => {
  const folder = await mkdtemp(join(tmpdir(), 'smartcore-gateway-')); await chmod(folder, 0o700);
  const file = join(folder, '.env');
  try {
    await writeFile(file, '# example\nSMARTCORE_BFF_BRIDGE_ENABLED=false\n', {mode: 0o600});
    assert.equal((await readSettings(file)).SMARTCORE_BFF_BRIDGE_ENABLED, 'false');
    await chmod(file, 0o644); await assert.rejects(readSettings(file)); await chmod(file, 0o600);
    for (const text of ['A=one\nA=two\n', 'KEY=$(touch /tmp/forbidden)\n', 'export KEY=value\n']) {
      await writeFile(file, text); await assert.rejects(readSettings(file));
    }
    await symlink(file, join(folder, 'linked')); await assert.rejects(readSettings(join(folder, 'linked')));
    await chmod(folder, 0o755); await assert.rejects(readSettings(file));
  } finally {await rm(folder, {recursive: true, force: true});}
});
