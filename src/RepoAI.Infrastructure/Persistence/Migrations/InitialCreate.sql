CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE users (
    "Id" uuid NOT NULL,
    "DisplayName" character varying(200) NOT NULL,
    "Email" character varying(320),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_users" PRIMARY KEY ("Id")
);

CREATE TABLE projects (
    "Id" uuid NOT NULL,
    "OwnerId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "RepositoryUrl" character varying(2048) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_projects" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_projects_users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES users ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_projects_OwnerId" ON projects ("OwnerId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005142008_InitialCreate', '10.0.12');

COMMIT;
