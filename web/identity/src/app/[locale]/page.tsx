'use client';
import {useLocale, useTranslations} from 'next-intl';
import RecoveryPanel from './RecoveryPanel';
import ResetPanel from './ResetPanel';
import RegistrationPanel from './RegistrationPanel';
import PasswordField from './PasswordField';
import {useCallback, useEffect, useRef, useState} from 'react';
import {bootstrap, BrowserError, invalidateCsrf, post, type Person} from '@/lib/bff/client';

const enabled = process.env.NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED === 'true';
const registrationEnabled = process.env.NEXT_PUBLIC_BFF_REGISTRATION_ENABLED === 'true';
const recoveryEnabled = process.env.NEXT_PUBLIC_BFF_RECOVERY_ENABLED === 'true';
const events = ['login', 'logout', 'session-lost'] as const;
type SessionEvent = typeof events[number];
export default function AccountShell() {
  const t = useTranslations('Shell');
  const a = useTranslations('Account');
  const locale = useLocale();
  const [checking, setChecking] = useState(enabled);
  const [accountView, setAccountView] = useState<'overview'|'security'>('overview');
  const [person, setPerson] = useState<Person>();
  const [register, setRegister] = useState(false);
  const [reset, setReset] = useState(false);
  const [notice, setNotice] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [retryUntil, setRetryUntil] = useState(0);
  const [remaining, setRemaining] = useState(0);
  const generation = useRef(0);
  const read = useRef<AbortController | null>(null);
  const channel = useRef<BroadcastChannel | null>(null);
  const locked = useRef(false);
  const clear = useCallback(() => {
    generation.current++; read.current?.abort(); invalidateCsrf(); setPerson(undefined); setAccountView('overview');
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
      const result = await post(action ? '/bff/me/action' : '/bff/me', {}, controller.signal);
      if (!controller.signal.aborted && current === generation.current && result) {setPerson(result.person); setNotice(false); setError('');}
    } catch (failure) {
      if (!controller.signal.aborted && current === generation.current) {
        if (failure instanceof BrowserError && failure.code === 'REAUTHENTICATION_REQUIRED') {clear(); broadcast('session-lost');}
        else handleError(failure);
      }
    } finally {if (!controller.signal.aborted && current === generation.current) setChecking(false);}
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
    const contact = String(fields.get('contact') ?? '').trim(); const password = String(fields.get('password') ?? '');
    clear(); const current = generation.current; locked.current = true; setBusy(true); setError('');
    try {
      const result = await post('/bff/login', contact.includes('@') ? {email: contact, password} : {mobile: contact, password});
      if (current !== generation.current) return;
      form.reset(); if (result) {setPerson(result.person); setNotice(false);}
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
  return <main className="identity-shell">
    <a className="skip-link" href="#account-content">{t('skip')}</a>
    <header className="site-header">
      <a className="brand" href={`/${locale}`} aria-label={t('brand')}><span className="brand-symbol" aria-hidden="true">S</span><span>{t('brand')}<small>SMARTCORE IDENTITY</small></span></a>
      <a className="language-link" href={locale === 'fa' ? '/en' : '/fa'} lang={locale === 'fa' ? 'en' : 'fa'}>{t('language')} <span aria-hidden="true">↗</span></a>
    </header>
    <div className={`identity-layout ${person ? 'is-account' : ''}`}>
      <aside className="identity-story" aria-labelledby="story-title">
        <p className="eyebrow"><span className="status-dot"/>{t('stage')}</p>
        <h1 id="story-title">{t(person ? 'accountTitle' : 'title')}</h1>
        <p className="story-description">{t(person ? 'accountDescription' : 'description')}</p>
        <div className="identity-art" aria-hidden="true"><div className="orbit orbit-one"/><div className="orbit orbit-two"/><div className="core-tile">S<span>SMARTCORE</span></div><span className="art-label art-label-one">IDENTITY</span><span className="art-label art-label-two">YOUR WORKSPACE</span><span className="art-node node-one"/><span className="art-node node-two"/></div>
        <div className="story-bottom"><span className="story-number">01 /</span><p>{t('promise')}</p></div>
      </aside>
      <section id="account-content" className="account-surface" tabIndex={-1} aria-label={t('account')}>
        <div className="surface-heading"><span className="eyebrow">{person ? a('accountHome') : register ? a('register') : reset ? a('resetHeading') : a('loginHeading')}</span><span className="section-number" aria-hidden="true">{person ? '02' : '01'}</span></div>
        {process.env.NEXT_PUBLIC_PRIVATE_TEST_MODE === 'true' && <p className="environment-notice">{t('privateNotice')}</p>}
        <div aria-busy={busy || checking}>
        {!enabled ? <div className="empty-state"><h2>{t('previewTitle')}</h2><p>{t('previewDescription')}</p></div> : checking ? <p role="status" className="loading-state">{a('checking')}</p> : person ? <div data-testid="profile">
          <div className="profile-heading"><span className="avatar" aria-hidden="true">{person.displayName.slice(0,1)}</span><div><h2>{a('welcome', {name: person.displayName})}</h2><p className="profile-contact" dir="ltr">{person.email ?? person.mobile}</p></div></div>
          <p role="status" className="success-notice">{a('signedIn')}</p>
          <nav className="account-navigation" aria-label={a('navigation')}>
            <button type="button" aria-pressed={accountView==='overview'} onClick={()=>setAccountView('overview')}>{a('overview')}</button>
            {recoveryEnabled && <button type="button" aria-pressed={accountView==='security'} onClick={()=>setAccountView('security')}>{a('security')}</button>}
          </nav>
          <section hidden={accountView!=='overview'} className="overview-panel">
            <h3>{a('details')}</h3>
            <dl className="account-details"><div><dt>{a('displayName')}</dt><dd>{person.displayName}</dd></div><div><dt>{a('contact')}</dt><dd dir="ltr">{person.email ?? person.mobile}</dd></div><div><dt>{a('recoveryStatus')}</dt><dd>{a(person.recoveryEnrollmentRequired ? 'recoveryMissing' : 'recoveryReady')}</dd></div></dl>
            {recoveryEnabled && person.recoveryEnrollmentRequired && <div className="recovery-reminder"><p>{a('recoveryPending')}</p><button type="button" className="text-button" onClick={()=>setAccountView('security')}>{a('setupRecovery')}</button></div>}
          </section>
          {recoveryEnabled && <div hidden={accountView!=='security'}><RecoveryPanel visible={accountView==='security'} onSessionLost={() => {clear(); setNotice(false); broadcast('session-lost');}} onEnrolled={() => void refresh(true)} onAccepted={() => {clear(); setRegister(false); setReset(false); setNotice(true); broadcast('session-lost');}} /></div>}
          <div className="account-actions"><button className="secondary-button" onClick={() => void refresh(true)} disabled={busy || remaining > 0}>{a('refreshAccount')}</button><button className="text-button" onClick={() => void logout()} disabled={busy || remaining > 0}>{a('logout')}</button></div>
        </div> : reset && recoveryEnabled ? <ResetPanel onSignIn={() => {setReset(false);setError('');}} onAccepted={() => {clear(); setNotice(true); broadcast('session-lost');}} /> : register && registrationEnabled ? <RegistrationPanel onSignIn={() => {setRegister(false);setError('');}} /> : <form onSubmit={login} className="auth-form">
          <div className="form-intro"><h2>{a('loginHeading')}</h2><p>{a('loginDescription')}</p></div>
          <label className="field-label">{a('contact')}<input name="contact" type="text" autoComplete="username" autoCapitalize="none" spellCheck={false} required maxLength={254} dir="ltr" aria-invalid={error==='UNAUTHORIZED'} aria-describedby={error ? 'account-error' : undefined}/></label>
          <PasswordField name="password" label={a('password')} autoComplete="current-password" invalid={error==='UNAUTHORIZED'} describedBy={error ? 'account-error' : undefined}/>
          {recoveryEnabled && <button type="button" onClick={() => {setNotice(false);setError('');setReset(true);setRegister(false);}} disabled={busy} className="text-button forgot-button">{a('reset')}</button>}
          <button type="submit" className="primary-button" disabled={busy || remaining > 0}>{busy ? a('waiting') : a('login')} <span aria-hidden="true">↗</span></button>
          {registrationEnabled && <div className="form-alternative"><p>{a('newAccount')}</p><button type="button" onClick={() => {setNotice(false);setError('');setRegister(true);}} disabled={busy} className="secondary-button">{a('register')}</button></div>}
          <p className="form-footnote">{a('passwordPrivacy')}</p>
        </form>}
        {notice && <p role="status" className="success-notice">{a('mutationAccepted')}</p>}
        {error && <p id="account-error" role="alert" className="error-notice">{a(`errors.${knownErrors.includes(error) ? error : 'UNKNOWN'}`)}</p>}
        {remaining > 0 && <p role="status" className="environment-notice">{a('retry', {seconds: remaining})}</p>}
        </div>
      </section>
    </div>
    <footer className="site-footer"><span>{t('brand')} · {t('account')}</span><span>{t('footer')}</span></footer>
  </main>;
}
