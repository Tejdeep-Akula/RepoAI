# RepoAI implementation phases

This document is our reference for building RepoAI incrementally. These phases
describe the planned order; they do not authorize implementation. Before each
step, we will explain the proposed changes and obtain your approval. A phase may
contain several separately approved steps.

Phases 1–9 are **Complete** (Phase 7 includes the extended static architecture
analysis scope). Phase 10 is **In progress**. Phase 11's container implementation
is **verified**; Docker Hub publication remains pending an approved namespace and
version.
The existing HLD remains in `RepoAI-design/`.

## Phase 1 — Project foundation

**Status:** Complete — 28 September 2026.

Create the .NET 10 solution, agree on project boundaries, document local commands,
and establish a minimal runnable API and build process.

**Complete when:** the solution builds and the API runs locally with a health endpoint.

**Delivered:** .NET 10 SDK selection, a solution containing Api, Application,
Domain and Infrastructure projects, project references, per-project build settings,
local build/run instructions in `README.md`.
The Worker process is deferred to Phase 6.

**Verified:** SDK 10.0.401; solution build succeeded with zero warnings and errors;
`GET /` returned HTTP 200 with the RepoAI startup response; `GET /health` returned
HTTP 200 and `Healthy`. HTTP smoke checks used port 5082 outside the tool sandbox;
the normal development launch profile uses port 5080.

**Update — 29 September 2026:** optional `.vscode` configuration removed at the
user's request. Terminal build/run commands remain available.

**Update — 29 September 2026:** framework, nullable and implicit-using settings
moved into each `.csproj`; `Directory.Build.props` removed. `global.json` retained.

## Phase 2 — API contracts and Swagger

**Status:** Complete — 30 September 2026.

Add OpenAPI documentation and Swagger UI for interactive API testing. Establish
consistent request validation, error responses, and API organization.

**Complete when:** approved endpoints can be explored and tested through Swagger,
including success and validation-error responses.

**Delivered:** Development-only Swagger UI and built-in OpenAPI, versioned status
and stateless repository-URL validation endpoints, controller/contract organization,
automatic request validation, and Problem Details responses with error codes and
trace IDs. The existing root and health endpoints are preserved. This phase does
not fetch or persist repositories.

**Verified:** solution build passed with zero warnings/errors; 23 integration
tests passed, covering success, invalid/missing/malformed input, field limits,
unsupported media types, 404/405 errors, safe 500 responses, Swagger/OpenAPI
availability, and production documentation restrictions. Swagger HTML/schema were
checked programmatically; interactive browser clicks were not automated.

**Documentation:** `README.md` includes Swagger examples, endpoint/error contracts
and test commands. Run instructions now match the existing HTTP-only launch profile.

## Phase 3 — PostgreSQL and persistence

**Status:** Complete — 6 October 2026.

**Delivered:** PostgreSQL 17 Compose configuration with a persistent named volume,
localhost-only published port and health check; `.env.example`; EF Core/Npgsql
configuration; initial User and Project entities and ownership relationship; an
EF Core design-time context factory; initial migration and SQL script. The initial
migration has been applied to the local database. In Development, the API reads
its connection string from `appsettings.json`; container deployments use normal
configuration overrides. The API applies pending migrations at startup.

**Complete when:** the API can start with the database, apply pending migrations,
and the resulting schema/migration history remains present after API and database
container restarts that retain the named volume. Each implementation step still
requires approval.

**Verified — 6 October 2026:** PostgreSQL restarted without deleting its named
volume and returned healthy. The `__EFMigrationsHistory` table retained
`20261005142008_InitialCreate`, and the `users` and `projects` tables remained.
Afterward, the API started, reported that the database was already up to date,
and `GET /health` returned `Healthy`.

**Next:** Configure a Google Web OAuth client and local JWT key, then verify
Google sign-in and owner-scoped project access end to end.

## Phase 4 — Google sign-in and access control

**Status:** Complete — Google sign-in and owner-scoped projects verified locally.

