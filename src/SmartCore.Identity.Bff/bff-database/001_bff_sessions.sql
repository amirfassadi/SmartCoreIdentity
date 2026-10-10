BEGIN;
CREATE TABLE bff_sessions (
 id uuid PRIMARY KEY, cookie_mac bytea NOT NULL UNIQUE CHECK(octet_length(cookie_mac)=32),
 identity_session_id uuid NOT NULL, version bigint NOT NULL CHECK(version>=0),
 status text NOT NULL CHECK(status IN ('Active','Closed','ReauthenticationRequired')),
 sealed_tokens bytea, access_issued_at timestamptz NOT NULL, access_expires_at timestamptz NOT NULL,
 expires_at timestamptz NOT NULL, last_foreground_at timestamptz NOT NULL,
 completed_attempt_id uuid, uncertain_at timestamptz,
 CHECK(access_expires_at<=expires_at AND access_expires_at>access_issued_at),
 CHECK((status='Active')=(sealed_tokens IS NOT NULL))
);
CREATE TABLE bff_refresh_journal (
 session_id uuid PRIMARY KEY REFERENCES bff_sessions(id),
 attempt_id uuid, source_version bigint, first_dispatched_at timestamptz, last_dispatched_at timestamptz,
 CHECK((attempt_id IS NULL)=(source_version IS NULL)), CHECK(source_version>=0)
);
CREATE FUNCTION bff_session_monotonic() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.id<>OLD.id OR NEW.cookie_mac<>OLD.cookie_mac OR NEW.identity_session_id<>OLD.identity_session_id
 OR NEW.expires_at<>OLD.expires_at OR NEW.version<OLD.version OR NEW.last_foreground_at<OLD.last_foreground_at
 OR (OLD.status<>'Active' AND NEW.status<>OLD.status) THEN RAISE EXCEPTION 'immutable BFF session evidence' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER bff_preserve_session BEFORE UPDATE ON bff_sessions FOR EACH ROW EXECUTE FUNCTION bff_session_monotonic();
CREATE TABLE bff_schema_versions(version integer PRIMARY KEY);
INSERT INTO bff_schema_versions VALUES(1);
COMMIT;
