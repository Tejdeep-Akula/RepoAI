BEGIN;

ALTER TABLE background_jobs
    ADD COLUMN "FailureMessage" character varying(2000);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011090000_AddBackgroundJobFailureDetails', '10.0.12');

COMMIT;
