START TRANSACTION;
ALTER TABLE users ADD "GoogleSubject" character varying(255) NOT NULL;

CREATE UNIQUE INDEX "IX_users_GoogleSubject" ON users ("GoogleSubject");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006132822_AddGoogleSubject', '10.0.12');

COMMIT;