Implement Google sign-in through OAuth 2.0/OpenID Connect, map Google identities
to local users, and enforce project access permissions. After authentication is
available, add the first Project API persistence flow: accept and return API DTOs,
persist through the application/infrastructure layers, and restrict reads and
writes to the owning user. Do not expose EF entities directly.

**Delivered so far:** `User.GoogleSubject`, a required unique identifier column
and index, and the `AddGoogleSubject` EF migration plus SQL script. The local
users table was empty when this migration was created; the API applies it at
startup. The API now has `POST /api/v1/auth/google`, which verifies a Google ID
token against the configured client ID, finds or creates the local user by the
verified `sub`, and issues a 15-minute stateless JWT. If Development has no JWT
key configured, bearer validation uses a temporary process-local key and sign-in
remains unavailable. Non-Development requires the real Google client ID and JWT
signing key. No
login-session or refresh-token records are stored. Authorized Project endpoints support
create, list and get-by-ID; project ownership is taken from the JWT and included
in every project query. A caller cannot supply or change `OwnerId`, and another
user's project is returned as not found.

“Chat sessions” means saved conversations; it does not require a login-session table.

**Complete when:** users can sign in, create and retrieve their own persisted
projects, while unauthenticated and cross-user access are rejected.

## Phase 5 — Web application workspace

**Status:** Complete — authenticated project list, create, and delete flows are implemented.

Use React with TypeScript for the browser UI and Vite for local development and
production builds. The local Vite origin is pinned to `http://localhost:5173`;
the .NET API remains at `http://localhost:5080`.

**Delivered so far:** Vite React + TypeScript app in `frontend/`, strict
development port configuration, responsive RepoAI workspace shell, API status
indicator, and repository URL syntax validation wired to the existing API.
Development CORS accepts only `http://localhost:5173`. Google Identity Services
is wired to `POST /api/v1/auth/google`; the RepoAI bearer token remains in
browser memory and is not persisted. The Google Client Secret is not used.
After sign-in, the workspace loads only the caller's projects using the bearer
token and supports creating projects with a name and HTTPS repository URL. Users
can delete an owned project from the repository list or workspace after
confirming that its repository data, generated results, conversations, and jobs
will be removed. The API rejects deletion while a background job is active.
Project loading, empty, error, retry, create, and delete states are shown in the UI.

**Verified:** frontend production build and lint pass. CORS tests cover allowed
and disallowed origins. Google sign-in was verified locally with the API,
PostgreSQL, and the OAuth client configured for the frontend origin.

**Complete when:** an authenticated user can manage their own projects through the UI.

## Phase 6 — Durable background processing

Introduce background workers, persisted job status, progress reporting, retry and
cancellation behavior. This foundation supports long-running repository-analysis tasks.

**Status:** Complete — PostgreSQL-backed queue, API contract and Worker host tested locally.

The queue stores typed JSON payloads, attempt counts, progress, idempotency keys,
and worker leases in PostgreSQL. Workers claim supported job kinds with
`FOR UPDATE SKIP LOCKED`; expired leases are retried after a worker restart.
Authenticated users can inspect and cancel their own jobs. A development-only
progress job exercises progress, cancellation and retry behavior; it does not
fetch or analyze repositories. Real repository-analysis handlers belong to
Phase 7.

**Verified:** solution builds with zero warnings and errors; EF reports no pending
model changes; the runtime migration applied successfully. Automated tests cover
idempotent enqueue, progress, retry, cancellation, lease recovery, handler behavior,
and unauthenticated access rejection. The user confirmed their end-to-end local test
worked successfully.

**Complete when:** a representative job survives a worker restart, reports progress,
and handles retry and cancellation without duplicating published results.

## Phase 7 — Repository import and understanding

**Status:** Complete (extended scope) — repository import, manifest/deployment graph, and best-effort source relationship analysis are delivered and reviewed.

The initial provider is public GitHub, with no language allowlist. Every archive
file path is inventoried; file types/languages are detected best-effort, text is
stored through a local content-store adapter, and binaries are recorded as metadata.
Large text files receive a visible skip reason. Archive size, expanded size and
file-count safeguards fail the job with a stable failure code rather than silently
truncating the inventory. Repository code is never executed.

