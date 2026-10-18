// Server-only transport. No Identity credentials, retries, redirects or request logging.
const routes = new Map([
  ['/bff/csrf', 'GET'], ['/bff/login', 'POST'], ['/bff/me', 'POST'],
  ['/bff/me/action', 'POST'], ['/bff/logout', 'POST'],
  ['/bff/register', 'POST'], ['/bff/register/verify', 'POST'],
  ['/bff/register/resend', 'POST'], ['/bff/register/setup', 'POST'], ['/bff/register/complete', 'POST'],
  ['/bff/recovery/enroll', 'POST'], ['/bff/password/change', 'POST'],
  ['/bff/password/reset', 'POST'], ['/bff/password/reset/complete', 'POST']
]);
const cookies = new Set(['__Host-smartcore-session', '__Host-smartcore-antiforgery',
  '__Host-smartcore-registration', '__Host-smartcore-reset']);
const safeHeaders = {'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY', 'Referrer-Policy': 'no-referrer'};
function error(status, code, extra = {}) {
  return Response.json(status === 503 ? {code, retryAfterSeconds: 60} : {code},
    {status, headers: {...safeHeaders, ...(status === 503 ? {'Retry-After': '60'} : {}), ...extra}});
}
function root(value) {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.pathname !== '/' || url.search || url.hash || url.username || url.password)
    throw new Error('Fixed HTTPS root required');
  return url;
}
function requestCookies(raw) {
  const values = new Map();
  for (const part of (raw ?? '').split(';')) {
    const i = part.indexOf('='); if (i < 0) continue;
    const name = part.slice(0, i).trim(), value = part.slice(i + 1).trim();
    values.set(name, values.has(name) ? null : value);
  }
  return [...cookies].flatMap(name => {
    const value = values.get(name);
    return value && /^[A-Za-z0-9_-]{1,4096}$/.test(value)
      && (name !== '__Host-smartcore-session' || value.length === 43) ? [`${name}=${value}`] : [];
  }).join('; ');
}
function responseCookie(field) {
  const [pair, ...parts] = field.split(';').map(x => x.trim());
  const name = pair.slice(0, pair.indexOf('='));
  if (!cookies.has(name)) return null;
  const attributes = new Map();
  for (const part of parts) {
    const i = part.indexOf('='), key = (i < 0 ? part : part.slice(0, i)).toLowerCase();
    if (attributes.has(key)) throw new Error('Duplicate cookie attribute');
    attributes.set(key, i < 0 ? '' : part.slice(i + 1));
  }
  if (!attributes.has('secure') || !attributes.has('httponly') || attributes.has('domain')
    || attributes.get('path') !== '/' || attributes.get('samesite')?.toLowerCase() !== 'lax')
    throw new Error('Unsafe gateway cookie');
  return field;
}
async function bounded(stream, limit, signal) {
  if (!stream) return new Uint8Array();
  const reader = stream.getReader(); const chunks = []; let used = 0;
  // Abort while awaiting a slow response body as well as response headers.
  const abort = () => { void reader.cancel().catch(() => {}); };
  signal?.addEventListener('abort', abort, {once: true});
  try {
    if (signal?.aborted) throw new Error('Aborted');
    while (true) {
      const {done, value} = await reader.read();
      if (signal?.aborted) throw new Error('Aborted');
      if (done) break;
      used += value.byteLength;
      if (used > limit) { void reader.cancel().catch(() => {}); throw new RangeError('Body limit'); }
      chunks.push(value);
    }
    const body = new Uint8Array(used); let offset = 0;
    for (const chunk of chunks) {body.set(chunk, offset); offset += chunk.byteLength;}
    return body;
  } finally {signal?.removeEventListener('abort', abort); reader.releaseLock();}
}
// Dependency injection is for transport tests; destinations always come from server configuration.
export function createBridge(env, send = fetch, timeoutMs = 45000) {
  return async function bridge(request) {
    if (env.SMARTCORE_BFF_BRIDGE_ENABLED !== 'true') return error(404, 'NOT_FOUND');
    let external, gateway;
    try {
      external = root(env.SMARTCORE_BROWSER_ORIGIN);
      gateway = root(env.SMARTCORE_BFF_GATEWAY_ORIGIN);
      if (external.origin === gateway.origin || !/^[A-Za-z0-9_-]{43,128}$/.test(env.SMARTCORE_BFF_GATEWAY_KEY ?? ''))
        throw new Error('Separate authenticated gateway required');
    } catch {return error(503, 'BFF_UNAVAILABLE');}
    const url = new URL(request.url), method = routes.get(url.pathname);
    if (!method || url.search) return error(404, 'NOT_FOUND');
    if (request.method !== method) return error(405, 'METHOD_NOT_ALLOWED', {Allow: method});
    const signal = AbortSignal.any([request.signal, AbortSignal.timeout(timeoutMs)]);
    let body;
    try {body = await bounded(request.body, 16384, signal);}
    catch (failure) {return failure instanceof RangeError ? error(413, 'REQUEST_TOO_LARGE') : error(503, 'BFF_UNAVAILABLE');}
    const contentType = request.headers.get('content-type') ?? '';
    if (method === 'POST' && !/^application\/json(?:\s*;\s*charset\s*=\s*(?:utf-8|"utf-8"))?\s*$/i.test(contentType))
      return error(415, 'UNSUPPORTED_MEDIA_TYPE');
    const origin = request.headers.get('origin'), site = request.headers.get('sec-fetch-site');
    if (url.origin !== external.origin || (method === 'POST' ? origin !== external.origin : origin !== null && origin !== external.origin)
      || site !== null && site !== 'same-origin' && site !== 'none') return error(403, 'ORIGIN_REJECTED');
    const headers = new Headers({'Accept': 'application/json', 'X-SmartCore-Gateway-Key': env.SMARTCORE_BFF_GATEWAY_KEY});
    for (const name of ['content-type', 'origin', 'sec-fetch-site', 'sec-fetch-mode', 'sec-fetch-dest', 'x-csrf-token', 'accept-language']) {
      const value = request.headers.get(name); if (value !== null) headers.set(name, value);
    }
    const cookie = requestCookies(request.headers.get('cookie')); if (cookie) headers.set('cookie', cookie);
    let result;
    try {
      result = await send(new URL(url.pathname, gateway), {method, headers,
        ...(method === 'POST' ? {body} : {}), redirect: 'manual', cache: 'no-store', signal});
      if (result.status >= 300 && result.status < 400 || result.status < 200 || result.status > 599
        || result.status !== 204 && !/^application\/json(?:;|$)/i.test(result.headers.get('content-type') ?? '')) {
        await result.body?.cancel(); return error(503, 'BFF_UNAVAILABLE');
      }
      const outgoing = new Headers(safeHeaders);
      for (const name of ['content-type', 'retry-after', 'allow']) {
        const value = result.headers.get(name); if (value !== null) outgoing.set(name, value);
      }
      for (const field of result.headers.getSetCookie()) {
        const cookie = responseCookie(field); if (cookie) outgoing.append('set-cookie', cookie);
      }
      const reply = await bounded(result.body, 1048576, signal);
      return new Response(result.status === 204 ? null : reply, {status: result.status, headers: outgoing});
    } catch {
      if (result?.body) void result.body.cancel().catch(() => {});
      return error(503, 'BFF_UNAVAILABLE');
    }
  };
}
