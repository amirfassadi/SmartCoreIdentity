// Generated from contracts/bff-browser.openapi.yaml; do not edit.
// SHA256 1eb051956d42b011883940c9c654021f4f9f402c9440e79d1d6b0984ee21a0d3
export type EmptyRequest = { };
export type LoginRequest = { email?: string; mobile?: string; password: string; };
export type CsrfResponse = { requestToken: string; };
export type Person = { displayName: string; email: string | null; mobile: string | null; status: "Active"; recoveryEnrollmentRequired: boolean; };
export type SessionResponse = { person: Person; };
export type ValidationError = { code: "VALIDATION_FAILED"; };
export type SessionError = { code: "REAUTHENTICATION_REQUIRED"; };
export type LoginError = { code: "UNAUTHORIZED"; };
export type ForbiddenError = { code: "CSRF_REJECTED" | "ORIGIN_REJECTED"; };
export type ConflictError = { code: "SESSION_ALREADY_ACTIVE"; };
export type TooLargeError = { code: "REQUEST_TOO_LARGE"; };
export type MediaTypeError = { code: "UNSUPPORTED_MEDIA_TYPE"; };
export type RateError = { code: "RATE_LIMITED"; retryAfterSeconds: number; };
export type InternalError = { code: "INTERNAL_ERROR"; };
export type CsrfUnavailable503Error = { code: "BFF_UNAVAILABLE"; retryAfterSeconds: number; };
export type LoginBusy503Error = { code: "BFF_UNAVAILABLE" | "AUTHENTICATION_BUSY"; retryAfterSeconds: number; };
export type Busy503Error = { code: "BFF_BUSY" | "BFF_UNAVAILABLE"; retryAfterSeconds: number; };
export type RouterError = { code: "NOT_FOUND" | "METHOD_NOT_ALLOWED"; };
export type BrowserPostPath = '/bff/login' | '/bff/me' | '/bff/me/action' | '/bff/logout';