**Delivered:** `repository.import` durable jobs pin the selected ref (or the
repository's default branch) to a commit SHA, download that immutable GitHub
archive, guard against redirects and unsafe archive paths, and persist
`repository_snapshots` and `repository_files`. Owner-scoped endpoints queue
imports, list snapshots and return paginated file metadata. Small text files are
stored under the ignored local `data/repository-content` directory; raw content is
not stored in PostgreSQL or exposed by an API endpoint. Binary file content is not
stored. The authenticated project UI can queue an import, poll and display job
progress/retry/failure details, list snapshots, and browse paginated file metadata.
The owner-scoped architecture endpoint and UI provide a static overview of
component folders, detected languages/formats, likely entry points, and
build/dependency manifests, with repository paths as evidence. Each report finding
labels observations separately from inferences and includes its method, confidence,
and source references. A parser registry reads stored manifest content and extracts
declared dependencies from npm/Composer JSON, Python requirements/TOML, Go modules,
Cargo TOML, Maven XML, and .NET project XML; line references are included when
available. These declarations are not treated as proof of runtime use.
The frontend visualizes package declarations, .NET project references, Compose
startup dependencies, and source-code references as typed edges with file/line
evidence. Its source-code relationship section surfaces HTTP, database-provider,
and message-broker findings with confidence and evidence paths/lines. C# receives Roslyn syntax-tree parsing. JavaScript/TypeScript, Python,
Go, Java, Rust, Ruby, PHP, C/C++, Scala, Haskell, Julia, Agda, and QL use
Tree-sitter syntax trees for selected HTTP, database, and broker patterns; F#,
VB, Kotlin, and Swift still use a conservative line-based fallback. Source edges
are labeled as inferences, distinct from manifest observations. The typed
directed graph is stored inside the versioned report JSON in PostgreSQL, not in a
graph database.
Analysis is queued as a durable Worker job; versioned JSON reports are persisted
against the immutable snapshot commit and retrieved after job completion.

**Verified:** solution build succeeds with zero warnings/errors; all 59 tests pass,
including mocked GitHub commit pinning, archive traversal rejection, file
classification, static architecture analysis, authenticated endpoint checks, and
API contract coverage. The frontend production build succeeds and lint reports no
warnings. The runtime migration creates `repository_architecture_reports`. The user
reviewed the integrated frontend and confirmed it is working as expected.

**Limitations:** dependency extraction covers supported manifest formats and
reports declarations only. Compose `depends_on` reports declared startup ordering,
not actual network traffic or business-level calls. Syntax parsers recognize
selected syntax patterns but do not resolve symbols; fallback languages use
best-effort lexical matches. Both may miss dynamic URLs, indirect calls, wrappers,
and unsupported library patterns. These are source references, not
runtime-observed calls, and the result is not a complete call graph or data-flow
trace. Unsupported deployment manifests may not be parsed.

**Completion scope:** supported repositories produce an inspectable architecture
graph with evidence-backed project, package, deployment, HTTP, messaging, and
database relationships, clearly distinguished from runtime-observed behavior.
This does not claim a complete call graph, universal language-level semantic
analysis, or proof of runtime behavior; see the limitations above.

## Phase 8 — Open-model integration and chat conversations

**Status:** Complete — Ollama-backed grounded chat, SSE streaming, saved follow-ups, citations, and project UI are implemented.

OllamaSharp connects the .NET Infrastructure layer to a separately hosted Ollama
HTTP API. The configured default is `qwen2.5-coder:7b` at
`http://localhost:11434`; both endpoint and model are configurable. The API does
not install Ollama, download model weights, or require Ollama to run in Docker.
The conversation service retrieves bounded lexical evidence from text files
stored for the selected immutable snapshot, supplies it with recent conversation
history to the model, and asks the model to cite only the supplied evidence.
The model is explicitly told repository contents are untrusted input. The SSE
endpoint sends answer deltas and a completion event with citation metadata;
assistant messages persist their model ID and citations. User messages and
assistant replies are stored separately, so follow-ups can use conversation
history. The project UI can select a snapshot, create and reopen chats, stream
answers, and display file/line citations. No chat login-session state is needed.

**Verified:** API integration suite passes, including chat storage ownership,
snapshot scoping, streamed service answer/citation persistence using a fake model,
and unauthenticated rejection of conversation and streaming endpoints. The API
builds with zero warnings/errors; all 60 tests pass. The frontend lint and
production build succeed, and EF reports no pending model changes.

**Limitations:** evidence retrieval is bounded lexical search, not a semantic
vector index. Answers can omit source context or make mistakes; citations point
to retrieved ranges and should be verified in the repository. A local Ollama
service and the configured model must be available for actual model-driven
responses. The test suite uses a fake model and does not claim to validate model
quality or actual Ollama hardware throughput.

**Complete when:** users can create/reopen chats, ask questions about a selected
repository snapshot, and receive streamed answers grounded with source citations,
while project ownership remains enforced.

## Phase 9 — LLM-generated architecture storyboards — Complete

For an imported immutable snapshot with architecture analysis, users can enqueue
`repository.storyboard` through `POST /api/v1/projects/{projectId}/snapshots/{snapshotId}/storyboard`
and fetch the saved draft from the matching `GET` route. The API checks ownership
and requires a saved architecture report. The durable Worker sends the report
and a bounded evidence whitelist—not the full repository contents—to the
configured Ollama model. Model citations are validated against that whitelist
before scenes are saved in `repository_storyboards` and tied to their snapshot
and commit. The repository Storyboards workspace lets users select snapshots,
watch job progress, inspect narration/visual directions and source citations, and
regenerate a draft. EF applies the table migration at startup.

This is a human-reviewable architecture explanation plan, not a guarantee of
factual correctness. Citations are limited to evidence already present in static
analysis; users should verify claims.

**Verified:** solution build succeeds with zero warnings/errors; all 61 tests
pass, including storyboard job behavior and unauthenticated endpoint checks;
frontend lint and production build succeed; EF reports no pending model changes.

## Phase 10 — Quality, performance, and security validation

**Status:** In progress — baseline functional and authorization tests pass; broader
accuracy, load, performance, and resource-limit measurements remain.

Consolidate the tests added throughout development. Evaluate analysis and answer
accuracy, authorization, job recovery, concurrent chat workloads, resource
limits, and operational visibility. Measure performance on documented hardware.

**Verified to date:** 63 backend integration tests pass; frontend lint and
production build pass. This does not yet constitute the planned performance,
model-quality, concurrency, or resource-limit evaluation.

**Complete when:** agreed acceptance checks pass and performance limits and remaining
issues are documented. Testing and security checks also happen within earlier phases.

## Phase 11 — Docker packaging and release — Implementation verified

Package the API, Worker, and frontend as separate container images. Docker Compose
provides PostgreSQL, persistent repository-content storage, local-only ports,
configuration through an ignored `.env` file, and startup dependencies. Ollama
remains an external model runtime; its endpoint and model are configurable. The
release instructions build and tag versioned images and publish them to Docker Hub
only after the owner selects the namespace and approves the release.

**Complete when:** a clean environment can run RepoAI using the documented container
setup and the release commands produce the expected API, Worker, and frontend images.

**Verified:** API, Worker, and frontend images build locally; Compose validates;
an isolated stack applied migrations, served the frontend, and returned HTTP 200
through the frontend's proxied API health route. The API and Worker mount the same
persistent repository-content volume. Imports created before using that volume
remain on the host and must be re-imported or migrated into the volume. No images
have been pushed yet; Docker Hub publication awaits approval of the namespace and
version.

## Working agreement

- Use these phases as the roadmap, not as permission to implement everything at once.
- Explain and approve each implementation step before changing application files.
- Verify each approved step and record its result before continuing.
- Mark a phase complete only after its completion criteria are met.
- Keep the HLD aligned when an approved decision changes the architecture.
