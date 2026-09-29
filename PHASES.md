# RepoAI implementation phases

This document is our reference for building RepoAI incrementally. These phases
describe the planned order; they do not authorize implementation. Before each
step, we will explain the proposed changes and obtain your approval. A phase may
contain several separately approved steps.

Phase 1 is **Complete**. Phases 2–13 are **Not started**.
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

Add OpenAPI documentation and Swagger UI for interactive API testing. Establish
consistent request validation, error responses, and API organization.

**Complete when:** approved endpoints can be explored and tested through Swagger,
including success and validation-error responses.

## Phase 3 — PostgreSQL and persistence

Connect PostgreSQL through Entity Framework Core, introduce migrations, and define
the initial user, project, and ownership records. Add more entities as their
features are implemented rather than designing every table upfront.

**Complete when:** migrations create the database and persisted data survives an
application restart.

## Phase 4 — Google sign-in and access control

Implement Google sign-in through OAuth 2.0/OpenID Connect, map Google identities
to local users, and enforce project access permissions.

We will agree on the API authentication credential mechanism before implementation.
“Chat sessions” means saved conversations; it does not require a login-session table.

**Complete when:** users can sign in and access their authorized resources, while
unauthorized and cross-user access are rejected.

## Phase 5 — Web application workspace

Choose the frontend approach and build the initial browser experience: sign-in,
project navigation, project creation, and clear loading and error states.

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
