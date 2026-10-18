import test from 'node:test';
import assert from 'node:assert/strict';
import {createBridge} from '../src/lib/bff/bridge.mjs';

const browser = 'https://identity.example.test';
const gateway = 'https://gateway.example.test';
const key = 'K'.repeat(43);
const env = {SMARTCORE_BFF_BRIDGE_ENABLED: 'true', SMARTCORE_BROWSER_ORIGIN: browser,
  SMARTCORE_BFF_GATEWAY_ORIGIN: gateway, SMARTCORE_BFF_GATEWAY_KEY: key};
const cookie = (name, value = 'test_value') => `${name}=${value}; Path=/; Secure; HttpOnly; SameSite=Lax`;
function request(path = '/bff/login', headers = {}, body = '{}', method = 'POST') {
  return new Request(browser + path, {method, headers: {'Content-Type': 'application/json', Origin: browser,
    'Sec-Fetch-Site': 'same-origin', ...headers}, ...(method === 'GET' ? {} : {body})});
}
async function outcome(response, status, code) {
  assert.equal(response.status, status); assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.equal((await response.json()).code, code);
}
test('disabled and incomplete configuration fail closed without dispatch', async () => {
  const send = () => assert.fail('Must not dispatch');
  await outcome(await createBridge({}, send)(request()), 404, 'NOT_FOUND');
  for (const invalid of [undefined, 'http://gateway.example.test', 'https://user:pass@gateway.example.test',
    'https://gateway.example.test/path', browser]) {
    await outcome(await createBridge({...env, SMARTCORE_BFF_GATEWAY_ORIGIN: invalid}, send)(request()), 503, 'BFF_UNAVAILABLE');
  }
  await outcome(await createBridge({...env, SMARTCORE_BFF_GATEWAY_KEY: 'short'}, send)(request()), 503, 'BFF_UNAVAILABLE');
});
test('only exact account routes/methods; no operator, queries, encoded slashes or arbitrary proxy', async () => {
  const bridge = createBridge(env, () => assert.fail('Must not dispatch'));
  for (const path of ['/bff', '/bff/', '/bff/login/', '/bff/dev/inbox', '/bff/operator', '/bff/login?destination=https://evil.test', '/bff/%6cogin', '/bff/login%2f'])
    await outcome(await bridge(request(path)), 404, 'NOT_FOUND');
  const reply = await bridge(request('/bff/login', {}, undefined, 'GET'));
  assert.equal(reply.headers.get('allow'), 'POST'); await outcome(reply, 405, 'METHOD_NOT_ALLOWED');
});
test('body, media type and same-origin guards precede dispatch', async () => {
  const bridge = createBridge(env, () => assert.fail('Must not dispatch'));
  await outcome(await bridge(request('/bff/login', {Origin: 'https://evil.test', 'Content-Type': 'text/plain'}, 'x'.repeat(16385))), 413, 'REQUEST_TOO_LARGE');
  await outcome(await bridge(request('/bff/login', {Origin: 'https://evil.test', 'Content-Type': 'text/plain'})), 415, 'UNSUPPORTED_MEDIA_TYPE');
  for (const headers of [{Origin: 'https://evil.test'}, {'Sec-Fetch-Site': 'cross-site'}, {Origin: browser + ', https://evil.test'}])
    await outcome(await bridge(request('/bff/login', headers)), 403, 'ORIGIN_REJECTED');
  const alias = new Request('https://other-alias.example.test/bff/login', {method: 'POST', headers: {'Content-Type': 'application/json', Origin: browser}, body: '{}'});
  await outcome(await bridge(alias), 403, 'ORIGIN_REJECTED');
});
test('single dispatch strips browser credentials and forged trust headers, preserves body and CSRF', async () => {
  let calls = 0;
  const bridge = createBridge(env, async (url, options) => {
    calls++; assert.equal(url.href, gateway + '/bff/login');
    assert.equal(options.redirect, 'manual'); assert.equal(options.cache, 'no-store');
    assert.equal(options.headers.get('x-smartcore-gateway-key'), key);
    for (const name of ['authorization', 'x-forwarded-for', 'x-forwarded-host', 'x-forwarded-proto', 'x-client-key', 'x-subject', 'user-agent'])
      assert.equal(options.headers.get(name), null);
    assert.equal(options.headers.get('x-csrf-token'), 'csrf-value'); assert.equal(options.headers.get('origin'), browser);
    assert.equal(options.headers.get('cookie'), '__Host-smartcore-session=' + 'A'.repeat(43));
    assert.equal(new TextDecoder().decode(options.body), '{"email":"person@example.test","password":"synthetic"}');
    return Response.json({status: 'Accepted'}, {headers: {'Cache-Control': 'public, max-age=3600',
      'Access-Control-Allow-Origin': '*', 'X-Diagnostic-Key': 'must-not-leak'}});
  });
  const reply = await bridge(request('/bff/login', {'X-CSRF-Token': 'csrf-value', Authorization: 'Bearer forged',
    'X-Forwarded-Host': 'evil.test', 'X-Forwarded-Proto': 'https', 'X-Forwarded-For': '1.2.3.4',
    'X-Client-Key': 'forged', 'X-Subject': 'forged', 'X-SmartCore-Gateway-Key': 'forged',
    Cookie: `__Host-smartcore-session=${'A'.repeat(43)}; smartcore-locale=fa; future-cookie=secret`},
    '{"email":"person@example.test","password":"synthetic"}'));
  assert.equal(calls, 1); assert.equal(reply.status, 200); assert.equal(reply.headers.get('cache-control'), 'no-store');
  assert.equal(reply.headers.get('access-control-allow-origin'), null); assert.equal(reply.headers.get('x-diagnostic-key'), null);
});
test('duplicate request cookies are discarded; separate Set-Cookie fields and logout expiration preserved', async () => {
  const bridge = createBridge(env, async (_url, options) => {
    assert.equal(options.headers.get('cookie'), '__Host-smartcore-antiforgery=valid');
    const headers = new Headers();
    headers.append('Set-Cookie', cookie('__Host-smartcore-session', 'A'.repeat(43)));
    headers.append('Set-Cookie', cookie('__Host-smartcore-antiforgery', '') + '; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT');
    headers.append('Set-Cookie', cookie('unexpected-cookie'));
    return new Response(null, {status: 204, headers});
  });
  const reply = await bridge(request('/bff/logout', {Cookie: `__Host-smartcore-session=${'A'.repeat(43)}; __Host-smartcore-session=${'B'.repeat(43)}; __Host-smartcore-antiforgery=valid`}));
  assert.equal(reply.status, 204); assert.equal(reply.headers.getSetCookie().length, 2);
  assert.match(reply.headers.getSetCookie()[1], /Max-Age=0/); assert.equal(await reply.text(), '');
});
test('unsafe upstream cookies, redirects and non-JSON replies fail closed and are never retried', async () => {
  for (const result of [new Response(null, {status: 307, headers: {Location: 'https://evil.test'}}),
    new Response('<html>bad gateway</html>', {headers: {'Content-Type': 'text/html'}}),
    Response.json({}, {headers: {'Set-Cookie': '__Host-smartcore-session=bad; Path=/; HttpOnly; SameSite=Lax'}}),
    Response.json({}, {headers: {'Set-Cookie': cookie('__Host-smartcore-session') + '; Domain=example.test'}}),
    Response.json({}, {headers: {'Set-Cookie': cookie('__Host-smartcore-session') + '; Path=/other'}})]) {
    let calls = 0;
    await outcome(await createBridge(env, async () => {calls++; return result;})(request()), 503, 'BFF_UNAVAILABLE');
    assert.equal(calls, 1);
  }
});
test('ambiguous transport failure never retries or returns upstream secrets', async () => {
  let calls = 0;
  const reply = await createBridge(env, async () => {calls++; throw new Error('private connection details');})(request());
  assert.equal(calls, 1); assert.equal(reply.status, 503); assert.equal(reply.headers.get('retry-after'), '60');
  assert.deepEqual(await reply.json(), {code: 'BFF_UNAVAILABLE', retryAfterSeconds: 60});
});
test('response body is bounded and timeout covers slow response streams', async () => {
  await outcome(await createBridge(env, async () => Response.json({data: 'x'.repeat(1048576)}))(request()), 503, 'BFF_UNAVAILABLE');
  let cancelled = false;
  const reply = await createBridge(env, async () => new Response(new ReadableStream({cancel() {cancelled = true;}}),
    {headers: {'Content-Type': 'application/json'}}), 30)(request());
  await outcome(reply, 503, 'BFF_UNAVAILABLE'); assert(cancelled);
});
test('CSRF bootstrap can be anonymous, reset Retry-After is preserved without clamping', async () => {
  const csrf = await createBridge(env, async () => Response.json({requestToken: 'synthetic'}))(
    new Request(browser + '/bff/csrf'));
  assert.equal(csrf.status, 200);
  const reply = await createBridge(env, async () => Response.json({code: 'COOLDOWN', retryAfterSeconds: 21600},
    {status: 429, headers: {'Retry-After': '21600'}}))(request('/bff/password/reset'));
  assert.equal(reply.status, 429); assert.equal(reply.headers.get('retry-after'), '21600');
});
