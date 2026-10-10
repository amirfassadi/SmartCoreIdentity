-- Forward-only migration. Run with a migration role, not the HTTP runtime role.
BEGIN;
CREATE TABLE IF NOT EXISTS schema_versions(version integer PRIMARY KEY, installed_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS verification_sessions (
 id text PRIMARY KEY, idempotency_key text NOT NULL UNIQUE,
 request_mac bytea, binding_mac bytea, code_mac bytea,
 contact_kind text NOT NULL CHECK (contact_kind IN ('email','mobile')),
 contact text NOT NULL, display_name text NOT NULL,
 sealed_password bytea, expires_at timestamptz NOT NULL,
 created_at timestamptz NOT NULL, last_sent_at timestamptz NOT NULL,
 attempts integer NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 5),
 resends integer NOT NULL DEFAULT 0 CHECK(resends BETWEEN 0 AND 3),
 registration_id uuid, invalidated boolean NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS verification_contact_time ON verification_sessions(contact, created_at);
CREATE TABLE IF NOT EXISTS persons (
 id uuid PRIMARY KEY, contact_kind text NOT NULL CHECK(contact_kind IN ('email','mobile')),
 contact text NOT NULL UNIQUE, display_name text NOT NULL,
 status text NOT NULL CHECK(status='Active'), created_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS organizations (
 id uuid PRIMARY KEY, name text NOT NULL,
 category text NOT NULL CHECK(category='Personal'), status text NOT NULL CHECK(status='Active')
);
CREATE TABLE IF NOT EXISTS memberships (
 id uuid PRIMARY KEY, person_id uuid NOT NULL REFERENCES persons(id),
 organization_id uuid NOT NULL REFERENCES organizations(id),
 role text NOT NULL CHECK(role='Owner'), status text NOT NULL CHECK(status='Active'),
 UNIQUE(person_id, organization_id)
);
CREATE TABLE IF NOT EXISTS registrations (
 id uuid PRIMARY KEY, person_id uuid NOT NULL UNIQUE REFERENCES persons(id),
 organization_id uuid NOT NULL REFERENCES organizations(id), membership_id uuid NOT NULL REFERENCES memberships(id),
 status text NOT NULL DEFAULT 'PendingCredential' CHECK(status IN ('PendingCredential','Ready')),
 ownership_committed_at timestamptz NOT NULL, ready_at timestamptz,
 ready_fact_id uuid UNIQUE, winner_id uuid, provisioning_version uuid,
 sealed_password bytea, material_expires_at timestamptz NOT NULL,
 CHECK ((status='PendingCredential' AND ready_at IS NULL AND ready_fact_id IS NULL)
 OR (status='Ready' AND ready_at IS NOT NULL AND ready_fact_id IS NOT NULL AND winner_id IS NOT NULL AND provisioning_version IS NOT NULL))
);
-- Credential boundary: separate local transactions even when hosted in one database.
CREATE TABLE IF NOT EXISTS credentials (
 id uuid PRIMARY KEY, person_id uuid NOT NULL UNIQUE REFERENCES persons(id),
 password_hash text NOT NULL, status text NOT NULL CHECK(status='Active')
);
CREATE TABLE IF NOT EXISTS initial_credential_winners (
 registration_id uuid PRIMARY KEY REFERENCES registrations(id),
 person_id uuid NOT NULL UNIQUE REFERENCES persons(id), credential_id uuid NOT NULL REFERENCES credentials(id),
 operation_id uuid NOT NULL UNIQUE, provisioning_version uuid NOT NULL,
 phase text NOT NULL CHECK(phase IN ('ProvisionedAwaitingReady','ReadyAcknowledged')),
 acknowledged_fact_id uuid, CHECK((phase='ReadyAcknowledged')=(acknowledged_fact_id IS NOT NULL))
);
CREATE TABLE IF NOT EXISTS workflow_jobs (
 registration_id uuid PRIMARY KEY REFERENCES registrations(id),
 attempts integer NOT NULL DEFAULT 0, next_attempt_at timestamptz NOT NULL,
 completed boolean NOT NULL DEFAULT false, recovery_needed boolean NOT NULL DEFAULT false,
 last_error text
);
CREATE TABLE IF NOT EXISTS event_outbox (
 id uuid PRIMARY KEY, registration_id uuid NOT NULL REFERENCES registrations(id),
 event_type text NOT NULL, payload jsonb NOT NULL,
 occurred_at timestamptz NOT NULL, published_at timestamptz,
 UNIQUE(registration_id,event_type)
);
-- No broker dispatcher in phase 1: unpublished facts are retained durably.
CREATE TABLE IF NOT EXISTS delivery_outbox (
 id uuid PRIMARY KEY, verification_id text NOT NULL REFERENCES verification_sessions(id),
 sealed_code bytea, created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
 delivered_at timestamptz
);
INSERT INTO schema_versions(version) VALUES(1) ON CONFLICT DO NOTHING;
COMMIT;
