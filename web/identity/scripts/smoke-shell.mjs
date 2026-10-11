import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
const child = spawn(process.execPath, ['node_modules/next/dist/bin/next', 'start', '--hostname', '127.0.0.1', '--port', '3181'], {stdio:'ignore'});
try {
  let ready = false;
  for(let i=0;i<100;i++) {
    if(child.exitCode !== null) throw new Error('Next process exited');
    try {if((await fetch('http://127.0.0.1:3181/fa')).ok){ready=true;break;}}catch{}
    await new Promise(resolve=>setTimeout(resolve,100));
  }
  assert(ready,'Next readiness timeout');
  const nonces=[];
  for(const locale of ['fa','en','fa']) {
    const response=await fetch(`http://127.0.0.1:3181/${locale}`,{headers:{cookie:'smartcore-locale=invalid','x-nonce':'attacker-controlled'}});
    assert.equal(response.status,200);assert.equal(response.headers.get('cache-control'),'no-store');
    const html=await response.text();
    assert.match(html,new RegExp(`<html[^>]*lang="${locale}"[^>]*dir="${locale==='fa'?'rtl':'ltr'}"`));
    const csp=response.headers.get('content-security-policy');
    assert(csp.includes("style-src-attr 'none'"));assert(!csp.includes('unsafe-inline')&&!csp.includes('unsafe-eval'));
    const nonce=csp.match(/'nonce-([^']+)'/)[1];assert.notEqual(nonce,'attacker-controlled');nonces.push(nonce);
    const scripts=html.match(/<script\b[^>]*>/g)??[];assert(scripts.length>0);
    for(const tag of scripts)assert(tag.includes(`nonce="${nonce}"`),'Every Next script must share this response nonce');
    assert(!/\sstyle="/.test(html),'Inline style attributes forbidden');
    assert.equal(response.headers.get('x-frame-options'),'DENY');assert.equal(response.headers.get('referrer-policy'),'no-referrer');
    for(const cookie of response.headers.getSetCookie()) {
      assert(cookie.startsWith('smartcore-locale='));assert(/Secure/i.test(cookie)&&/SameSite=lax/i.test(cookie)&&/Path=\//.test(cookie));assert(!/Domain=/i.test(cookie));
    }
  }
  assert.equal(new Set(nonces).size,nonces.length);
  for(const path of ['/bff/register','/bff/password/reset/start']) {
    const response=await fetch(`http://127.0.0.1:3181${path}`,{method:'POST'});
    assert.equal(response.status,404);assert.equal(response.headers.get('cache-control'),'no-store');assert.deepEqual(await response.json(),{code:'NOT_FOUND'});
  }
  console.log('Next production HTTP PASS: fa/en direction, fresh nonce on every script, no inline styles, locale-cookie attributes, misrouted BFF JSON rejection. Not browser hydration or real ingress evidence.');
}finally {
  child.kill('SIGTERM');await new Promise(resolve=>{if(child.exitCode!==null)resolve();else child.once('exit',resolve);});
}
