# RepoAI

A repository-to-video application with code-grounded conversational chat.
Implementation follows [PHASES.md](PHASES.md), one approved phase at a time.

## Phase 1 structure

```text
RepoAI/
  RepoAI.slnx
  global.json
  src/
    RepoAI.Api/
    RepoAI.Application/
    RepoAI.Domain/
    RepoAI.Infrastructure/
  RepoAI-design/
  PHASES.md
```

| Project | Responsibility | Project references |
| --- | --- | --- |
| Api | HTTP endpoints and startup/composition | Application, Infrastructure |
| Application | Use cases and interfaces | Domain |
| Domain | Business entities and rules | None |
| Infrastructure | Database, repository and model adapters | Application |

The three class libraries are deliberately empty until their first approved
features. Infrastructure can access Domain through the transitive project
reference. Domain does not depend on web or database frameworks. A separate Worker
process will be introduced in Phase 6.

## SDK and build

Use the .NET 10.0.401 SDK, or a newer patch in the same feature band.
`global.json` selects the SDK for this directory; .NET 8 remains installed for
other projects. Each `.csproj` declares `net10.0`, nullable checks and implicit
usings directly. `.slnx` is the solution format supported by .NET 10.

From the RepoAI root:

```sh
dotnet --version
dotnet build RepoAI.slnx --disable-build-servers -m:1
dotnet run --project src/RepoAI.Api --no-build --launch-profile https
```

The build command uses one MSBuild node and avoids persistent build servers to
keep the initial local build predictable. No additional NuGet packages are used
in this phase.

Open https://localhost:7080 for the JSON startup response.
Open https://localhost:7080/health for `Healthy` (HTTP 200).
Press Ctrl+C in the terminal to stop the API.

The `https` profile is the default and listens on HTTPS port 7080 and HTTP port
5080. If the local HTTPS certificate is not trusted, run
`dotnet dev-certs https --trust` and accept the macOS trust prompt.
For HTTP only, use `dotnet run --project src/RepoAI.Api --launch-profile http`.

The health endpoint checks process liveness only. It does not claim that a
database or AI model is available. This phase creates an API, not a frontend UI.
Both launch profiles are for local development. They do not configure production
TLS or automatic HTTP-to-HTTPS redirection.

## VS Code

Open this root folder and use the build and run commands above in the integrated
terminal. Project-specific `.vscode` configuration is not included.

## Later phases

Swagger is Phase 2. PostgreSQL, Google sign-in, frontend, background jobs, repository
analysis, persistent chat conversations, and LTX integration follow the roadmap.
No login-session database table, authentication, database, or model integration
is introduced by this foundation.
