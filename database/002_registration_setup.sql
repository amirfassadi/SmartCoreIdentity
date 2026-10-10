-- Forward migration: separate setup proof and accepted candidate from registration proof.
BEGIN;
ALTER TABLE verification_sessions ADD COLUMN IF NOT EXISTS setup_registration_id uuid REFERENCES registrations(id);
CREATE TABLE IF NOT EXISTS setup_challenges (
 id text PRIMARY KEY, verification_id text NOT NULL UNIQUE REFERENCES verification_sessions(id),
 registration_id uuid NOT NULL REFERENCES registrations(id), operation_id uuid NOT NULL UNIQUE,
 binding_mac bytea, code_mac bytea, expires_at timestamptz NOT NULL,
 created_at timestamptz NOT NULL, attempts integer NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 5),
 accepted_at timestamptz, idempotency_key text, request_mac bytea,
 sealed_password bytea, material_expires_at timestamptz,
 CHECK ((accepted_at IS NULL AND idempotency_key IS NULL AND request_mac IS NULL)
 OR (accepted_at IS NOT NULL AND idempotency_key IS NOT NULL)),
 CHECK (sealed_password IS NULL OR (accepted_at IS NOT NULL AND material_expires_at IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS setup_candidate_registration ON setup_challenges(registration_id,accepted_at);
CREATE TABLE IF NOT EXISTS setup_delivery_outbox (
 id uuid PRIMARY KEY, setup_id text NOT NULL UNIQUE REFERENCES setup_challenges(id),
 sealed_code bytea, created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL
);
INSERT INTO schema_versions(version) VALUES(2) ON CONFLICT DO NOTHING;
COMMIT;
