import {NextIntlClientProvider, hasLocale} from 'next-intl';
import {getMessages} from 'next-intl/server';
import {notFound} from 'next/navigation';
import {routing} from '@/i18n/routing';
import '../globals.css';
export const dynamic = 'force-dynamic';
export default async function Layout({children, params}: {children: React.ReactNode; params: Promise<{locale: string}>}) {
  const {locale} = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const messages = await getMessages();
  return <html lang={locale} dir={locale === 'fa' ? 'rtl' : 'ltr'}><body>
    <NextIntlClientProvider locale={locale} messages={messages} timeZone="Asia/Tehran">{children}</NextIntlClientProvider>
  </body></html>;
}
