// Real Chromium acceptance. No traces, screenshots, credential-bearing network logs or profile dumps.
import assert from 'node:assert/strict';
import {createHash, X509Certificate} from 'node:crypto';
import {readFile} from 'node:fs/promises';
import {chromium} from 'playwright';
const chunks=[];for await (const chunk of process.stdin) chunks.push(chunk);
const fixture=JSON.parse(Buffer.concat(chunks).toString());
const certificate=new X509Certificate(await readFile(fixture.tlsCertificatePath));
const spki=createHash('sha256').update(certificate.publicKey.export({type:'spki',format:'der'})).digest('base64');
// Trust only this disposable TLS key, never a blanket ignoreHTTPSerrors switch.
const browser=await chromium.launch({headless:true,args:[`--ignore-certificate-errors-spki-list=${spki}`]});
const context=await browser.newContext({baseURL:'https://localhost:7443',ignoreHTTPSErrors:false});
let release;
try {
  await context.addInitScript(() => {
    window.__cspViolations=[];
    document.addEventListener('securitypolicyviolation', event => window.__cspViolations.push(event.effectiveDirective));
  });
  const page=await context.newPage();const faults=[];
  page.on('pageerror',()=>faults.push('pageerror'));
  await page.goto('/en');
  await page.locator('input[name=contact]').waitFor();
  assert.equal(await page.locator('html').getAttribute('dir'),'ltr');
  assert.equal(await page.locator('input[name=password]').getAttribute('autocomplete'),'current-password');
  const form=page.locator('form');
  await form.locator('[name=contact]').fill(fixture.email);
  await form.locator('[name=password]').fill(fixture.password);
  await form.getByRole('button').click();
  await page.getByTestId('profile').waitFor();
  const cookies=await context.cookies();
  for (const name of ['__Host-smartcore-session']) {
    const cookie=cookies.find(value=>value.name===name);
    assert(cookie && cookie.secure && cookie.httpOnly && cookie.sameSite==='Lax' && cookie.path==='/','Session cookie attributes');
  }
  const publicPage=await context.request.get('/en');
  const html=await publicPage.text();assert(!html.includes(fixture.email),'Public SSR must not contain private profile');
  const firstNonce=publicPage.headers()['content-security-policy'].match(/nonce-([^']+)/)[1];
  const secondPage=await context.request.get('/en');
  assert.notEqual(firstNonce,secondPage.headers()['content-security-policy'].match(/nonce-([^']+)/)[1]);
  assert.equal(await page.evaluate(()=>JSON.stringify({...localStorage,...sessionStorage}).includes('requestToken')),false);
  assert.equal(await page.evaluate(()=>Object.keys(localStorage).some(key=>/token|password|profile/i.test(key))),false);
  const peer=await context.newPage();peer.on('pageerror',()=>faults.push('pageerror'));
  await peer.goto('/fa');await peer.getByTestId('profile').waitFor();
  assert.equal(await peer.locator('html').getAttribute('dir'),'rtl');
  await peer.setViewportSize({width:390,height:844});
  assert(await peer.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'Mobile RTL must not overflow');
  // Real /me read is held after response arrives; logout must discard it on the second tab.
  const gate=new Promise(resolve=>{release=resolve;});let mark;
  const held=new Promise(resolve=>{mark=resolve;});
  await peer.route('**/bff/me/action', async route => {
    const result=await route.fetch();mark();await gate;
    try {await route.fulfill({response:result});}catch {/* The UI correctly aborts obsolete reads. */}
  });
  await peer.getByRole('button',{name:'ادامه',exact:true}).click();await held;
  await page.getByRole('button',{name:'Sign out',exact:true}).click();
  await page.locator('form').waitFor();await peer.locator('form').waitFor();release();
  await peer.waitForTimeout(300);
  assert.equal(await peer.getByTestId('profile').count(),0,'Delayed read must not resurrect logged-out profile');
  assert.equal(await page.getByTestId('profile').count(),0);
  // Same owner logs in again; peer must reread authoritative session on the event.
  await page.locator('[name=contact]').fill(fixture.email);
  await page.locator('[name=password]').fill(fixture.password);
  await page.locator('form').getByRole('button').click();
  await page.getByTestId('profile').waitFor();await peer.getByTestId('profile').waitFor();
  await page.getByRole('link',{name:'فارسی',exact:true}).click();await page.getByTestId('profile').waitFor();
  assert.equal(await page.locator('html').getAttribute('lang'),'fa');
  await page.goto('/');assert.equal(new URL(page.url()).pathname,'/fa');
  await page.getByTestId('profile').waitFor();
  assert.equal(await page.evaluate(()=>window.__cspViolations.length),0,'Production hydration CSP');
  assert.equal(await peer.evaluate(()=>window.__cspViolations.length),0,'RTL production hydration CSP');
  assert.equal(faults.length,0,'Production client runtime');
  await page.getByRole('button',{name:'خروج',exact:true}).click();await page.locator('form').waitFor();await peer.locator('form').waitFor();
  // Storage-event fallback is tested in an independent browser context without BroadcastChannel.
  const fallback=await browser.newContext({baseURL:'https://localhost:7443',ignoreHTTPSErrors:false});
  try {
    await fallback.addInitScript(()=>{Object.defineProperty(window,'BroadcastChannel',{value:undefined});});
    const one=await fallback.newPage();const two=await fallback.newPage();await one.goto('/en');
    await one.locator('[name=contact]').fill(fixture.email);await one.locator('[name=password]').fill(fixture.password);
    await one.locator('form').getByRole('button').click();await one.getByTestId('profile').waitFor();
    await two.goto('/en');await two.getByTestId('profile').waitFor();
    await one.getByRole('button',{name:'Sign out',exact:true}).click();await one.locator('form').waitFor();await two.locator('form').waitFor();
    assert.equal(await two.getByTestId('profile').count(),0,'Storage fallback clears peer');
    const stored=await one.evaluate(()=>JSON.parse(localStorage.getItem('smartcore-session-event')));
    assert.deepEqual(Object.keys(stored).sort(),['stamp','type']);assert.equal(stored.type,'logout');
  } finally {await fallback.close();}
  console.log('B1 CHROMIUM PASS: real private login; HttpOnly cookie; public SSR isolation; production nonce CSP hydration; fa/en, RTL mobile and locale persistence; BroadcastChannel + storage fallback logout; delayed read discarded; second login rereads session. No B2/B3 claim.');
} catch {
  console.error('B1 CHROMIUM FAIL: a browser assertion did not pass. Credential-bearing artifacts suppressed.');
  process.exitCode=1;
} finally {release?.();await context.close();await browser.close();}
