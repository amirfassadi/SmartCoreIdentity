'use client';
import {useLocale, useTranslations} from 'next-intl';
export default function AccountShell() {
  const t = useTranslations('Shell');
  const locale = useLocale();
  return <main className="mx-auto flex min-h-screen max-w-5xl flex-col px-6 py-8 sm:px-10">
    <header className="flex items-center justify-between border-b border-slate-200 pb-6">
      <span className="text-xl font-bold tracking-tight">{t('brand')}</span>
      <a className="rounded-full border border-slate-300 px-4 py-2 text-sm" href={locale === 'fa' ? '/en' : '/fa'} lang={locale === 'fa' ? 'en' : 'fa'}>{t('language')}</a>
    </header>
    <section className="my-auto py-16 text-start">
      <p className="mb-5 text-sm font-semibold text-teal-700">{t('stage')}</p>
      <h1 className="max-w-2xl text-4xl font-semibold leading-snug sm:text-5xl">{t('title')}</h1>
      <p className="mt-6 max-w-xl text-lg leading-8 text-slate-600">{t('description')}</p>
      <div className="mt-10 grid gap-4 sm:grid-cols-2">
        {[t('account'), t('dashboard')].map(label => <article key={label} className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm"><h2 className="text-lg font-semibold">{label}</h2><p className="mt-3 text-sm text-slate-500">{t('status')}</p></article>)}
      </div>
    </section>
  </main>;
}
