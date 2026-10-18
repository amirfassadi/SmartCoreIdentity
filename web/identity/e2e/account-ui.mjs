// Isolated production UI checks with synthetic BFF replies; not backend acceptance.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright';
const cwd=fileURLToPath(new URL('../',import.meta.url));
const server=spawn(process.execPath,['node_modules/next/dist/bin/next','start','--hostname','127.0.0.1','--port','3189'],{cwd,stdio:'ignore'});
let browser;
try {
  let ready=false;
  for(let i=0;i<150;i++) {
    if(server.exitCode!==null)throw Error('UI server exited');
    try {if((await fetch('http://127.0.0.1:3189/en')).ok){ready=true;break;}}catch{}
    await new Promise(resolve=>setTimeout(resolve,100));
  }
  assert(ready,'UI server readiness');
  browser=await chromium.launch({headless:true,...(process.env.SMARTCORE_UI_BROWSER?{executablePath:process.env.SMARTCORE_UI_BROWSER,args:['--no-sandbox','--disable-dev-shm-usage','--disable-gpu']}: {})});
  for(const locale of ['en','fa']) {
    const context=await browser.newContext({baseURL:'http://127.0.0.1:3189',viewport:{width:1280,height:900}});
    const page=await context.newPage();let signedIn=false;const faults=[];
    page.on('pageerror',()=>faults.push('runtime'));
    await page.addInitScript(()=>{window.__violations=[];document.addEventListener('securitypolicyviolation',e=>window.__violations.push(e.effectiveDirective));});
    await page.route('**/bff/**',async route=>{
      const path=new URL(route.request().url()).pathname;
      let status=200,body={};
      const person={displayName:locale==='fa'?'کاربر آزمایشی':'Example account',email:'fixture@example.invalid',mobile:null,recoveryEnrollmentRequired:true};
      if(path==='/bff/csrf')body={requestToken:'synthetic-request-token-for-ui-tests-only-123456789'};
      else if(path==='/bff/login'){signedIn=true;body={person};}
      else if(path==='/bff/logout'){signedIn=false;status=204;}
      else if(path==='/bff/me'||path==='/bff/me/action'){status=signedIn?200:401;body=signedIn?{person}:{code:'REAUTHENTICATION_REQUIRED'};}
      else {status=404;body={code:'NOT_FOUND'};}
      await route.fulfill({status,headers:{'Content-Type':'application/json','Cache-Control':'no-store'},...(status===204?{}:{body:JSON.stringify(body)})});
    });
    await page.goto('/'+locale);
    const contact=page.locator('input[name=contact]');await contact.waitFor();
    // An anonymous /me rejection must end the loading state and show sign-in.
    assert.equal(await contact.isVisible(),true);
    const show=locale==='en'?'Show password':'نمایش رمز عبور';
    await page.getByRole('button',{name:show,exact:true}).click();
    assert.equal(await page.locator('input[name=password]').getAttribute('type'),'text');
    await page.getByRole('button',{name:locale==='en'?'Hide password':'پنهان‌کردن رمز عبور',exact:true}).click();
    assert.equal(await page.locator('input[name=password]').getAttribute('type'),'password');
    await page.getByRole('button',{name:locale==='en'?'Create an account':'ایجاد حساب',exact:true}).click();
    await page.getByTestId('registration').locator('ol li').first().waitFor();
    assert.equal(await page.getByTestId('registration').locator('li[aria-current=step]').count(),1);
    await page.getByRole('button',{name:locale==='en'?'Go to sign in':'رفتن به ورود',exact:true}).click();
    if(process.env.SMARTCORE_UI_SCREENSHOTS)await page.screenshot({path:`${process.env.SMARTCORE_UI_SCREENSHOTS}/login-${locale}.png`,fullPage:true});
    await contact.fill('fixture@example.invalid');await page.locator('input[name=password]').fill('Synthetic UI password only');
    await page.locator('form button[type=submit]').click();await page.getByTestId('profile').waitFor();
    assert.equal(await page.getByTestId('profile').locator('form').count(),0);
    await page.getByRole('button',{name:locale==='en'?'Account security':'امنیت حساب',exact:true}).click();
    await page.getByRole('button',{name:locale==='en'?'Recovery code':'کد بازیابی',exact:true}).click();
    await page.locator('input[name=currentPassword]').waitFor();
    await page.getByRole('button',{name:locale==='en'?'Back to account':'بازگشت به حساب',exact:true}).click();
    assert.equal(await page.getByTestId('recovery').locator('form').count(),0);
    await page.getByRole('button',{name:locale==='en'?'Overview':'نمای کلی',exact:true}).click();
    if(process.env.SMARTCORE_UI_SCREENSHOTS)await page.screenshot({path:`${process.env.SMARTCORE_UI_SCREENSHOTS}/account-${locale}.png`,fullPage:true});
    for(const width of [390,320]) {
      await page.setViewportSize({width,height:844});
      assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`Account overflow at ${locale}/${width}`);
    }
    await page.getByRole('button',{name:locale==='en'?'Sign out':'خروج',exact:true}).click();await contact.waitFor();
    for(const width of [390,320]) {
      await page.setViewportSize({width,height:844});
      assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`Login overflow at ${locale}/${width}`);
    }
    if(process.env.SMARTCORE_UI_SCREENSHOTS)await page.screenshot({path:`${process.env.SMARTCORE_UI_SCREENSHOTS}/mobile-${locale}.png`,fullPage:true});
    assert.equal(await page.evaluate(()=>window.__violations.length),0,'Production CSP');
    assert.equal(faults.length,0,'UI runtime errors');
    await context.close();
  }
  console.log('Production UI PASS: anonymous loading, password visibility, registration progress, explicit security forms, account navigation, logout, fa/en and 320/390px layouts. Synthetic backend only.');
}finally{
  await browser?.close();server.kill('SIGTERM');
  if(server.exitCode===null)await new Promise(resolve=>server.once('exit',resolve));
}
