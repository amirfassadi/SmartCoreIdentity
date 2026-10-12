import type {BrowserPostPath, BrowserRequests, BrowserResponses, CsrfResponse} from './generated';
export type {Person} from './generated';
export class BrowserError extends Error {
  constructor(public code: string, public retryAfterSeconds = 0) {super(code);}
}
let csrf: string | undefined;
export function invalidateCsrf() {csrf = undefined;}
async function response<T>(request: Promise<Response>): Promise<T> {
  let result: Response;
  try {result = await request;} catch {throw new BrowserError('BFF_UNAVAILABLE');}
  const data = result.status === 204 ? undefined : await result.json().catch(() => undefined);
  if (!result.ok) {
    const seconds = Number(result.headers.get('Retry-After'));
    throw new BrowserError(typeof data?.code === 'string' ? data.code : 'BFF_UNAVAILABLE',
      Number.isInteger(seconds) && seconds >= 1 && seconds <= 60 && seconds === data?.retryAfterSeconds ? seconds : 0);
  }
  return data as T;
}
async function csrfLock<T>(action: () => Promise<T>, signal?: AbortSignal): Promise<T> {
  if (typeof navigator !== 'undefined' && navigator.locks) return navigator.locks.request('smartcore-csrf-bootstrap', {signal}, action);
  return action();
}
async function loadCsrf(signal?: AbortSignal) {
  const value = await response<CsrfResponse>(fetch('/bff/csrf', {credentials: 'same-origin', cache: 'no-store', signal}));
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError');
  if (typeof value?.requestToken !== 'string' || value.requestToken.length < 32 || value.requestToken.length > 4096) throw new BrowserError('BFF_UNAVAILABLE');
  csrf = value.requestToken;
}
// Cookie creation is shared across tabs: serialize it with transitions so simultaneous
// first bootstraps cannot overwrite the cookie underlying another tab's request token.
export async function bootstrap(signal?: AbortSignal) {await csrfLock(() => loadCsrf(signal), signal);}
export async function post<P extends BrowserPostPath>(path: P, body: BrowserRequests[P], signal?: AbortSignal): Promise<BrowserResponses[P]> {
  const send = async () => response<BrowserResponses[P]>(fetch(path, {method: 'POST', credentials: 'same-origin', cache: 'no-store',
    headers: {'Content-Type': 'application/json', 'X-CSRF-Token': csrf!}, body: JSON.stringify(body), signal}));
  // Every account response may clear cookies on terminal rejection. Keep its proof
  // bootstrap and response in the same cross-tab lock as login/logout, so an old
  // anonymous/terminal read cannot clear a later login cookie.
  return csrfLock(async () => {
    await loadCsrf(signal);
    const result = await send();
    if (path === '/bff/login' || path === '/bff/logout') {
      invalidateCsrf();
      // Failure here does not change the already committed transition outcome.
      try {await loadCsrf(signal);} catch {invalidateCsrf();}
    }
    return result;
  }, signal);
}
// No retries of credentials or state-changing commands. New user actions honor Retry-After.
