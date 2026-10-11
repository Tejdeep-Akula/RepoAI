# RepoAI

A repository-to-video application with code-grounded conversational chat.
Implementation follows [PHASES.md](PHASES.md), one approved phase at a time.

## Project structure

```text
RepoAI/
  RepoAI.slnx
  global.json
  compose.yaml
  .env.example
  src/
    RepoAI.Api/
      Contracts/
      Controllers/
      Errors/
    RepoAI.Application/
    RepoAI.Domain/
    RepoAI.Infrastructure/
    RepoAI.Worker/          .NET background job worker
  frontend/               React + TypeScript browser application
  tests/
    RepoAI.Api.Tests/
  RepoAI-design/
  PHASES.md
```

| Project | Responsibility | Project references |
| --- | --- | --- |
| Api | HTTP endpoints and startup/composition | Application, Infrastructure |
| Application | Use cases and interfaces | Domain |
| Domain | Business entities and rules | None |
| Infrastructure | Database, repository and model adapters | Application |
| Worker | Durable background job processing host | Application, Infrastructure |
| Frontend | Browser UI built with React; served/bundled by Vite | RepoAI.Api over HTTP |

## Frontend

The browser application uses React with TypeScript. Vite serves it during local
development and builds the production static files; it is tooling, not another
application framework. The Vite development server is pinned to
`http://localhost:5173` so its origin stays stable for Google sign-in and API
CORS configuration. The .NET API continues to use
`http://localhost:5080`.

From the repository root, start the frontend with:

```sh
cd frontend
npm install
npm run dev
```

The initial workspace displays API availability, provides a repository URL
syntax check, and indicates the Google sign-in setup status. URL validation
checks syntax only; it does not fetch or import a repository. Google Identity
Services now sends its ID token to the .NET sign-in endpoint; the returned
15-minute RepoAI access token is kept in browser memory only. The Google Client
Secret is not used. After sign-in, the workspace provides separate Repositories,
Conversations, and Videos destinations. Repositories open an individual
repository workspace with overview, analysis, conversations, and video sections.
Saved conversations are grouped by repository; selecting one opens a dedicated
chat page. The video pages are navigation placeholders until storyboard and video
generation are implemented. Project creation is available from the Repositories
page. The JWT is sent as a bearer token only for project API calls.

Local OAuth settings live in Git-ignored `frontend/.env.local` and
`src/RepoAI.Api/appsettings.Local.json`. Restart both the Vite dev server and
the .NET API after changing these files. To verify the production build, run
`npm run build` from `frontend/`.

Application contains use cases and persistence/job interfaces; Domain contains
the core entities; Infrastructure implements PostgreSQL persistence and repository
content storage. Domain does not depend on web or database frameworks. The separate
Worker process runs durable jobs.

## SDK and build

Use the .NET 10.0.401 SDK, or a newer patch in the same feature band.
`global.json` selects the SDK for this directory; .NET 8 remains installed for
other projects. Each `.csproj` declares `net10.0`, nullable checks and implicit
usings directly. `.slnx` is the solution format supported by .NET 10.

From the RepoAI root:

```sh
dotnet --version
dotnet build RepoAI.slnx --disable-build-servers -m:1
dotnet run --project src/RepoAI.Api --no-build --launch-profile http
```

The build command uses one MSBuild node and avoids persistent build servers to
keep the local build predictable. Phase 2 uses `Microsoft.AspNetCore.OpenApi`
10.0.12 for schema generation and `Swashbuckle.AspNetCore.SwaggerUI` 10.2.3 for
the interactive UI. The first build restores these packages from NuGet.

Open http://localhost:5080/swagger for interactive API testing.
Open http://localhost:5080/health for `Healthy` (HTTP 200).
Press Ctrl+C in the terminal to stop the API.

The current checked-in launch settings contain an `http` profile on port 5080.
HTTPS can be added to the launch settings separately; it is not enabled by this
profile. No certificate trust changes are needed for the Phase 2 HTTP profile.

The health endpoint checks process liveness only. It does not claim that a
database or AI model is available. This phase creates an API, not a frontend UI.
The launch profile is for local development. It does not configure production
TLS or automatic HTTP-to-HTTPS redirection.

## Phase 2: Swagger and API contracts

