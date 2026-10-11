CREATE TABLE chat_conversations (
    "Id" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "SnapshotId" uuid NOT NULL,
    "Title" character varying(160) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_chat_conversations" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_chat_conversations_title" CHECK (length(trim("Title")) > 0),
    CONSTRAINT "FK_chat_conversations_projects_ProjectId"
        FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_chat_conversations_repository_snapshots_SnapshotId"
        FOREIGN KEY ("SnapshotId") REFERENCES repository_snapshots ("Id") ON DELETE CASCADE
);

CREATE TABLE chat_messages (
    "Id" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "Role" character varying(16) NOT NULL,
    "Content" text NOT NULL,
    "Model" character varying(128),
    "CitationsJson" jsonb,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_chat_messages" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_chat_messages_role" CHECK ("Role" IN ('user', 'assistant')),
    CONSTRAINT "CK_chat_messages_content" CHECK (length(trim("Content")) > 0),
    CONSTRAINT "FK_chat_messages_chat_conversations_ConversationId"
        FOREIGN KEY ("ConversationId") REFERENCES chat_conversations ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_chat_conversations_ProjectId_UpdatedAtUtc"
    ON chat_conversations ("ProjectId", "UpdatedAtUtc");
CREATE INDEX "IX_chat_conversations_SnapshotId"
    ON chat_conversations ("SnapshotId");
CREATE INDEX "IX_chat_messages_ConversationId_CreatedAtUtc"
    ON chat_messages ("ConversationId", "CreatedAtUtc");
