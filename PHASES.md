# RepoAI implementation phases

This document is our reference for building RepoAI incrementally. These phases
describe the planned order; they do not authorize implementation. Before each
step, we will explain the proposed changes and obtain your approval. A phase may
contain several separately approved steps.

Phases 1–3 are **Complete**.
Phases 4 and 5 are **In progress**. Phases 6–13 are **Not started**.
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

**Status:** In progress — local Google identity storage foundation added.

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

**Next:** Configure the Google Web OAuth client and a private local JWT signing
key, apply the pending migration by starting the API, then verify sign-in and
owner isolation end to end. A browser client is still needed to obtain a Google
ID token through the intended user flow.

“Chat sessions” means saved conversations; it does not require a login-session table.

**Complete when:** users can sign in, create and retrieve their own persisted
projects, while unauthenticated and cross-user access are rejected.

## Phase 5 — Web application workspace

**Status:** In progress — frontend foundation created.

Use React with TypeScript for the browser UI and Vite for local development and
production builds. The local Vite origin is pinned to `http://localhost:5173`;
the .NET API remains at `http://localhost:5080`.

**Delivered so far:** Vite React + TypeScript app in `frontend/`, strict
development port configuration, responsive RepoAI workspace shell, API status
indicator, and repository URL syntax validation wired to the existing API.
Development CORS accepts only `http://localhost:5173`. Google Identity Services
is wired to `POST /api/v1/auth/google`; the RepoAI bearer token remains in
browser memory and is not persisted. The Google Client Secret is not used.
Authenticated project loading and creation are not connected yet.

**Verified:** frontend production build and lint pass. CORS tests cover allowed
and disallowed origins. Google sign-in was verified locally with the API,
PostgreSQL, and the OAuth client configured for the frontend origin.

**Next:** Restart the API and Vite dev server to load local OAuth configuration,
then verify browser sign-in. After sign-in works, connect the authenticated
project list and creation flows.

**Complete when:** an authenticated user can manage their own projects through the UI.

## Phase 6 — Durable background processing

Introduce background workers, persisted job status, progress reporting, retry and
cancellation behavior. This foundation supports long repository and video tasks.

**Complete when:** a representative job survives a worker restart, reports progress,
and handles retry and cancellation without duplicating published results.

## Phase 7 — Repository import and understanding

Agree on initial repository providers and language coverage. Import a repository
at a fixed commit, analyze supported code and configuration, and expose searchable
files, components, dependencies, and evidence for architecture relationships.

**Complete when:** a supported repository produces an inspectable architecture
overview with source references and clearly stated analysis limitations.

## Phase 8 — Open-model integration and chat conversations

Select and configure an appropriate open model, retrieve repository evidence, and
build detailed conversational answers. Persist separate conversations and messages,
support follow-up questions, and provide streaming responses and code citations.

**Complete when:** users can create and reopen chats, ask repository questions, and
receive grounded answers that respect project access and the selected code snapshot.

## Phase 9 — Explanation and video storyboard

Turn verified repository findings into an explanation outline, narration script,
and structured video scenes. Provide a preview of the proposed explanation before
expensive video generation.

**Complete when:** each planned scene has a clear purpose, narration, visual
instructions, and supporting repository evidence.

## Phase 10 — LTX video generation and composition

Integrate a selected LTX checkpoint through an isolated inference runtime controlled
by the .NET backend. Generate clips and compose accurate code, diagram labels,
narration, captions, and final video output. Validate model licensing and hardware
requirements before committing to the runtime configuration.

**Complete when:** an approved storyboard produces a playable video, with progress,
failure recovery, and mappings between scenes and repository evidence.

## Phase 11 — Connected video and chat experience

Integrate the video player, scene navigation, captions, and downloads with chat.
Allow users to ask about the current video segment and jump from answers to related
code or timestamps.

**Complete when:** users can watch a repository explanation and explore its details
through persistent, context-aware conversations.

## Phase 12 — Quality, performance, and security validation

Consolidate the tests added throughout development. Evaluate analysis and answer
accuracy, authorization, job recovery, concurrent chat/video workloads, resource
limits, and operational visibility. Measure performance on documented hardware.

**Complete when:** agreed acceptance checks pass and performance limits and remaining
issues are documented. Testing and security checks also happen within earlier phases.

## Phase 13 — Docker packaging and release

Package the .NET services and document external model dependencies. Provide Docker
Compose, persistent storage, secrets configuration, setup instructions, and a
repeatable release process. Publish versioned images to Docker Hub after approval
of the target account and release contents.

**Complete when:** a clean environment can run RepoAI using the documented container
setup and the approved release images. Local dependency containers may be used in
earlier phases; this phase covers the complete distribution.

## Working agreement

- Use these phases as the roadmap, not as permission to implement everything at once.
- Explain and approve each implementation step before changing application files.
- Verify each approved step and record its result before continuing.
- Mark a phase complete only after its completion criteria are met.
- Keep the HLD aligned when an approved decision changes the architecture.
