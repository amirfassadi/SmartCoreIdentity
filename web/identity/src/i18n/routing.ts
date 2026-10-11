import {defineRouting} from 'next-intl/routing';
export const routing = defineRouting({
  locales: ['fa', 'en'], defaultLocale: 'fa', localePrefix: 'always',
  localeCookie: {name: 'smartcore-locale', secure: true, sameSite: 'lax', path: '/', maxAge: 15552000}
});
