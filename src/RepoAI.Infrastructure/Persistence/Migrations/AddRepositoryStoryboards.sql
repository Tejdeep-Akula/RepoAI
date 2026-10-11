CREATE TABLE repository_storyboards (
    "Id" uuid NOT NULL,
    "SnapshotId" uuid NOT NULL,
    "CommitSha" character varying(40) NOT NULL,
    "Model" character varying(128) NOT NULL,
    "StoryboardJson" jsonb NOT NULL,
    "GeneratedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_repository_storyboards" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_repository_storyboards_repository_snapshots_SnapshotId"
        FOREIGN KEY ("SnapshotId") REFERENCES repository_snapshots ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_repository_storyboards_SnapshotId"
    ON repository_storyboards ("SnapshotId");
