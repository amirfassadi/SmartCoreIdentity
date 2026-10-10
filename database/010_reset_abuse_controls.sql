BEGIN;
ALTER TABLE auth_reset_failures ADD COLUMN saturated_windows integer NOT NULL DEFAULT 0 CHECK(saturated_windows BETWEEN 0 AND 6);
ALTER TABLE auth_reset_failures ADD COLUMN blocked_until timestamptz;
ALTER TABLE auth_reset_failures ADD COLUMN last_failure_at timestamptz;
-- Existing six-digit challenges/receipts retain their original ten-minute lifetime.
ALTER TABLE auth_reset_delivery ADD COLUMN otp_digits integer NOT NULL DEFAULT 6 CHECK(otp_digits IN (6,8));
ALTER TABLE auth_reset_delivery ALTER COLUMN otp_digits SET DEFAULT 8;
ALTER TABLE auth_account_notifications DROP CONSTRAINT auth_account_notifications_kind_check;
ALTER TABLE auth_account_notifications ADD CHECK(kind IN
 ('RecoveryCodeRegistered','RecoveryCodeReplaced','PasswordResetCompleted','ResetRequestsLimited','ResetProofFailuresLimited'));
INSERT INTO schema_versions(version) VALUES(10);
COMMIT;
