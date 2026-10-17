-- 003 has already been applied in disposable validation databases: preserve migration history.
BEGIN;
CREATE OR REPLACE FUNCTION auth_preserve_session() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.id,NEW.person_id,NEW.issuance_epoch,NEW.client_id,NEW.created_at,NEW.expires_at)
   IS DISTINCT FROM (OLD.id,OLD.person_id,OLD.issuance_epoch,OLD.client_id,OLD.created_at,OLD.expires_at)
   OR NEW.last_foreground_refresh_at<OLD.last_foreground_refresh_at
   OR (OLD.status<>'Active' AND (NEW.status,NEW.closed_at,NEW.close_reason)
       IS DISTINCT FROM (OLD.status,OLD.closed_at,OLD.close_reason)) THEN
   RAISE EXCEPTION 'Immutable authentication state' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION auth_preserve_family() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.id,NEW.session_id,NEW.expires_at) IS DISTINCT FROM (OLD.id,OLD.session_id,OLD.expires_at)
   OR NEW.current_generation<OLD.current_generation
   OR (OLD.status='Revoked' AND (NEW.status,NEW.revoked_at) IS DISTINCT FROM (OLD.status,OLD.revoked_at)) THEN
   RAISE EXCEPTION 'Immutable authentication state' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION auth_preserve_generation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF (NEW.family_id,NEW.generation,NEW.verifier,NEW.key_id,NEW.issued_at,NEW.recognition_until)
   IS DISTINCT FROM (OLD.family_id,OLD.generation,OLD.verifier,OLD.key_id,OLD.issued_at,OLD.recognition_until)
   OR (OLD.status='Consumed' AND (NEW.status,NEW.consumed_at) IS DISTINCT FROM (OLD.status,OLD.consumed_at)) THEN
   RAISE EXCEPTION 'Immutable authentication state' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TABLE auth_refresh_reuse_observations (
 event_id uuid PRIMARY KEY REFERENCES auth_refresh_security_events(id),
 milliseconds_since_consumption bigint NOT NULL CHECK(milliseconds_since_consumption>=0)
);
CREATE TABLE auth_domain_event_outbox (
 id uuid PRIMARY KEY, person_id uuid REFERENCES persons(id), session_id uuid REFERENCES auth_sessions(id),
 event_type text NOT NULL CHECK(event_type IN ('LoginSucceeded','LoginFailed','SessionCreated','LogoutCompleted','SessionExpired')),
 payload jsonb NOT NULL, occurred_at timestamptz NOT NULL, published_at timestamptz,
 UNIQUE(session_id,event_type)
);
INSERT INTO schema_versions(version) VALUES(4);
COMMIT;
