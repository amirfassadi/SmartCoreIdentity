BEGIN;
CREATE TABLE bff_refresh_circuit(id integer PRIMARY KEY CHECK(id=1),opened_at timestamptz,reason text);
INSERT INTO bff_refresh_circuit(id) VALUES(1);
CREATE TABLE bff_refresh_samples(session_id uuid PRIMARY KEY,observed_at timestamptz NOT NULL,status integer NOT NULL);
INSERT INTO bff_schema_versions VALUES(3);
COMMIT;
