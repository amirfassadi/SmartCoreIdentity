// Real production Next + TLS + private Identity. No traces/screenshots/proof logs.
import assert from 'node:assert/strict';
import {createHash, X509Certificate} from 'node:crypto';
import {readFile} from 'node:fs/promises';
import {createInterface} from 'node:readline';
import {chromium} from 'playwright';
const input=createInterface({input:process.stdin,terminal:false})[Symbol.asyncIterator]();
const receive=async()=>JSON.parse((await input.next()).value);
const fixture=await receive();
const certificate=new X509Certificate(await readFile(fixture.tlsCertificatePath));
const spki=createHash('sha256').update(certificate.publicKey.export({type:'spki',format:'der'})).digest('base64');
const browser=await chromium.launch({headless:true,args:[`--ignore-certificate-errors-spki-list=${spki}`]});
const context=await browser.newContext({baseURL:'https://localhost:7443',ignoreHTTPSErrors:false});
let stage='private registration shell';
const deadline=setTimeout(()=>{process.stdout.write(JSON.stringify({event:'failed',stage:'deadline'})+'\n');process.exit(1);},90000);deadline.unref();
try {
  await context.addInitScript(()=>{
    window.__cspViolations=[];
    document.addEventListener('securitypolicyviolation',e=>window.__cspViolations.push(e.effectiveDirective));
  });
  const page=await context.newPage();const faults=[];page.on('pageerror',()=>faults.push('pageerror'));
  await page.goto('/fa');await page.getByRole('button',{name:'ایجاد حساب',exact:true}).click();
  const panel=page.getByTestId('registration');await panel.waitFor();
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.locator('html').getAttribute('dir'),'rtl');
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await panel.locator('input[name=displayName]').fill('آزمون ثبت‌نام');
  await panel.locator('input[name=contact]').fill(fixture.email);
  await panel.locator('input[name=password]').fill(fixture.password);
  stage='lost start response and explicit retry';
  await page.evaluate(()=>{
    const native=fetch.bind(window);let lost=false;
    window.fetch=async(input,options)=>{
      const response=await native(input,options);
      if(input==='/bff/register' && response.status===202 && !lost){lost=true;throw new Error('Fixture lost response');}
      return response;
    };
  });
  await panel.getByRole('button',{name:'ارسال کد تأیید',exact:true}).click();
  await panel.getByRole('alert').waitFor();
  assert.equal(await panel.locator('input[name=password]').inputValue(),fixture.password);
  await panel.getByRole('button',{name:'ارسال کد تأیید',exact:true}).click();
  await panel.locator('input[name=code]').waitFor();
  assert.equal(await page.locator('input[type=password]').count(),0,'Start success removes password input');
  assert.equal(await panel.getByRole('button',{name:/ارسال مجدد تا/}).isDisabled(),true);
  const cookies=await context.cookies();const proof=cookies.find(c=>c.name==='__Host-smartcore-registration');
  assert(proof && proof.httpOnly && proof.secure && proof.path==='/' && proof.sameSite==='Lax');
  assert(!cookies.some(c=>c.name==='__Host-smartcore-session'));
  assert.equal(await page.evaluate(()=>document.cookie.includes('__Host-smartcore-registration')),false);
  process.stdout.write(JSON.stringify({event:'started'})+'\n');const otp=await receive();
  stage='localized OTP and pending account';
  const localized=otp.code.replace(/[0-9]/g,c=>String.fromCharCode(1776+Number(c)));
  await panel.locator('input[name=code]').fill(localized);
  await panel.getByRole('button',{name:'تأیید کد',exact:true}).click();
  await panel.getByRole('button',{name:'بررسی آماده‌شدن حساب',exact:true}).waitFor();
  assert(!(await context.cookies()).some(c=>c.name==='__Host-smartcore-session'));
  process.stdout.write(JSON.stringify({event:'provision'})+'\n');await receive();
  stage='Ready and separate login';
  await panel.getByRole('button',{name:'بررسی آماده‌شدن حساب',exact:true}).click();
  await panel.getByText('حساب شما آماده است. برای ادامه وارد شوید.',{exact:true}).waitFor();
  assert(!(await context.cookies()).some(c=>c.name==='__Host-smartcore-session'));
  await panel.getByRole('button',{name:'رفتن به ورود',exact:true}).click();
  await page.getByRole('link',{name:'English',exact:true}).click();
  await page.locator('input[name=contact]').waitFor();
  assert.equal(await page.locator('html').getAttribute('dir'),'ltr');
  await page.locator('input[name=contact]').fill(fixture.email);await page.locator('input[name=password]').fill(fixture.password);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.getByTestId('profile').waitFor();
  stage='proof isolation and CSP';
  const stored=await page.evaluate(()=>JSON.stringify({...localStorage,...sessionStorage}));
  for(const value of [fixture.email,fixture.password,otp.code])assert(!stored.includes(value),'No private registration material in browser storage');
  assert.equal(await page.evaluate(()=>window.__cspViolations.length),0);
  assert.equal(faults.length,0);
  process.stdout.write(JSON.stringify({event:'passed'})+'\n');
} catch {
  process.stdout.write(JSON.stringify({event:'failed',stage})+'\n');process.exitCode=1;
} finally {clearTimeout(deadline);await context.close();await browser.close();process.stdin.destroy();}
