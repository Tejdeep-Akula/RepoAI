BEGIN;

DO $EF$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20261011135522_AddRepositoryArchitectureReports'
    ) THEN
        CREATE TABLE repository_architecture_reports (
            "Id" uuid NOT NULL,
            "SnapshotId" uuid NOT NULL,
            "CommitSha" character varying(40) NOT NULL,
            "AnalyzerVersion" character varying(64) NOT NULL,
            "ReportJson" jsonb NOT NULL,
            "GeneratedAtUtc" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_repository_architecture_reports" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_repository_architecture_reports_repository_snapshots_Snapsh~"
                FOREIGN KEY ("SnapshotId") REFERENCES repository_snapshots ("Id") ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX "IX_repository_architecture_reports_SnapshotId"
            ON repository_architecture_reports ("SnapshotId");

        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20261011135522_AddRepositoryArchitectureReports', '10.0.12');
    END IF;
END $EF$;

COMMIT;
