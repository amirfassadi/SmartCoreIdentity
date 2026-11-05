import {NextIntlClientProvider, hasLocale} from 'next-intl';
import {getMessages} from 'next-intl/server';
import {notFound} from 'next/navigation';
import {routing} from '@/i18n/routing';
import {PlatformLinkProvider} from '../platform-link';
import '../globals.css';
export const dynamic = 'force-dynamic';
export default async function Layout({children, params}: {children: React.ReactNode; params: Promise<{locale: string}>}) {
  const {locale} = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const messages = await getMessages();
  const candidate = process.env.SMARTCORE_CONSOLE_ORIGIN;
  let consoleOrigin: string | null = null;
  try {
    if (candidate) {
      const url = new URL(candidate);
      if ((url.protocol === 'https:' || (url.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(url.hostname))) && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash) consoleOrigin = url.origin;
    }
  } catch { /* Leave the optional launcher link unavailable for invalid configuration. */ }
  return <html lang={locale} dir={locale === 'fa' ? 'rtl' : 'ltr'}><body>
    <NextIntlClientProvider locale={locale} messages={messages} timeZone="Asia/Tehran"><PlatformLinkProvider origin={consoleOrigin}>{children}</PlatformLinkProvider></NextIntlClientProvider>
  </body></html>;
}
