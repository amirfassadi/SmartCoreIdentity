BEGIN;
ALTER TABLE bff_sessions ADD COLUMN sealed_logout_access bytea, ADD COLUMN logout_attempts integer NOT NULL DEFAULT 0 CHECK(logout_attempts>=0), ADD COLUMN logout_retry_at timestamptz;
ALTER TABLE bff_sessions DROP CONSTRAINT bff_sessions_check1;
ALTER TABLE bff_sessions ADD CHECK(
 (status='Active' AND sealed_tokens IS NOT NULL AND sealed_logout_access IS NULL)
 OR (status='LogoutRequested' AND ((sealed_tokens IS NOT NULL)<>(sealed_logout_access IS NOT NULL)))
 OR (status IN ('Closed','ReauthenticationRequired') AND sealed_tokens IS NULL AND sealed_logout_access IS NULL));
ALTER TABLE bff_refresh_journal ADD COLUMN probe_dispatch boolean NOT NULL DEFAULT false, ADD COLUMN probe_loss_recorded boolean NOT NULL DEFAULT false, ADD COLUMN probe_uncertain boolean NOT NULL DEFAULT true;
CREATE INDEX bff_logout_pending ON bff_sessions(logout_retry_at,expires_at,id) WHERE status='LogoutRequested';
CREATE INDEX bff_sample_retention ON bff_refresh_samples(observed_at,session_id);
INSERT INTO bff_schema_versions VALUES(5);
COMMIT;
