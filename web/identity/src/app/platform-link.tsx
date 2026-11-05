"use client";
import { createContext, useContext } from "react";
import { useLocale } from "next-intl";
const PlatformOrigin = createContext<string | null>(null);
export function PlatformLinkProvider({
  origin,
  children,
}: {
  origin: string | null;
  children: React.ReactNode;
}) {
  return (
    <PlatformOrigin.Provider value={origin}>{children}</PlatformOrigin.Provider>
  );
}
export function PlatformLink() {
  const origin = useContext(PlatformOrigin);
  const locale = useLocale();
  if (!origin) return null;
  return (
    <a className="platform-return" href={`${origin}/${locale}`}>
      {locale === "fa" ? "فضای کار اسمارت‌کور" : "SmartCore workspace"}{" "}
      <span aria-hidden="true">↗</span>
    </a>
  );
}
