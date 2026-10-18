import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';

// Real production Next routing, no live upstream or credentials. Every request
// must terminate locally before dispatch (the hostname is reserved for tests).
const origin = 'https://identity.example.test';
const child = spawn(process.execPath, ['node_modules/next/dist/bin/next', 'start', '--hostname', '127.0.0.1', '--port', '3183'], {
  stdio: 'ignore', env: {...process.env, SMARTCORE_BFF_BRIDGE_ENABLED: 'true',
    SMARTCORE_BROWSER_ORIGIN: origin, SMARTCORE_BFF_GATEWAY_ORIGIN: 'https://gateway.example.test',
    SMARTCORE_BFF_GATEWAY_KEY: 'K'.repeat(43)}
});
const local = 'http://127.0.0.1:3183';
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (child.exitCode !== null) throw new Error('Next exited');
    try {if ((await fetch(local + '/fa')).ok) {ready = true; break;}} catch {}
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert(ready, 'Next readiness timeout');
  for (const [path, method, body, status, code] of [
    ['/bff/login', 'POST', '{}', 403, 'ORIGIN_REJECTED'],
    ['/bff/csrf', 'POST', '{}', 405, 'METHOD_NOT_ALLOWED'],
    ['/bff/dev/inbox', 'POST', '{}', 404, 'NOT_FOUND'],
    ['/bff/login', 'POST', 'x'.repeat(16385), 413, 'REQUEST_TOO_LARGE'],
    ['/bff', 'GET', undefined, 404, 'NOT_FOUND']
  ]) {
    const reply = await fetch(local + path, {method, headers: {'Content-Type': 'application/json', Origin: origin}, body, redirect: 'manual'});
    assert.equal(reply.status, status, path); assert.equal(reply.headers.get('cache-control'), 'no-store');
    assert.equal((await reply.json()).code, code, path);
  }
  console.log('Enabled bridge production Next routing PASS: JSON errors, size/method/origin guards, no locale redirect or live gateway calls.');
} finally {
  child.kill('SIGTERM');
  if (child.exitCode === null) await new Promise(resolve => child.once('exit', resolve));
}
