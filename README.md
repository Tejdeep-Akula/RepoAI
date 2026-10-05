# RepoAI

A repository-to-video application with code-grounded conversational chat.
Implementation follows [PHASES.md](PHASES.md), one approved phase at a time.

## Project structure

```text
RepoAI/
  RepoAI.slnx
  global.json
  src/
    RepoAI.Api/
      Contracts/
      Controllers/
      Errors/
    RepoAI.Application/
    RepoAI.Domain/
    RepoAI.Infrastructure/
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

Phases 1 and 2 are complete. PostgreSQL, Google sign-in, frontend, background jobs, repository
analysis, persistent chat conversations, and LTX integration follow the roadmap.
No login-session database table, authentication, database, or model integration
is introduced by this foundation.
