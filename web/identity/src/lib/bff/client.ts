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
export async function bootstrap(signal?: AbortSignal) {
  const value = await response<CsrfResponse>(fetch('/bff/csrf', {credentials: 'same-origin', cache: 'no-store', signal}));
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError');
  if (typeof value?.requestToken !== 'string' || value.requestToken.length < 32 || value.requestToken.length > 4096) throw new BrowserError('BFF_UNAVAILABLE');
  csrf = value.requestToken;
}
export async function post<P extends BrowserPostPath>(path: P, body: BrowserRequests[P], signal?: AbortSignal) {
  if (!csrf) await bootstrap(signal);
  return response<BrowserResponses[P]>(fetch(path, {method: 'POST', credentials: 'same-origin', cache: 'no-store',
    headers: {'Content-Type': 'application/json', 'X-CSRF-Token': csrf!}, body: JSON.stringify(body), signal}));
}
// No retries of credentials or state-changing commands. A caller may initiate a new action after the shown delay.
