BEGIN;
-- Eight-digit reset OTP permits a shorter denial-of-recovery window.
-- Clamp existing stops against their original counted failure; never extend a stop.
UPDATE auth_reset_failures
 SET blocked_until=LEAST(blocked_until,last_failure_at + interval '60 minutes')
 WHERE blocked_until IS NOT NULL AND last_failure_at IS NOT NULL;
INSERT INTO schema_versions(version) VALUES(11);
COMMIT;