Swagger UI (`/swagger`) and the OpenAPI document (`/openapi/v1.json`) are exposed
only when the environment is `Development`, as set by the `http` launch profile.
Both routes return 404 in Production. The UI uses the built-in OpenAPI document;
there is no second schema generator.

| Endpoint | Purpose | Responses |
| --- | --- | --- |
| `GET /api/v1/status` | API availability | 200 JSON |
| `POST /api/v1/repositories/validate` | Stateless repository URL syntax validation | 200, 400, 415, 500 |
| `GET /` | Existing startup endpoint, alias of status | 200 JSON |
| `GET /health` | Process liveness | 200 text |

The versioned controller endpoints appear in Swagger under Status and
Repositories. The root alias and health middleware remain available separately.
This phase introduces no database, imports, Google sign-in, or AI services.

### Try a request in Swagger

1. Open `/swagger` and expand **POST /api/v1/repositories/validate**.
2. Select **Try it out** and enter:

   ```json
   {
     "repositoryUrl": "https://github.com/dotnet/aspnetcore"
   }
   ```

3. Select **Execute**. Expect HTTP 200 with:

   ```json
   {
     "repositoryUrl": "https://github.com/dotnet/aspnetcore",
     "host": "github.com",
     "reachabilityChecked": false
   }
   ```

4. Change the URL to `not-a-url` and execute again. Expect HTTP 400 with field
   errors, `code: "validation_failed"`, and a `traceId`.

The URL must be at most 2,048 characters, use HTTPS, contain a hostname and a
repository path, and exclude credentials, custom ports, queries and fragments.
Validation only checks syntax. It does **not** prove a repository exists, check
provider support or permissions, perform network requests, or protect a future
fetcher against SSRF. Import-time destination validation belongs to Phase 7.

### Error response convention

Errors use `application/problem+json`: `type`, `title`, `status`, `instance`,
`traceId`, and a stable `code`. Validation errors also contain an `errors`
dictionary. Malformed JSON and missing bodies return 400; unsupported content
types return 415; unknown routes return 404; incorrect methods return 405.
Unhandled failures are logged server-side and return a generic 500 response
without exception messages or stack traces, including in Development.

Keep HTTP DTOs in `Contracts`, routes in `Controllers`, and exception handling
in `Errors`. `[ApiController]` and DataAnnotations provide automatic request
validation. Add new use cases to Application when later phases require them.

## Phase 3: PostgreSQL and persistence (complete)

`compose.yaml` defines PostgreSQL 17 with a named data volume, localhost-only
published port and health check. EF Core/Npgsql is wired into the API, and the
initial User and Project entities, ownership relationship and migration are in
place. The initial migration has been applied to the local database. The API
applies pending migrations at startup. Verified on 6 October 2026: PostgreSQL
restarted without deleting its named volume; the initial migration record and
`users`/`projects` tables remained. The API then started, reported the database
was up to date, and `/health` returned `Healthy`. Project endpoints are now
implemented and require a valid RepoAI bearer token.

In Development, the API reads its connection string from
`src/RepoAI.Api/appsettings.json`. Keep any real local password private and do not
commit it. Outside Development, standard .NET configuration providers (including
environment variables in containers) can override configuration values.

### Settings

Before starting the database, copy `.env.example` to `.env` and set a unique
`REPOAI_DB_PASSWORD`. Keep the real `.env` out of Git; it is already ignored.
Do not run using `.env.example` as credentials. An empty password causes Compose
validation to fail. `.env` is a local plaintext settings file, not a secrets vault.

| Setting | Default / purpose |
| --- | --- |
| `REPOAI_DB_NAME` | `repoai` database |
| `REPOAI_DB_USER` | `repoai_admin` initial administrator |
| `REPOAI_DB_PASSWORD` | Required; no default password |
| `REPOAI_DB_PORT` | `5432` on your Mac; change if occupied |

The image creates this initial user with administrator privileges. This setup is
for local development; a limited application role will be considered when wiring
database access. Initialization settings apply to an empty volume. Changing the
password in `.env` later does not change an existing database user's password.

### Storage and networking

- PostgreSQL 17 stores its data at `/var/lib/postgresql/data`, mounted from the
  `pgdata` named volume. With the default Compose project name `repoai`, Docker
  names it `repoai_pgdata`.
