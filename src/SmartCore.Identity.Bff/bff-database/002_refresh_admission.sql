BEGIN;
ALTER TABLE bff_refresh_journal ADD COLUMN retry_not_before timestamptz;
ALTER TABLE bff_refresh_journal ADD COLUMN admission_rejections integer NOT NULL DEFAULT 0 CHECK(admission_rejections BETWEEN 0 AND 6);
INSERT INTO bff_schema_versions(version) VALUES(2);
COMMIT;
