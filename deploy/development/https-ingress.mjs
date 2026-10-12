// Development/test ingress only. No refresh, tokens, retries, credentials or request logging.
import https from 'node:https';
import http from 'node:http';
import {readFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';

const authCookies = ['__Host-smartcore-session', '__Host-smartcore-antiforgery'];
const sharedHeaders = ['accept','accept-language','content-type','origin','sec-fetch-site','sec-fetch-mode','sec-fetch-dest','user-agent'];
const hopHeaders = new Set(['connection','keep-alive','proxy-authenticate','proxy-authorization','te','trailer','transfer-encoding','upgrade']);
function backend(value) {
  const url = new URL(value);
  if(url.protocol !== 'http:' || !['127.0.0.1','localhost','[::1]'].includes(url.hostname) || url.pathname !== '/' || url.search || url.hash || url.username || url.password) throw new Error('Private loopback backend root required');
  return url;
}
function cookieRequest(raw, bff) {
  const entries = new Map();
  for(const part of (raw ?? '').split(';')) {
    const index = part.indexOf('=');if(index < 0)continue;
    const name=part.slice(0,index).trim(), value=part.slice(index+1).trim();
    entries.set(name, entries.has(name) ? null : value);
  }
  const names = bff ? authCookies : ['smartcore-locale'];
  return names.flatMap(name => {
    const value=entries.get(name);
    if(!value || (name==='smartcore-locale' ? !['fa','en'].includes(value) : !/^[A-Za-z0-9_-]{1,4096}$/.test(value)))return [];
    if(name==='__Host-smartcore-session' && value.length!==43)return [];
    return [`${name}=${value}`];
  }).join('; ');
}
function cookieResponse(field,bff) {
  const parts=field.split(';').map(value=>value.trim());const index=parts[0].indexOf('=');
  const name=parts[0].slice(0,index), value=parts[0].slice(index+1);
  if(!(bff?authCookies:['smartcore-locale']).includes(name))return null;
  const attributes=new Map(parts.slice(1).map(part=>{const index=part.indexOf('=');return index<0?[part.toLowerCase(),'']:[part.slice(0,index).toLowerCase(),part.slice(index+1)];}));
  if(!attributes.has('secure') || attributes.get('path')!=='/' || attributes.get('samesite')?.toLowerCase()!=='lax' || attributes.has('domain') || bff&&!attributes.has('httponly'))throw new Error('Unsafe upstream cookie');
  if(!bff && value!=='' && !['fa','en'].includes(value))throw new Error('Invalid locale cookie');
  return field;
}
function error(response,status,code) {
  if(response.headersSent){response.destroy();return;}
  const body=status===503?{code,retryAfterSeconds:60}:{code};
  response.removeHeader('content-encoding');
  response.writeHead(status,{'connection':'close','content-length':String(Buffer.byteLength(JSON.stringify(body))),'content-type':'application/json; charset=utf-8','cache-control':'no-store',...(status===503?{'retry-after':'60'}:{})});
  response.end(JSON.stringify(body));
}
export function createIngress({origin,nextOrigin,bffOrigins,key,cert,timeout=45000}) {
  const external=new URL(origin);
  if(external.protocol!=='https:' || !['localhost','127.0.0.1','[::1]'].includes(external.hostname) || external.pathname!=='/' || external.username || external.password || external.search || external.hash)throw new Error('Development HTTPS loopback origin required');
  const next=backend(nextOrigin), peers=bffOrigins.map(backend);if(peers.length!==2)throw new Error('Two private BFF peers required');
  let turn=0;
  const server=https.createServer({key,cert,minVersion:'TLSv1.2'},async (request,response)=>{
    response.setHeader('Strict-Transport-Security','max-age=31536000');
    response.setHeader('X-Content-Type-Options','nosniff');response.setHeader('X-Frame-Options','DENY');
    response.setHeader('Referrer-Policy','no-referrer');response.setHeader('Permissions-Policy','camera=(), microphone=(), geolocation=()');
    if(request.headers.host!==external.host){error(response,400,'VALIDATION_FAILED');return;}
    let path;
    try {
      if(!request.url.startsWith('/') || request.url.startsWith('//'))throw new Error('Invalid target');
      const raw=request.url.split('?')[0];
      if(/\\|%2f|%5c/i.test(raw))throw new Error('Ambiguous path');
      path=decodeURIComponent(raw);
      if(path.split('/').some(segment=>segment==='.' || segment==='..'))throw new Error('Ambiguous path');
    }catch{error(response,400,'VALIDATION_FAILED');return;}
    if(['/auth','/dev','/operator'].some(prefix=>path===prefix || path.startsWith(prefix+'/'))){error(response,404,'NOT_FOUND');return;}
    const bff=path==='/bff' || path.startsWith('/bff/');
    const target=bff?peers[turn++%peers.length]:next;
    const headers={host:target.host,'x-forwarded-host':external.host,'x-forwarded-proto':'https','x-forwarded-for':request.socket.remoteAddress};
    for(const name of [...sharedHeaders,...(bff?['x-csrf-token']:['rsc','next-router-state-tree','next-router-prefetch','next-url'])])if(request.headers[name]!==undefined)headers[name]=request.headers[name];
    const cookie=cookieRequest(request.headers.cookie,bff);if(cookie)headers.cookie=cookie;
    const chunks=[];let used=0;
    try {
      for await(const chunk of request.iterator({destroyOnReturn:false})){used+=chunk.length;if(used>(bff?16384:65536)){error(response,413,'REQUEST_TOO_LARGE');return;}chunks.push(chunk);}
    }catch{response.destroy();return;}
    if(request.headers['content-length']!==undefined || used>0)headers['content-length']=String(used);
    const upstream=http.request(target,{method:request.method,path:request.url,headers,agent:false}, incoming=>{
      try {
        if(incoming.headers.location && new URL(incoming.headers.location,external.origin).origin!==external.origin)throw new Error('Cross-origin upstream redirect');
        const connection=new Set(String(incoming.headers.connection??'').split(',').map(x=>x.trim().toLowerCase()));
        for(const [name,value]of Object.entries(incoming.headers))if(value!==undefined && !hopHeaders.has(name) && !connection.has(name) && !['set-cookie','strict-transport-security'].includes(name))response.setHeader(name,value);
        const cookies=(incoming.headers['set-cookie']??[]).map(value=>cookieResponse(value,bff)).filter(Boolean);
        if(cookies.length)response.setHeader('set-cookie',cookies);
        if(bff)response.setHeader('Cache-Control','no-store');
        response.writeHead(incoming.statusCode??502);incoming.pipe(response);
        incoming.on('error',()=>response.destroy());
      }catch{incoming.destroy();error(response,503,'BFF_UNAVAILABLE');}
    });
    const deadline=setTimeout(()=>upstream.destroy(new Error('Upstream deadline')),timeout);deadline.unref();
    upstream.once('close',()=>clearTimeout(deadline));
    upstream.setTimeout(timeout,()=>upstream.destroy(new Error('Upstream timeout')));
    upstream.on('error',()=>error(response,503,'BFF_UNAVAILABLE'));
    response.on('close',()=>{if(!response.writableFinished)upstream.destroy();});
    // Exactly one dispatch. Never retry or fail over an ambiguous request.
    upstream.end(Buffer.concat(chunks));
  });
  server.requestTimeout=60000;server.headersTimeout=10000;
  return server;
}
if(process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) {
  if(!['Development','Testing'].includes(process.env.SMARTCORE_INGRESS_ENVIRONMENT))throw new Error('Development/test ingress only');
  const origin=process.env.SMARTCORE_INGRESS_ORIGIN ?? 'https://localhost:7443';
  const server=createIngress({origin,nextOrigin:process.env.SMARTCORE_NEXT_ORIGIN??'http://127.0.0.1:3181',bffOrigins:(process.env.SMARTCORE_BFF_ORIGINS??'http://127.0.0.1:5171,http://127.0.0.1:5172').split(','),key:await readFile(process.env.SMARTCORE_TLS_KEY),cert:await readFile(process.env.SMARTCORE_TLS_CERT)});
  server.listen(Number(new URL(origin).port||443),'127.0.0.1');
  for(const signal of ['SIGINT','SIGTERM'])process.once(signal,()=>server.close(()=>process.exit(0)));
}
