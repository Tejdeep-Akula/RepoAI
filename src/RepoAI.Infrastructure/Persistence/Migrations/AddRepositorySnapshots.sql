START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009135311_AddRepositorySnapshots') THEN
    CREATE TABLE repository_snapshots (
        "Id" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "RepositoryUrl" character varying(2048) NOT NULL,
        "CommitSha" character varying(40) NOT NULL,
        "ResolvedRef" character varying(255) NOT NULL,
        "DefaultBranch" character varying(255) NOT NULL,
        "FileCount" integer NOT NULL,
        "TextFileCount" integer NOT NULL,
        "BinaryFileCount" integer NOT NULL,
        "SkippedFileCount" integer NOT NULL,
        "ImportedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_repository_snapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_repository_snapshots_counts" CHECK ("FileCount" >= 0 AND "TextFileCount" >= 0 AND "BinaryFileCount" >= 0 AND "SkippedFileCount" >= 0),
        CONSTRAINT "FK_repository_snapshots_projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009135311_AddRepositorySnapshots') THEN
    CREATE TABLE repository_files (
        "Id" uuid NOT NULL,
        "SnapshotId" uuid NOT NULL,
        "RelativePath" character varying(4096) NOT NULL,
        "DetectedLanguage" character varying(100),
        "SizeBytes" bigint NOT NULL,
        "Sha256" character(64) NOT NULL,
        "IsBinary" boolean NOT NULL,
        "ContentStorageKey" character varying(512),
        "SkipReason" character varying(100),
        CONSTRAINT "PK_repository_files" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_repository_files_size" CHECK ("SizeBytes" >= 0),
        CONSTRAINT "FK_repository_files_repository_snapshots_SnapshotId" FOREIGN KEY ("SnapshotId") REFERENCES repository_snapshots ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009135311_AddRepositorySnapshots') THEN
    CREATE INDEX "IX_repository_files_SnapshotId_DetectedLanguage" ON repository_files ("SnapshotId", "DetectedLanguage");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009135311_AddRepositorySnapshots') THEN
    CREATE INDEX "IX_repository_snapshots_ProjectId_ImportedAtUtc" ON repository_snapshots ("ProjectId", "ImportedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009135311_AddRepositorySnapshots') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261009135311_AddRepositorySnapshots', '10.0.12');
    END IF;
END $EF$;
COMMIT;
