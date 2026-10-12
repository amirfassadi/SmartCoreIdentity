'use client';
import {useLocale, useTranslations} from 'next-intl';
import {useCallback, useEffect, useRef, useState} from 'react';
import {bootstrap, BrowserError, invalidateCsrf, post, type Person} from '@/lib/bff/client';

const enabled = process.env.NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED === 'true';
const events = ['login', 'logout', 'session-lost'] as const;
type SessionEvent = typeof events[number];
export default function AccountShell() {
  const t = useTranslations('Shell');
  const a = useTranslations('Account');
  const locale = useLocale();
  const [person, setPerson] = useState<Person>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [retryUntil, setRetryUntil] = useState(0);
  const [remaining, setRemaining] = useState(0);
  const generation = useRef(0);
  const read = useRef<AbortController | null>(null);
  const channel = useRef<BroadcastChannel | null>(null);
  const locked = useRef(false);
  const clear = useCallback(() => {
    generation.current++; read.current?.abort(); invalidateCsrf(); setPerson(undefined);
  }, []);
  const broadcast = useCallback((type: SessionEvent) => {
    if (channel.current) channel.current.postMessage(type);
    else {try {localStorage.setItem('smartcore-session-event', JSON.stringify({type, stamp: Date.now()}));} catch { /* UX fallback only. */ }}
  }, []);
  const handleError = useCallback((failure: unknown) => {
    const code = failure instanceof BrowserError ? failure.code : 'BFF_UNAVAILABLE';
    if (code === 'REAUTHENTICATION_REQUIRED') {clear(); broadcast('session-lost');}
    if (failure instanceof BrowserError && failure.retryAfterSeconds) setRetryUntil(Date.now() + failure.retryAfterSeconds * 1000);
    setError(code);
  }, [clear, broadcast]);
  const refresh = useCallback(async (action = false) => {
    if (!enabled || locked.current) return;
    read.current?.abort(); const controller = new AbortController(); read.current = controller;
    const current = generation.current;
    try {
      await bootstrap(controller.signal);
      const result = await post(action ? '/bff/me/action' : '/bff/me', {}, controller.signal);
      if (!controller.signal.aborted && current === generation.current && result) {setPerson(result.person); setError('');}
    } catch (failure) {
      if (!controller.signal.aborted && current === generation.current) {
        if (failure instanceof BrowserError && failure.code === 'REAUTHENTICATION_REQUIRED') {clear(); broadcast('session-lost');}
        else handleError(failure);
      }
    }
  }, [clear, handleError, broadcast]);
  useEffect(() => {
    if (!enabled) return;
    const receive = (value: unknown) => {
      if (typeof value !== 'string' || !events.includes(value as SessionEvent)) return;
      clear(); setError(''); if (value === 'login') void refresh();
    };
    if (typeof BroadcastChannel !== 'undefined') {
      channel.current = new BroadcastChannel('smartcore-session'); channel.current.onmessage = event => receive(event.data);
    }
    const storage = (event: StorageEvent) => {
      if (event.key !== 'smartcore-session-event' || !event.newValue) return;
      try {receive(JSON.parse(event.newValue).type);} catch { /* Ignore malformed non-secret event. */ }
    };
    const visible = () => {if (document.visibilityState === 'visible') void refresh();};
    const focus = () => {void refresh();};
    window.addEventListener('storage', storage); window.addEventListener('focus', focus); window.addEventListener('pageshow', focus);
    document.addEventListener('visibilitychange', visible); void refresh();
    return () => {
      generation.current++; read.current?.abort(); channel.current?.close(); channel.current = null;
      window.removeEventListener('storage', storage); window.removeEventListener('focus', focus); window.removeEventListener('pageshow', focus);
      document.removeEventListener('visibilitychange', visible);
    };
  }, [clear, refresh]);
  useEffect(() => {
    const update = () => setRemaining(Math.max(0, Math.ceil((retryUntil - Date.now()) / 1000)));
    update(); const timer = setInterval(update, 1000); return () => clearInterval(timer);
  }, [retryUntil]);
  async function login(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (locked.current || remaining) return;
    const form = event.currentTarget; const fields = new FormData(form);
    const contact = String(fields.get('contact') ?? ''); const password = String(fields.get('password') ?? '');
    clear(); const current = generation.current; locked.current = true; setBusy(true); setError('');
    try {
      const result = await post('/bff/login', contact.includes('@') ? {email: contact, password} : {mobile: contact, password});
      if (current !== generation.current) return;
      form.reset(); if (result) setPerson(result.person);
      await bootstrap(); broadcast('login');
    } catch (failure) {if (current === generation.current) handleError(failure);}
    finally {locked.current = false; setBusy(false);}
  }
  async function logout() {
    if (locked.current || remaining) return;
    locked.current = true; read.current?.abort(); const current = generation.current; setBusy(true); setError('');
    try {
      await post('/bff/logout', {});
      clear(); broadcast('logout'); await bootstrap();
    } catch (failure) {if (current === generation.current) handleError(failure);}
    finally {locked.current = false; setBusy(false);}
  }
  const knownErrors = ['UNAUTHORIZED','REAUTHENTICATION_REQUIRED','CSRF_REJECTED','ORIGIN_REJECTED','VALIDATION_FAILED','SESSION_ALREADY_ACTIVE','RATE_LIMITED','BFF_BUSY','BFF_UNAVAILABLE','AUTHENTICATION_BUSY'];
  return <main className="mx-auto flex min-h-screen max-w-5xl flex-col px-6 py-8 sm:px-10">
    <header className="flex items-center justify-between border-b border-slate-200 pb-6">
      <span className="text-xl font-bold tracking-tight">{t('brand')}</span>
      <a className="rounded-full border border-slate-300 px-4 py-2 text-sm" href={locale === 'fa' ? '/en' : '/fa'} lang={locale === 'fa' ? 'en' : 'fa'}>{t('language')}</a>
    </header>
    <section className="my-auto py-16 text-start">
      <p className="mb-5 text-sm font-semibold text-teal-700">{t('stage')}</p>
      <h1 className="max-w-2xl text-4xl font-semibold leading-snug sm:text-5xl">{t('title')}</h1>
      <p className="mt-6 max-w-xl text-lg leading-8 text-slate-600">{t('description')}</p>
      <div className="mt-10 max-w-xl rounded-2xl border border-slate-200 bg-white p-6 shadow-sm" aria-busy={busy}>
        {!enabled ? <p>{t('status')}</p> : person ? <div data-testid="profile">
          <h2 className="text-2xl font-semibold">{a('welcome', {name: person.displayName})}</h2>
          <p className="mt-3" dir="ltr">{person.email ?? person.mobile}</p>
          {person.recoveryEnrollmentRequired && <p className="mt-3 text-sm text-slate-600">{a('recoveryPending')}</p>}
          <div className="mt-6 flex flex-wrap gap-3">
            <button className="rounded-lg border border-slate-300 px-4 py-2" onClick={() => void refresh(true)} disabled={busy || remaining > 0}>{a('continue')}</button>
            <button className="rounded-lg bg-teal-800 px-4 py-2 text-white" onClick={() => void logout()} disabled={busy || remaining > 0}>{a('logout')}</button>
          </div>
        </div> : <form onSubmit={login} className="grid gap-4">
          <h2 className="text-xl font-semibold">{a('login')}</h2>
          <label className="grid gap-2">{a('contact')}<input name="contact" type="text" autoComplete="username" required maxLength={254} dir="ltr" className="rounded-lg border border-slate-300 px-3 py-2" /></label>
          <label className="grid gap-2">{a('password')}<input name="password" type="password" autoComplete="current-password" required maxLength={128} className="rounded-lg border border-slate-300 px-3 py-2" /></label>
          <button className="rounded-lg bg-teal-800 px-4 py-3 font-semibold text-white" disabled={busy || remaining > 0}>{busy ? a('waiting') : a('login')}</button>
        </form>}
        {error && <p role="alert" className="mt-4 text-sm text-red-800">{a(`errors.${knownErrors.includes(error) ? error : 'UNKNOWN'}`)}</p>}
        {remaining > 0 && <p role="status" className="mt-3 text-sm">{a('retry', {seconds: remaining})}</p>}
      </div>
    </section>
  </main>;
}
