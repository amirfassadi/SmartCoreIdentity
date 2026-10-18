'use client';
import {useId, useState} from 'react';
import {useTranslations} from 'next-intl';
export default function PasswordField({name,label,autoComplete,minLength,invalid,describedBy}: {
  name:string;label:string;autoComplete:'current-password'|'new-password';minLength?:number;invalid?:boolean;describedBy?:string;
}) {
  const t=useTranslations('Fields');const [visible,setVisible]=useState(false);const id=useId();
  return <div className="password-field"><label className="field-label" htmlFor={id}>{label}</label><div className="password-control">
    <input id={id} name={name} type={visible?'text':'password'} autoComplete={autoComplete} required minLength={minLength} maxLength={128} dir="ltr" aria-invalid={invalid} aria-describedby={describedBy}/>
    <button type="button" aria-controls={id} aria-pressed={visible} aria-label={t(visible?'hidePassword':'showPassword')} onClick={()=>setVisible(value=>!value)}>{t(visible?'hide':'show')}</button>
  </div></div>;
}