- Recreating a container with the same project name and volume preserves its
  data. Changing the Compose project name can select a different volume.
- The published address is `127.0.0.1:5432` by default. A .NET app running on the
  Mac can connect there. The port is not published on the Mac's LAN interfaces;
  containers on the Compose network can reach `postgres:5432`.
- A volume is not a backup. Removing the volume removes the database files.
- The image is pinned to major version 17, not an immutable patch/digest. Review
  upgrades before pulling a new image; major-version upgrades need a migration
  plan, not just a changed image tag.

### Database setup and inspection

For Docker Compose database startup, create the ignored local `.env` from
`.env.example` and set a unique password. Validate the Compose configuration:

```sh
docker compose config --quiet
```

Start the database when you intend to use it:

```sh
docker compose up -d postgres
docker compose ps
```

The health check tests whether PostgreSQL accepts connections; it does not verify
application tables or the application password. The local database schema and
migration history were checked after a PostgreSQL restart with the named volume
retained. Phase 4's Project endpoints require a valid RepoAI bearer token. Phases
4 and 5 have been verified locally with Google sign-in and the authenticated
project UI.

`docker compose stop postgres` stops the database without deleting data.
`docker compose down` removes the project's containers/network but retains its
named volume. **Do not add `-v` unless you intend to delete the database volume.**

