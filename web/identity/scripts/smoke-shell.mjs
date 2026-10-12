import assert from 'node:assert/strict';
import {spawn, execFileSync} from 'node:child_process';
import http from 'node:http';
import https from 'node:https';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createIngress} from '../../../deploy/development/https-ingress.mjs';
const servers=[];let scratch;
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
  scratch=await mkdtemp(join(tmpdir(),'smartcore-next-tls-'));
  execFileSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-days','1','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1','-keyout',join(scratch,'tls.key'),'-out',join(scratch,'tls.crt')],{stdio:'ignore'});
  const cert=await readFile(join(scratch,'tls.crt')),key=await readFile(join(scratch,'tls.key'));
  const peers=[];
  for(let index=0;index<2;index++) {
    const peer=http.createServer((request,response)=>{response.writeHead(404,{'Content-Type':'application/json'});response.end(JSON.stringify({code:'NOT_FOUND'}));});
    servers.push(peer);await new Promise(resolve=>peer.listen(0,'127.0.0.1',resolve));peers.push(`http://127.0.0.1:${peer.address().port}`);
  }
  const ingress=createIngress({origin:'https://localhost:7443',nextOrigin:'http://127.0.0.1:3181',bffOrigins:peers,key,cert});
  servers.push(ingress);await new Promise(resolve=>ingress.listen(0,'127.0.0.1',resolve));
  const secure=path=>new Promise((resolve,reject)=>{
    const request=https.request({hostname:'127.0.0.1',servername:'localhost',port:ingress.address().port,path,ca:cert,agent:false,headers:{Host:'localhost:7443',Cookie:`smartcore-locale=fa; __Host-smartcore-session=${'A'.repeat(43)}; future-cookie=blocked`}},response=>{
      assert.equal(response.socket.authorized,true);const chunks=[];response.on('data',chunk=>chunks.push(chunk));response.on('end',()=>resolve({status:response.statusCode,headers:response.headers,text:Buffer.concat(chunks).toString()}));
    });request.on('error',reject);request.end();
  });
  for(const locale of ['fa','en']) {
    const response=await secure('/'+locale);assert.equal(response.status,200);
    assert.equal(response.headers['strict-transport-security'],'max-age=31536000');assert.equal(response.headers['cache-control'],'no-store');
    assert.match(response.text,new RegExp(`<html[^>]*lang="${locale}"[^>]*dir="${locale==='fa'?'rtl':'ltr'}"`));
    const nonce=response.headers['content-security-policy'].match(/'nonce-([^']+)'/)[1];
    for(const tag of response.text.match(/<script\b[^>]*>/g)??[])assert(tag.includes(`nonce="${nonce}"`));
    for(const cookie of response.headers['set-cookie']??[])assert(cookie.startsWith('smartcore-locale='));
  }
  const redirect=await secure('/');assert([307,308].includes(redirect.status));
  assert.equal(new URL(redirect.headers.location,'https://localhost:7443').origin,'https://localhost:7443');
  assert.equal(new URL(redirect.headers.location,'https://localhost:7443').pathname,'/fa');
  assert.equal((await secure('/bff/register')).status,404);
  console.log('Next HTTPS ingress PASS: trusted local TLS, actual production fa/en pages, nonce preservation and BFF-only routing. BFF peers here are controlled; real ASP.NET TLS checks run in the other CI job.');
  console.log('Next production HTTP PASS: fa/en direction, fresh nonce on every script, no inline styles, locale-cookie attributes, misrouted BFF JSON rejection. Not browser hydration or full account E2E evidence.');
}finally {
  await Promise.all(servers.map(server=>new Promise(resolve=>{server.closeAllConnections();server.close(resolve);})));
  if(scratch)await rm(scratch,{recursive:true,force:true});
  child.kill('SIGTERM');await new Promise(resolve=>{if(child.exitCode!==null)resolve();else child.once('exit',resolve);});
}
