'use client';
import {useTranslations} from 'next-intl';
import {useEffect, useRef, useState} from 'react';
import {BrowserError, post} from '@/lib/bff/client';

function newFlow() {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
type Stage = 'Start' | 'AwaitingVerification' | 'PendingCredential' | 'Ready' | 'SetupRequired' | 'SignInRequired' | 'AwaitingSetup';
export default function RegistrationPanel({onSignIn}: {onSignIn: () => void}) {
  const t = useTranslations('Registration');
  const [stage, setStage] = useState<Stage>('Start');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [expires, setExpires] = useState(0);
  const [resendAt, setResendAt] = useState(0);
  const [retryAt, setRetryAt] = useState(0);
  const [now, setNow] = useState(0);
  const [outcome, setOutcome] = useState('');
  const flow = useRef<string | undefined>(undefined);
  const active = useRef(true);
  const locked = useRef(false);
  useEffect(() => {
    active.current = true; setNow(Date.now());
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => {active.current = false; flow.current = undefined; clearInterval(timer);};
  }, []);
  const retry = Math.max(0, Math.ceil((retryAt - now) / 1000));
  const resend = Math.max(0, Math.ceil((resendAt - now) / 1000));
  const expired = expires > 0 && now >= expires;
  const blocked = busy || retry > 0;
  async function action(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (locked.current || retry || expired && stage !== 'Start') return;
    const form = event.currentTarget; const data = new FormData(form);
    locked.current = true; setBusy(true); setError('');
    try {
      flow.current ??= newFlow(); const flowId = flow.current;
      if (stage === 'Start') {
        const contact = String(data.get('contact') ?? '');
        const fields = {flowId, password: String(data.get('password') ?? ''), displayName: String(data.get('displayName') ?? '')};
        const value = await post('/bff/register', contact.includes('@') ? {...fields, email: contact} : {...fields, mobile: contact});
        if (!active.current) return;
        setExpires(Date.parse(value.expiresAt)); setResendAt(Date.now() + value.resendAfterSeconds * 1000); setStage(value.status); form.reset();
      } else {
        // Localized digits are normalized only for OTP, never for passwords.
        const code = String(data.get('code') ?? '').replace(/[۰-۹٠-٩]/g, c => String(c.charCodeAt(0) - (c >= '۰' ? 1776 : 1632)));
        if (stage === 'SetupRequired') {
          const value = await post('/bff/register/setup', {flowId, code});
          if (!active.current) return;
          setExpires(Date.parse(value.expiresAt)); setStage(value.status); form.reset();
        } else if (stage === 'AwaitingSetup' || outcome) {
          const value = await post('/bff/register/complete', {flowId, code, newPassword: String(data.get('newPassword') ?? '')});
          if (!active.current) return;
          setStage(value.status); setOutcome(value.credentialOutcome); if (value.status === 'Ready') form.reset();
        } else {
          const value = await post('/bff/register/verify', {flowId, code});
          if (!active.current) return;
          setStage(value.status); if (value.status !== 'PendingCredential') form.reset();
        }
      }
    } catch (failure) {
      if (!active.current) return;
      const code = failure instanceof BrowserError ? failure.code : 'BFF_UNAVAILABLE';
      setError(code); if (failure instanceof BrowserError && failure.retryAfterSeconds) setRetryAt(Date.now() + failure.retryAfterSeconds * 1000);
    } finally {locked.current = false; if (active.current) setBusy(false);}
  }
  async function sendAgain() {
    if (locked.current || blocked || resend || expired || !flow.current) return;
    locked.current = true; setBusy(true); setError('');
    try {
      const value = await post('/bff/register/resend', {flowId: flow.current});
      if (active.current) setResendAt(Date.now() + value.resendAfterSeconds * 1000);
    } catch (failure) {
      if (active.current) {setError(failure instanceof BrowserError ? failure.code : 'BFF_UNAVAILABLE');
        if (failure instanceof BrowserError && failure.retryAfterSeconds) setRetryAt(Date.now() + failure.retryAfterSeconds * 1000);}
    } finally {locked.current = false; if (active.current) setBusy(false);}
  }
  function restart() {if (blocked) return; flow.current = undefined; setStage('Start'); setExpires(0); setOutcome(''); setError('');}
  const input = 'rounded-lg border border-slate-300 px-3 py-2';
  const button = 'rounded-lg bg-teal-800 px-4 py-3 font-semibold text-white disabled:opacity-50';
  const known = ['VALIDATION_FAILED','VERIFICATION_FAILED','REGISTRATION_FLOW_EXPIRED','IDEMPOTENCY_CONFLICT','SESSION_ALREADY_ACTIVE','CSRF_REJECTED','ORIGIN_REJECTED','RATE_LIMITED','BFF_UNAVAILABLE'];
  return <section data-testid="registration" aria-busy={busy} className="grid gap-4">
    <h2 className="text-xl font-semibold">{t('title')}</h2>
    {stage !== 'Start' && <p role="status">{t(`states.${stage}`)}</p>}
    {outcome === 'ExistingWinner' && <p>{t('existingWinner')}</p>}
    {expired && stage !== 'Ready' && stage !== 'SignInRequired' && <p role="status">{t('expired')}</p>}
    {stage !== 'Ready' && stage !== 'SignInRequired' && <form key={stage === 'Start' ? 'start' : stage === 'AwaitingSetup' || outcome ? 'complete' : stage === 'SetupRequired' ? 'setup' : 'verify'} onSubmit={action} className="grid gap-4">
      {stage === 'Start' ? <>
        <label className="grid gap-2">{t('displayName')}<input name="displayName" autoComplete="name" required maxLength={100} className={input} /></label>
        <label className="grid gap-2">{t('contact')}<input name="contact" autoComplete="username" required maxLength={254} dir="ltr" className={input} /></label>
        <label className="grid gap-2">{t('password')}<input name="password" type="password" autoComplete="new-password" required minLength={15} maxLength={128} className={input} /></label>
      </> : <>
        <label className="grid gap-2">{stage === 'SetupRequired' ? t('originalCode') : t('code')}<input name="code" inputMode="numeric" autoComplete="one-time-code" required minLength={6} maxLength={6} pattern="[0-9۰-۹٠-٩]{6}" dir="ltr" className={input} /></label>
        {(stage === 'AwaitingSetup' || outcome) && <label className="grid gap-2">{t('password')}<input name="newPassword" type="password" autoComplete="new-password" required minLength={15} maxLength={128} className={input} /></label>}
      </>}
      <button className={button} disabled={blocked || expired && stage !== 'Start'}>{busy ? t('waiting') : t(stage === 'Start' ? 'start' : stage === 'SetupRequired' ? 'requestSetup' : stage === 'AwaitingSetup' || outcome ? 'complete' : stage === 'PendingCredential' ? 'check' : 'verify')}</button>
    </form>}
    {stage === 'AwaitingVerification' && <button type="button" onClick={() => void sendAgain()} disabled={blocked || resend > 0 || expired} className={input}>{resend ? t('resendWait', {seconds: resend}) : t('resend')}</button>}
    {retry > 0 && <p role="status">{t('retry', {seconds: retry})}</p>}
    {error && <p role="alert" className="text-sm text-red-800">{t(`errors.${known.includes(error) ? error : 'UNKNOWN'}`)}</p>}
    <div className="flex flex-wrap gap-3">
      <button type="button" onClick={onSignIn} disabled={busy} className={input}>{t('signIn')}</button>
      {stage !== 'Start' && <button type="button" onClick={restart} disabled={blocked} className={input}>{t('restart')}</button>}
    </div>
  </section>;
}