References: [official PostgreSQL image](https://hub.docker.com/_/postgres),
[Compose environment interpolation](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/).

## Phase 4: Google sign-in and access control (complete)

`POST /api/v1/auth/google` accepts a Google ID token, validates it for the
configured Google client ID, finds or creates the local user by the verified
Google `sub`, and returns a 15-minute RepoAI JWT. When authentication settings
are configured, the API validates RepoAI JWTs with the configured issuer,
audience, lifetime, signature and HS256 algorithm.
In Development without credentials, an ephemeral process-local key is used only
to reject bearer tokens; sign-in returns 503 until configured. Non-Development
startup requires a Google client ID and JWT key. No login sessions or refresh
tokens are stored.

| Endpoint | Purpose | Access |
| --- | --- | --- |
| `POST /api/v1/auth/google` | Verify Google ID token, create/find local user, issue JWT | Public |
| `POST /api/v1/projects` | Create a project for the caller | Bearer token required |
| `GET /api/v1/projects` | List only the caller's projects | Bearer token required |
| `GET /api/v1/projects/{projectId}` | Get one of the caller's projects | Bearer token required |

The Project API derives the owner from the JWT `sub` claim. It never accepts an
owner ID from the request. Reads include the owner ID in the database query, so a
project belonging to someone else returns 404. Project responses are DTOs and do
not expose EF entities.

The local user identity migration is
`src/RepoAI.Infrastructure/Persistence/Migrations/20261006132822_AddGoogleSubject.cs`;
its SQL script is alongside it. The API applies it at startup. The local user
table was empty when the non-null column migration was generated.

### Local authentication configuration

For local Development, copy
`src/RepoAI.Api/appsettings.Local.example.json` to
`src/RepoAI.Api/appsettings.Local.json` (the latter is Git-ignored). Set
`Authentication:Google:ClientId` to the Google Web OAuth client ID and set
`Authentication:Jwt:SigningKey` to a random value of at least 32 UTF-8 bytes.
Generate a suitable random value in a terminal with:

```sh
openssl rand -base64 32
```

Keep the generated value private. Do not commit `appsettings.Local.json`. The API
continues to read the Development database connection string only from
`appsettings.json`; this local authentication file cannot override it. Outside
Development, provide the corresponding settings through the deployment's secure
configuration mechanism (environment variable names use double underscores,
such as `Authentication__Jwt__SigningKey`).

Local Google OAuth settings are in Git-ignored
`src/RepoAI.Api/appsettings.Local.json` and `frontend/.env.local` files. Project
routes reject requests without a valid bearer token. “Chat sessions” remain
saved conversations, not server-side login-session records.

## Phase 6: Durable background processing (complete)

The API and a separate .NET Worker share a PostgreSQL-backed job queue. Start
the API (which applies EF migrations) and Worker from the repository root in
separate terminals:

```sh
dotnet run --project src/RepoAI.Api --launch-profile http
dotnet run --project src/RepoAI.Worker
```

The Worker reads the local database connection string from the API's
`appsettings.json` / Git-ignored `appsettings.Local.json`, or from
`ConnectionStrings__RepoAI`. The development-only demo endpoint queues a
progress-reporting job for one of the signed-in user's projects:

| Endpoint | Purpose |
| --- | --- |
| `POST /api/v1/projects/{projectId}/jobs/demo` | Queue a development-only progress job; `Idempotency-Key` prevents duplicate enqueue |
| `GET /api/v1/jobs/{jobId}` | Read job status, progress and attempt count |
| `DELETE /api/v1/jobs/{jobId}` | Request cancellation |

Send `{}` for a normal demo (about 10 seconds), or
`{"failFirstAttempt": true}` to verify retry behavior. Its `demo.progress`
handler is only a queue smoke test; it does not clone, fetch or analyze a
repository. The API hides this endpoint outside Development. Jobs persist after
process restarts; workers use expiring leases and PostgreSQL `SKIP LOCKED` claims
so multiple worker processes do not claim the same row concurrently.

API requests and worker lifecycle events are written as structured console logs.
EF Core SQL command logs are reduced to warnings so they do not drown out useful
diagnostics. Failed jobs also retain a safe `failureMessage` alongside
`failureCode`; read it from `GET /api/v1/jobs/{jobId}`. Stack traces remain in
the Worker console and are not returned by the API.

## Phase 7: Public GitHub import and architecture overview (extended)

Repository imports currently support public `github.com` owner/repository URLs.
The API queues imports as durable jobs. The Worker resolves the requested ref—or
the repository's default branch—to a commit SHA and downloads that fixed commit's
archive. Redirects are rejected, archive paths are checked before use, and
repository code is never executed.

| Endpoint | Purpose |
| --- | --- |
| `POST /api/v1/projects/{projectId}/imports` | Queue an import; optional JSON `{"ref":"main"}` and optional `Idempotency-Key` |
| `GET /api/v1/jobs/{jobId}` | Poll import status and progress |
| `GET /api/v1/projects/{projectId}/snapshots` | List the project's imported commit snapshots |
| `GET /api/v1/projects/{projectId}/snapshots/{snapshotId}/files?page=1&pageSize=100` | Browse the snapshot's paginated file inventory |
| `POST /api/v1/projects/{projectId}/snapshots/{snapshotId}/analysis` | Queue static architecture analysis; returns a durable job |
| `GET /api/v1/projects/{projectId}/snapshots/{snapshotId}/architecture` | Retrieve the saved architecture report after analysis succeeds |
All endpoints require the signed-in user's bearer token and enforce project
ownership. The API accepts imports only for public GitHub repository URLs; other
hosts and nested repository paths are rejected.

`repository_snapshots` and `repository_files` store the commit and complete file
inventory metadata in PostgreSQL. Small text-file bytes are kept outside the
database under the ignored local `data/repository-content` directory. Binary
content is not stored; those files remain in the inventory with path, size,
detected type and SHA-256. Unknown extensions are still inventoried and labeled
best-effort. Files above the text-file limit receive `file_size_limit`; configured
archive, expanded-size, and file-count limits fail the job with a visible
`FailureCode` rather than silently producing a partial snapshot.

Defaults are 100 MiB compressed archive, 500 MiB expanded contents, 20,000 files,
and 5 MiB per stored text file. The Worker accepts overrides through
`RepositoryImport__StorageRoot`, `RepositoryImport__MaxArchiveBytes`,
`RepositoryImport__MaxExpandedBytes`, `RepositoryImport__MaxTextFileBytes`, and
`RepositoryImport__MaxFiles`. The browser project workflow can queue an import,
poll job status and progress, show retry/failure details, list snapshots, and browse
paginated file metadata. Its architecture overview groups files by component
folders (including project folders below common `src`, `apps`, and `services`
directories), summarizes detected languages/formats, and highlights likely entry
points and build/dependency manifests with paths as evidence. Supported
manifests are parsed for declared dependencies across npm, Composer, Python,
Go, Cargo, Maven, and .NET project formats. These are observations about what a
manifest declares, not proof of runtime use. Each finding is labeled as an
observation or inference, includes method and confidence, and cites source paths,
with line references where the format parser can provide them.
The report also visualizes component-to-package declarations and .NET project
references as typed graph edges, each retaining its manifest evidence. It parses
Docker Compose `depends_on` declarations into service nodes and startup dependency
edges. C# source is parsed with Roslyn syntax trees. JavaScript/TypeScript, Python,
Go, Java, Rust, Ruby, PHP, C/C++, Scala, Haskell, Julia, Agda, and QL use
Tree-sitter syntax trees for selected HTTP, database, and broker patterns; F#,
VB, Kotlin, and Swift still use the line-based fallback. Each edge carries its
type, confidence, and evidence path/line. The directed, typed node/edge graph is
serialized in the versioned architecture report in PostgreSQL, not stored in a
separate graph database. Static relationships are not proof that code executes or
traffic occurs. Dynamic URLs, indirect calls, wrappers, and unsupported patterns
can be missed; this is not a complete call graph or runtime data-flow trace.
Analysis runs as a durable Worker job; its versioned JSON report is saved against
the immutable snapshot commit and can be fetched after job completion.

## Phase 8: Repository chat with Ollama

| Endpoint | Purpose |
| --- | --- |
| `POST /api/v1/projects/{projectId}/conversations` | Create a saved chat for an owned snapshot; optional title |
| `GET /api/v1/projects/{projectId}/conversations` | List the signed-in user's saved chats for a project |
| `GET /api/v1/projects/{projectId}/conversations/{conversationId}` | Reopen a chat and retrieve its message history |
| `POST /api/v1/projects/{projectId}/conversations/{conversationId}/messages/stream` | Ask a question; receive Server-Sent Events with answer deltas and citations |

All routes require the signed-in user's bearer token and enforce project ownership.

The .NET API calls Ollama's HTTP service through the `OllamaSharp` NuGet client.
Ollama runs separately and manages the model weights; install/run it, then
download the configured coding model once:

```sh
ollama pull qwen2.5-coder:7b
```

The default Ollama endpoint is `http://localhost:11434`, and the default model
is `qwen2.5-coder:7b` (Qwen2.5-Coder's 7B release uses Apache 2.0). Change either
using `Chat:Ollama:Endpoint` and `Chat:Ollama:Model` configuration (for example,
environment variables `Chat__Ollama__Endpoint` and `Chat__Ollama__Model`). A
smaller compatible model can be selected when local memory is limited. The API
does not start Ollama, pull model files, or package them into the API image. If
the API runs in a container while Ollama runs on the host, configure the endpoint
to the host address reachable from that container (commonly
`http://host.docker.internal:11434`). The configured Ollama endpoint receives
retrieved repository excerpts; use a local endpoint if code must stay on the
development machine.

To use chat, import a repository and select a completed snapshot in its repository
workspace. Start a conversation tied to that immutable snapshot, then ask questions.
The API retrieves bounded text excerpts from the snapshot's stored files,
provides them as untrusted context to the model, streams the answer, and stores
the user and assistant messages. Assistant messages retain their model ID and
source citations (`path:startLine-endLine`); conversations are project-owner
scoped. Follow-up prompts include recent messages from the same conversation.
Retrieval is lexical and bounded (candidate/file/byte/chunk limits), so answers
can miss relevant code; citations should be checked against source. Repository
files are never executed by this integration.

## Verification

```sh
dotnet test RepoAI.slnx --disable-build-servers -m:1
```

The integration tests use the actual ASP.NET Core pipeline through
`WebApplicationFactory`. They check valid and invalid URLs, field length,
malformed/empty bodies, content types, 404/405 responses, safe exception handling,
Swagger/schema availability and production restrictions. An exception-throwing
controller is loaded only by the test host, not the shipped application.

Verified on 30 September 2026: build passed with zero warnings/errors; all 23 tests
passed. The Swagger HTML and OpenAPI routes were checked programmatically;
interactive browser clicks were not automated. Test-runner local sockets require
permission when running through a restricted agent sandbox.

## VS Code

Open this root folder and use the build and run commands above in the integrated
terminal. Project-specific `.vscode` configuration is not included.

## Later phases

Phases 1–8 are complete, including the extended best-effort static architecture
analysis and Ollama-backed, evidence-grounded repository chat. Storyboarding and
LTX video generation follow. The architecture graph is evidence-backed static
analysis, not a complete runtime call graph. No login-session table is introduced.
