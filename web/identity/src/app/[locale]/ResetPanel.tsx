'use client';
import PasswordField from './PasswordField';
import {useTranslations} from 'next-intl';
import {useEffect,useRef,useState} from 'react';
import {BrowserError,post} from '@/lib/bff/client';
import {newFlow,otp} from '@/lib/bff/flow';
export default function ResetPanel({onSignIn,onAccepted}: {onSignIn:()=>void,onAccepted:()=>void}) {
  const t=useTranslations('Reset');
  const [stage,setStage]=useState<'Start'|'Proof'|'Accepted'>('Start');
  const [expires,setExpires]=useState(0);const [now,setNow]=useState(0);const [until,setUntil]=useState(0);
  const [busy,setBusy]=useState(false);const [error,setError]=useState('');
  const flow=useRef<string | undefined>(undefined);const active=useRef(true);const lock=useRef(false);
  useEffect(()=>{active.current=true;setNow(Date.now());const timer=setInterval(()=>setNow(Date.now()),1000);return()=>{active.current=false;flow.current=undefined;clearInterval(timer);};},[]);
  const remaining=Math.max(0,Math.ceil((until-now)/1000));const expired=expires>0 && now>=expires;
  async function submit(event:React.FormEvent<HTMLFormElement>) {
    event.preventDefault();if(lock.current || remaining || expired && stage==='Proof')return;
    const form=event.currentTarget;const data=new FormData(form);lock.current=true;setBusy(true);setError('');
    try {
      flow.current ??= newFlow();const flowId=flow.current;
      if(stage==='Start') {
        const contact=String(data.get('contact')??'').trim();
        const result=await post('/bff/password/reset',contact.includes('@')?{flowId,email:contact}:{flowId,mobile:contact});
        if(!active.current)return;setExpires(Date.parse(result.expiresAt));setStage('Proof');form.reset();
      } else {
        await post('/bff/password/reset/complete',{flowId,code:otp(String(data.get('code')??'')),recoveryCode:String(data.get('recoveryCode')??'').trim().toUpperCase(),newPassword:String(data.get('newPassword')??'')});
        if(!active.current)return;form.reset();setStage('Accepted');onAccepted();
      }
    } catch(failure) {
      if(active.current){setError(failure instanceof BrowserError?failure.code:'BFF_UNAVAILABLE');if(failure instanceof BrowserError && failure.retryAfterSeconds)setUntil(Date.now()+failure.retryAfterSeconds*1000);}
    } finally {lock.current=false;if(active.current)setBusy(false);}
  }
  const input='rounded-lg border border-slate-300 px-3 py-2';
  const known=['VALIDATION_FAILED','RESET_PROOF_REJECTED','RESET_FLOW_EXPIRED','CSRF_REJECTED','ORIGIN_REJECTED','SESSION_ALREADY_ACTIVE','IDEMPOTENCY_CONFLICT','RATE_LIMITED','BFF_UNAVAILABLE'];
  return <section data-testid="reset" className="grid gap-4" aria-busy={busy}>
    <h2 className="text-xl font-semibold">{t('title')}</h2>
    <p>{t(stage==='Start'?'requirements':stage==='Proof'?'uniform':'accepted')}</p>
    {stage!=='Accepted' && <form key={stage} onSubmit={submit} className="grid gap-4">
      {stage==='Start' ? <label className="grid gap-2">{t('contact')}<input name="contact" autoComplete="username" required maxLength={254} dir="ltr" className={input}/></label> : <>
        <label className="grid gap-2">{t('code')}<input name="code" autoComplete="one-time-code" inputMode="numeric" minLength={6} maxLength={8} pattern="[0-9۰-۹٠-٩]{6}|[0-9۰-۹٠-٩]{8}" required dir="ltr" className={input}/></label>
        <label className="grid gap-2">{t('recoveryCode')}<input name="recoveryCode" autoComplete="off" autoCapitalize="characters" spellCheck={false} required maxLength={64} dir="ltr" className={input}/></label>
        <PasswordField name="newPassword" label={t('newPassword')} autoComplete="new-password" minLength={15}/>
      </>}
      <button type="submit" className="rounded-lg bg-teal-800 px-4 py-3 text-white disabled:opacity-50" disabled={busy || remaining>0 || expired}>{busy?t('waiting'):t(stage==='Start'?'start':'complete')}</button>
    </form>}
    {expired && stage==='Proof' && <p role="status">{t('expired')}</p>}
    {error && <p role="alert" className="text-sm text-red-800">{t(`errors.${known.includes(error)?error:'UNKNOWN'}`)}</p>}
    {remaining>0 && <p role="status">{t('retry',{seconds:remaining})}</p>}
    <div className="flex flex-wrap gap-3">
      <button type="button" className={input} onClick={onSignIn} disabled={busy}>{t('signIn')}</button>
      {stage==='Proof' && <button type="button" className={input} disabled={busy || remaining>0} onClick={()=>{flow.current=undefined;setStage('Start');setExpires(0);setError('');}}>{t('restart')}</button>}
    </div>
  </section>;
}
