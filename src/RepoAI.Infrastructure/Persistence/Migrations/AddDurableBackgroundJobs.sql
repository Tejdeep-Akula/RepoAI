START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009125723_AddDurableBackgroundJobs') THEN
    CREATE TABLE background_jobs (
        "Id" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "Kind" character varying(100) NOT NULL,
        "PayloadJson" jsonb NOT NULL,
        "Status" character varying(32) NOT NULL,
        "ProgressPercentage" integer NOT NULL,
        "AttemptCount" integer NOT NULL,
        "MaxAttempts" integer NOT NULL,
        "IdempotencyKey" character varying(128),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "AvailableAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        "StartedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "LeaseOwner" character varying(200),
        "LeaseExpiresAtUtc" timestamp with time zone,
        "FailureCode" character varying(100),
        CONSTRAINT "PK_background_jobs" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_background_jobs_attempts" CHECK ("AttemptCount" >= 0 AND "MaxAttempts" > 0),
        CONSTRAINT "CK_background_jobs_progress" CHECK ("ProgressPercentage" >= 0 AND "ProgressPercentage" <= 100),
        CONSTRAINT "FK_background_jobs_projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009125723_AddDurableBackgroundJobs') THEN
    CREATE UNIQUE INDEX "IX_background_jobs_ProjectId_IdempotencyKey" ON background_jobs ("ProjectId", "IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009125723_AddDurableBackgroundJobs') THEN
    CREATE INDEX "IX_background_jobs_Status_AvailableAtUtc_CreatedAtUtc" ON background_jobs ("Status", "AvailableAtUtc", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009125723_AddDurableBackgroundJobs') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261009125723_AddDurableBackgroundJobs', '10.0.12');
    END IF;
END $EF$;
COMMIT;
