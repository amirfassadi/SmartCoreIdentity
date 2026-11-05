# Unified platform UI continuation

This change is stacked on `review/identity-bff`; it does not replace the existing
registration, session or recovery implementation. Shared SmartCore design tokens,
self-hosted Vazirmatn fonts and the generated `design-manifest.json` align the
account interface with the Platform console and Service Business admin.

`SMARTCORE_CONSOLE_ORIGIN` is an optional server-only, validated launcher origin.
It carries no token, cookie or return URL. Existing BFF guards and auth flows remain
the source of account/session state. An unavailable backend is never converted into
an authenticated preview. With account flags off, fields are disabled and no BFF
requests are issued. With flags on, the original registration/recovery flows apply.

Next.js is patched to 16.4.5. Shared design source lives in the Platform repository
at `packages/design`; run its `tools/sync-design.mjs` with this checkout's path to
update the generated CSS/font snapshot. The source hash is recorded in the manifest.
The existing account UI acceptance suite remains applicable.

These presentation changes do not approve the draft PR stack, ADR dispositions,
production release gates, outbound messaging or owner VPS deployment. See
`PR_STACK.md` and `RELEASE_GATES.md` for those separate prerequisites.
