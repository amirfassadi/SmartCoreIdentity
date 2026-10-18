'use client';
import PasswordField from './PasswordField';
import {useTranslations} from 'next-intl';
import {useEffect, useRef, useState} from 'react';
import {BrowserError, post} from '@/lib/bff/client';
import {newFlow} from '@/lib/bff/flow';
const input='rounded-lg border border-slate-300 px-3 py-2';
const button='rounded-lg bg-teal-800 px-4 py-3 text-white disabled:opacity-50';
export default function RecoveryPanel({onAccepted,onEnrolled,onSessionLost,visible=true}: {visible?:boolean,onAccepted:()=>void,onEnrolled:()=>void,onSessionLost:()=>void}) {
  const t=useTranslations('Recovery');
  const [mode,setMode]=useState<'overview'|'enroll'|'change'>('overview');
  const [code,setCode]=useState<string>();
  const [receipt,setReceipt]=useState('');
  const [error,setError]=useState('');
  const [busy,setBusy]=useState(false);
  const [until,setUntil]=useState(0);
  const [remaining,setRemaining]=useState(0);
  const flow=useRef<string | undefined>(undefined);
  const lock=useRef(false);const active=useRef(true);
  const shown=useRef(visible);shown.current=visible;
  useEffect(()=>{if(!visible){setCode(undefined);setReceipt(value=>value==='Issued'?'Hidden':value);}},[visible]);
  useEffect(()=>{
    active.current=true;
    const hidden=()=>{if(document.hidden){setCode(undefined);setReceipt(value=>value==='Issued'?'Hidden':value);}};
    document.addEventListener('visibilitychange',hidden);
    return()=>{active.current=false;flow.current=undefined;document.removeEventListener('visibilitychange',hidden);};
  },[]);
  useEffect(()=>{const update=()=>setRemaining(Math.max(0,Math.ceil((until-Date.now())/1000)));update();const timer=setInterval(update,1000);return()=>clearInterval(timer);},[until]);
  async function submit(event:React.FormEvent<HTMLFormElement>) {
    event.preventDefault();if(lock.current || remaining || mode==='overview')return;
    const form=event.currentTarget;const data=new FormData(form);lock.current=true;setBusy(true);setError('');
    try {
      flow.current ??= newFlow();const flowId=flow.current;
      if(mode==='enroll') {
        if(data.get('acceptLossRisk')!=='on')throw new BrowserError('VALIDATION_FAILED');
        const result=await post('/bff/recovery/enroll',{flowId,currentPassword:String(data.get('currentPassword')??''),acceptLossRisk:true});
        if(!active.current)return;form.reset();const canShow=shown.current && !document.hidden;setReceipt(result.status==='Issued' && !canShow?'Hidden':result.status);setCode(result.status==='Issued' && canShow?result.recoveryCode:undefined);
      } else {
        await post('/bff/password/change',{flowId,currentPassword:String(data.get('currentPassword')??''),newPassword:String(data.get('newPassword')??'')});
        if(!active.current)return;form.reset();setCode(undefined);onAccepted();
      }
    } catch(failure) {
      if(active.current){if(failure instanceof BrowserError && failure.code==='REAUTHENTICATION_REQUIRED'){setCode(undefined);onSessionLost();return;}setError(failure instanceof BrowserError?failure.code:'BFF_UNAVAILABLE');
        if(failure instanceof BrowserError && failure.retryAfterSeconds)setUntil(Date.now()+failure.retryAfterSeconds*1000);}
    } finally {lock.current=false;if(active.current)setBusy(false);}
  }
  function choose(value:'overview'|'enroll'|'change') {if(busy || remaining || code)return;flow.current=undefined;setMode(value);setReceipt('');setError('');}
  const known=['VALIDATION_FAILED','CREDENTIALS_REJECTED','REAUTHENTICATION_REQUIRED','CSRF_REJECTED','ORIGIN_REJECTED','IDEMPOTENCY_CONFLICT','RECOVERY_UNAVAILABLE','RATE_LIMITED','BFF_BUSY','BFF_UNAVAILABLE'];
  return <section data-testid="recovery" className="mt-6 grid gap-4 border-t border-slate-200 pt-6" aria-busy={busy}>
    <h3 className="font-semibold">{t('securityTitle')}</h3>
    <p className="text-sm leading-7 text-slate-600">{t('securityDescription')}</p>
    <div className="flex flex-wrap gap-3">
      <button type="button" className={input} aria-expanded={mode==='enroll'} disabled={busy || remaining>0 || !!code} onClick={()=>choose('enroll')}>{t('enroll')}</button>
      <button type="button" className={input} aria-expanded={mode==='change'} disabled={busy || remaining>0 || !!code} onClick={()=>choose('change')}>{t('change')}</button>
      {mode!=='overview' && <button type="button" className={input} disabled={busy || remaining>0 || !!code} onClick={()=>choose('overview')}>{t('backToAccount')}</button>}
    </div>
    {code ? <div role="region" aria-label={t('oneTime')} className="grid gap-4">
      <p>{t('oneTime')}</p><p data-testid="recovery-code" dir="ltr" className="break-all rounded-lg border border-slate-300 p-3 font-mono">{code}</p>
      <p>{t('keepCode')}</p>
      <button type="button" className={button} onClick={()=>{setCode(undefined);setReceipt('Saved');onEnrolled();}}>{t('saved')}</button>
    </div> : receipt ? <div className="grid gap-3">
      <p role="status">{t(`receipts.${receipt}`)}</p>
      <button type="button" className={input} disabled={busy || remaining>0} onClick={()=>choose('enroll')}>{t('replace')}</button>
    </div> : mode!=='overview' && <form key={mode} onSubmit={submit} className="grid gap-4">
      <h3 className="font-semibold">{t(mode)}</h3>
      <p className="text-sm leading-7 text-slate-600">{t(mode==='enroll'?'enrollDescription':'changeDescription')}</p>
      <PasswordField name="currentPassword" label={t('currentPassword')} autoComplete="current-password"/>
      {mode==='enroll' ? <label className="flex items-start gap-3"><input name="acceptLossRisk" type="checkbox" required className="mt-1"/>{t('consent')}</label> : <PasswordField name="newPassword" label={t('newPassword')} autoComplete="new-password" minLength={15}/>}
      <button type="submit" className={button} disabled={busy || remaining>0}>{busy?t('waiting'):t(mode==='enroll'?'issue':'submitChange')}</button>
    </form>}
    {error && <p role="alert" className="text-sm text-red-800">{t(`errors.${known.includes(error)?error:'UNKNOWN'}`)}</p>}
    {remaining>0 && <p role="status">{t('retry',{seconds:remaining})}</p>}
  </section>;
}
