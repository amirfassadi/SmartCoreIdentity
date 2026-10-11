import createMiddleware from 'next-intl/middleware';
import {NextRequest, NextResponse} from 'next/server';
import {routing} from './i18n/routing';
const localize = createMiddleware(routing);
export function proxy(request: NextRequest) {
  // Defence in depth: a misrouted /bff request must never become an HTML page.
  if (request.nextUrl.pathname === '/bff' || request.nextUrl.pathname.startsWith('/bff/')) {
    return NextResponse.json({code: 'NOT_FOUND'}, {status: 404, headers: {'Cache-Control': 'no-store'}});
  }
  const nonce = Buffer.from(crypto.getRandomValues(new Uint8Array(24))).toString('base64');
  const csp = `default-src 'self'; script-src 'self' 'nonce-${nonce}' 'strict-dynamic'; style-src 'self' 'nonce-${nonce}'; style-src-attr 'none'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`;
  request.headers.set('Content-Security-Policy', csp);
  request.headers.set('x-nonce', nonce);
  const response = localize(request);
  // Preserve next-intl's rewrite headers while forwarding the fresh nonce for Next rendering.
  const overridden = new Set((response.headers.get('x-middleware-override-headers') ?? '').split(',').filter(Boolean));
  for (const name of ['content-security-policy', 'x-nonce']) {
    overridden.add(name);
    response.headers.set(`x-middleware-request-${name}`, request.headers.get(name)!);
  }
  response.headers.set('x-middleware-override-headers', [...overridden].join(','));
  response.headers.set('Content-Security-Policy', csp);
  response.headers.set('Cache-Control', 'no-store');
  response.headers.set('X-Content-Type-Options', 'nosniff');
  response.headers.set('X-Frame-Options', 'DENY');
  response.headers.set('Referrer-Policy', 'no-referrer');
  response.headers.set('Permissions-Policy', 'camera=(), microphone=(), geolocation=()');
  if (request.nextUrl.protocol === 'https:') response.headers.set('Strict-Transport-Security', 'max-age=31536000');
  return response;
}
export const config = {matcher: ['/((?!_next|favicon.ico).*)']};
