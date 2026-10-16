import assert from 'node:assert/strict';
import http from 'node:http';
import https from 'node:https';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {execFileSync} from 'node:child_process';
import {createIngress} from '../../deploy/development/https-ingress.mjs';
const scratch=await mkdtemp(join(tmpdir(),'smartcore-ingress-'));
const servers=[];const seen=[];
async function listen(server){servers.push(server);await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));return `http://127.0.0.1:${server.address().port}`;}
function echo(peer){return http.createServer((request,response)=>{
  seen.push({peer,url:request.url,headers:request.headers});
  if(request.url==='/bff/fault'){request.socket.destroy();return;}
  response.setHeader('Content-Type','application/json');
  if(request.url==='/bff/cookie')response.setHeader('Set-Cookie',['__Host-smartcore-session='+ 'A'.repeat(43)+'; Secure; HttpOnly; SameSite=Lax; Path=/','__Host-smartcore-antiforgery='+ 'B'.repeat(64)+'; Secure; HttpOnly; SameSite=Lax; Path=/', '__Host-smartcore-registration='+ 'C'.repeat(256)+'; Secure; HttpOnly; SameSite=Lax; Path=/; Max-Age=600', '__Host-smartcore-reset='+ 'D'.repeat(256)+'; Secure; HttpOnly; SameSite=Lax; Path=/; Max-Age=600']);
  if(request.url==='/redirect-unsafe')response.setHeader('Location','https://evil.invalid/');
  if(peer==='next')response.setHeader('Set-Cookie',['smartcore-locale=en; Secure; SameSite=Lax; Path=/; Max-Age=15552000','__Host-smartcore-session='+ 'X'.repeat(43)+'; Secure; HttpOnly; SameSite=Lax; Path=/','__Host-smartcore-registration='+ 'C'.repeat(256)+'; Secure; HttpOnly; SameSite=Lax; Path=/', '__Host-smartcore-reset='+ 'D'.repeat(256)+'; Secure; HttpOnly; SameSite=Lax; Path=/', 'future-cookie=blocked; Path=/']);
  const unknown=request.url==='/bff/register'||request.url.startsWith('/bff/password/reset');
  response.statusCode=unknown?404:200;response.end(JSON.stringify(unknown?{code:'NOT_FOUND'}:{peer}));
});}
try {
  execFileSync('openssl',['req','-x509','-newkey','rsa:2048','-nodes','-days','1','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1','-keyout',join(scratch,'tls.key'),'-out',join(scratch,'tls.crt')],{stdio:'ignore'});
  const cert=await readFile(join(scratch,'tls.crt')),key=await readFile(join(scratch,'tls.key'));
  const nextOrigin=await listen(echo('next'));
  const bffOrigins=[await listen(echo('bff-a')),await listen(echo('bff-b'))];
  const proxy=createIngress({origin:'https://localhost:7443',nextOrigin,bffOrigins,cert,key});await listen(proxy);
  async function call(path,headers={},body='',method='GET') {
    return await new Promise((resolve,reject)=>{
      const request=https.request({hostname:'127.0.0.1',port:proxy.address().port,servername:'localhost',ca:cert,path,method,headers:{Host:'localhost:7443',...headers},agent:false},response=>{
        assert.equal(response.socket.authorized,true);const chunks=[];response.on('data',chunk=>chunks.push(chunk));response.on('end',()=>resolve({status:response.statusCode,headers:response.headers,body:JSON.parse(Buffer.concat(chunks).toString())}));
      });request.on('error',reject);request.end(body);
    });
  }
  const cookies=`__Host-smartcore-session=${'A'.repeat(43)}; __Host-smartcore-antiforgery=${'B'.repeat(64)}; __Host-smartcore-registration=${'C'.repeat(256)}; __Host-smartcore-reset=${'D'.repeat(256)}; smartcore-locale=fa; future-cookie=blocked`;
  const response=await call('/fa',{Cookie:cookies,'X-Bff-Client-Key':'forged','X-Bff-Subject':'forged','X-Dev-Inbox-Key':'forged','X-Forwarded-For':'203.0.113.1','X-Forwarded-Host':'evil.invalid','X-Forwarded-Proto':'http','X-Nonce':'forged',Authorization:'Bearer forbidden'});
  assert.equal(response.body.peer,'next');assert.equal(seen.at(-1).headers.cookie,'smartcore-locale=fa');
  for(const name of ['x-bff-client-key','x-bff-subject','x-dev-inbox-key','authorization','x-nonce'])assert.equal(seen.at(-1).headers[name],undefined);
  assert.equal(seen.at(-1).headers['x-forwarded-host'],'localhost:7443');assert.equal(seen.at(-1).headers['x-forwarded-proto'],'https');assert.equal(seen.at(-1).headers['x-forwarded-for'],'127.0.0.1');
  assert.deepEqual(response.headers['set-cookie'],['smartcore-locale=en; Secure; SameSite=Lax; Path=/; Max-Age=15552000']);
  assert.equal(response.headers['strict-transport-security'],'max-age=31536000');
  for(const locale of ['smartcore-locale=invalid','smartcore-locale=fa; smartcore-locale=en']){await call('/fa',{Cookie:locale});assert.equal(seen.at(-1).headers.cookie,undefined);}
  const bff=await call('/bff/cookie',{Cookie:cookies,Origin:'https://localhost:7443','Sec-Fetch-Site':'same-origin','X-CSRF-Token':'proof'});
  assert.equal(seen.at(-1).headers.cookie,`__Host-smartcore-session=${'A'.repeat(43)}; __Host-smartcore-antiforgery=${'B'.repeat(64)}; __Host-smartcore-registration=${'C'.repeat(256)}; __Host-smartcore-reset=${'D'.repeat(256)}`);
  assert.equal(seen.at(-1).headers['x-csrf-token'],'proof');assert.equal(bff.headers['set-cookie'].length,4);assert.equal(bff.headers['cache-control'],'no-store');
  for(const name of ['__Host-smartcore-registration','__Host-smartcore-reset']) {await call('/bff/cookie',{Cookie:`${name}=${'C'.repeat(256)}; ${name}=${'D'.repeat(256)}`});assert.equal(seen.at(-1).headers.cookie,undefined,'Duplicate proof cookie is not forwarded');}
  for(const path of ['/bff','/bff/register','/bff/password/reset/start','/bff/password/reset/complete']) {
    const r=await call(path);assert(seen.at(-1).peer.startsWith('bff-'));if(path!=='/bff'){assert.equal(r.status,404);assert.deepEqual(r.body,{code:'NOT_FOUND'});assert.equal(r.headers['cache-control'],'no-store');}
  }
  assert.equal((await call('/redirect-unsafe')).status,503);
  const before=seen.length;const failed=await call('/bff/fault');assert.equal(failed.status,503);assert.equal(failed.body.code,'BFF_UNAVAILABLE');assert.equal(seen.length,before+1,'No replay/failover after socket failure');
  const noCalls=seen.length;
  assert.equal((await call('/bff/login',{'Content-Type':'text/plain'},'x'.repeat(16385),'POST')).status,413);
  for(const path of ['/auth/login','/dev/inbox/x','/operator/status'])assert.equal((await call(path)).status,404);
  assert.equal((await call('/bff/register',{Host:'evil.invalid'})).status,400);
  assert.equal(seen.length,noCalls);
  assert(seen.some(x=>x.peer==='bff-a')&&seen.some(x=>x.peer==='bff-b'));
  console.log('HTTPS ingress PASS: verified TLS, cookie/header allowlists, separate Set-Cookie, BFF-only routing incl B2/B3, body limit before dispatch, no retry/failover. Backends in this test are controlled HTTP peers, not ASP.NET/Next.');
}finally {
  await Promise.all(servers.map(server=>new Promise(resolve=>{server.closeAllConnections();server.close(resolve);})));await rm(scratch,{recursive:true,force:true});
}
