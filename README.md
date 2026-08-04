# csharp-practice

[![CI](https://github.com/MichaelWick2024/csharp-practice/actions/workflows/ci.yml/badge.svg)](https://github.com/MichaelWick2024/csharp-practice/actions/workflows/ci.yml)

Hands-on C#/.NET practice as I transfer my Salesforce/Apex development experience to
C#, ASP.NET Core, and SQL Server. Learning by building, not by watching — each folder
is a small, runnable project, growing toward a full ASP.NET Core case-management API.

## Projects

| Project | Type | Role |
|---------|------|------|
| **`CasePriority.Core`** | class library | Reusable domain, repository/unit-of-work contracts, service — no EF dependency |
| **`CasePriority.Infrastructure`** | class library | EF Core: `DbContext`, mapping, `EfCaseRepository`, migrations (SQL Server) |
| **`CasePriority.Api`** | ASP.NET Core Web API | Controller-based HTTP API over the Core service |
| **`CasePriorityApp`** | console app | Composition root that demonstrates Core (in-memory) as a console program |
| **`CasePriorityApp.Tests`** | xUnit | Unit tests for the domain, repository, and service (in-memory) |
| **`CasePriority.Api.Tests`** | xUnit | API tests via `WebApplicationFactory` (in-memory swap) + SQL-backed E2E (CI) |
| **`CasePriority.Infrastructure.Tests`** | xUnit | Real SQL Server integration tests (mapping, persistence, DB concurrency) — CI |

## Architecture (Day 7)

The domain/repository/service layers live in a reusable class library; the API and
the console app are two front ends that assemble the same Core. Mutations use
**optimistic concurrency** — each case has a version, surfaced as an HTTP ETag —
now enforced at the database via an EF Core concurrency token.

```
HTTP If-Match                     console
    │                                │
    ▼                                ▼
CasesController                 Program.cs (composition root)
    │  expected version              │
    └───────────────┬────────────────┘
                    ▼
             CaseService              coordinates use cases; mutations require a version
                    ▼
      ICaseRepository + IUnitOfWork   persistence + commit contracts the service depends on
                    │
        ┌───────────┴─────────────────────────────┐
        ▼                                          ▼
  EfCaseRepository → CasePriorityDbContext    InMemoryCaseRepository
        → SQL Server                          (ConcurrentDictionary)
     production API                           console + fast tests
        │                                          │
        └───────────────┬──────────────────────────┘
                        ▼
             SupportCase              per-case lock: atomic version-check + mutation + bump
                        ▼
          SupportCaseSnapshot         immutable view returned by service → CaseResponse + ETag
```

> The production API talks to **SQL Server via EF Core** and needs a reachable
> database + `ConnectionStrings:CasePriority` (see *Persistence* below). The
> console app and the fast tests use the in-memory repository.

- **`SupportCase`** owns the rules about one case and a per-case `Lock` + `Version`. Every mutation runs the expected-version check, the domain transition, and the version bump **inside one critical section**, then returns an immutable `SupportCaseSnapshot`. The version increments only when the representation actually changes.
- **`ICaseRepository`** describes persistence operations without a storage mechanism.
- **`InMemoryCaseRepository`** uses a `ConcurrentDictionary`, making its **collection** operations safe across concurrent requests (a shared singleton). Per-object coordination lives in `SupportCase`'s lock.
- **`CaseService`** returns snapshots and requires an expected version on every mutation; it never hands out the mutable domain object.
- **`CasesController`** does only HTTP work and the ETag/If-Match plumbing. Exceptions map to Problem Details centrally (`ApiExceptionHandler`): `PreconditionRequiredException` → **428**, `CaseConcurrencyException` → **412**, `KeyNotFoundException` → 404, `InvalidOperationException` → 409, `ArgumentException` → 400.

### Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/api/cases` | all cases |
| `GET` | `/api/cases/{caseNumber}` | one case; response carries an `ETag` |
| `POST` | `/api/cases` | create; `201` + `Location` + `ETag: "1"` |
| `PATCH` | `/api/cases/{caseNumber}/close` | requires `If-Match` |
| `PATCH` | `/api/cases/{caseNumber}/reopen` | requires `If-Match` |
| `PATCH` | `/api/cases/{caseNumber}/escalate` | requires `If-Match` |
| `PATCH` | `/api/cases/{caseNumber}/severity` | requires `If-Match`; body `{ "severity": 1..5 }` |

Every `PATCH` needs `If-Match: "<version>"`. Missing → **428**, malformed → **400**, stale → **412**, invalid transition → **409**, success → **200** with the new `ETag`.

### Run the API

```bash
dotnet run --project CasePriority.Api        # http://localhost:5075
```

The API requires a bearer token (all case endpoints are protected). Generate one
locally (stored in user-secrets, never committed):

```bash
TOKEN=$(dotnet user-jwts create --project CasePriority.Api --role CaseManager --valid-for 1h --output token)
```

Then use `CasePriority.Api/CasePriority.Api.http`, or:

```bash
# No token -> 401 Unauthorized + WWW-Authenticate: Bearer
curl -i http://localhost:5075/api/cases

# Create (CaseManager, returns ETag: "1")
curl -i -X POST http://localhost:5075/api/cases \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"caseNumber":"WEB-0001","subject":"User cannot access the portal","severity":3}'

# Close using the current version (returns ETag: "2")
curl -i -X PATCH http://localhost:5075/api/cases/WEB-0001/close \
  -H "Authorization: Bearer $TOKEN" -H 'If-Match: "1"'

# Re-using the stale version now returns 412 Precondition Failed
curl -i -X PATCH http://localhost:5075/api/cases/WEB-0001/escalate \
  -H "Authorization: Bearer $TOKEN" -H 'If-Match: "1"'

# A Viewer token attempting to create -> 403 Forbidden
VIEWER=$(dotnet user-jwts create --project CasePriority.Api --role Viewer --valid-for 1h --output token)
curl -i -X POST http://localhost:5075/api/cases \
  -H "Authorization: Bearer $VIEWER" -H "Content-Type: application/json" \
  -d '{"caseNumber":"WEB-0002","subject":"nope","severity":3}'
```

### Run the console demo

```bash
dotnet run --project CasePriorityApp
```

## Persistence (Day 7)

Storage is EF Core + SQL Server. `CasePriority.Core` stays EF-free; persistence
lives in `CasePriority.Infrastructure` behind `ICaseRepository` + `IUnitOfWork`,
so the service and API contracts don't change. The API registers a **scoped**
`DbContext` and an `EfCaseRepository` (both interfaces resolve to the same scoped
instance). Repository/service operations are **async** with cancellation tokens.

The numeric `Version` is mapped as an **application-managed EF concurrency
token** (not SQL `rowversion`), preserving the existing numeric ETag contract:
EF puts the original version in the `UPDATE ... WHERE Version = @original`, so a
stale write updates zero rows → `DbUpdateConcurrencyException`, translated back to
the same `CaseConcurrencyException` → **412**. Migrations are applied out-of-band
(CLI/CI), never automatically at startup.

## Operations (Day 8)

The API is built to configure, monitor, and troubleshoot:

- **Validated configuration** — `RequestTracing` settings are bound and validated
  at **startup** (`AddOptionsWithValidateOnStart`); bad values stop the app rather
  than surfacing per request.
- **Correlation IDs** — `CorrelationIdMiddleware` honors a valid client
  `X-Correlation-ID` or generates one, sets it as `TraceIdentifier`, echoes it on
  the response, and opens a logging scope. Invalid IDs are replaced, never rejected.
  The same value appears as `traceId` in every Problem Details, so a caller can
  quote one value that also appears in the logs.
- **Structured logging** — `CaseService` emits source-generated logs with stable
  event IDs (1001–1007) and named fields (`CaseNumber`, `Version`, …), **only after
  a successful commit**; no-ops log at Debug. Never logs the subject or secrets.
- **Health checks** — `GET /health/live` (is the process up? no dependency probe)
  and `GET /health/ready` (real SQL Server connectivity → 503 when unreachable).
  Health responses never expose the connection string.

## Security (Day 9)

The API **validates** JWT bearer tokens — signature, issuer, audience, and
expiration — but never issues production tokens or accepts passwords. Local dev
tokens come from `dotnet user-jwts`; a real deployment would use an OIDC/OAuth
provider (e.g. Entra ID).

| Role | Read | Create / modify |
|------|------|-----------------|
| `Viewer` | ✅ | ❌ |
| `CaseManager` | ✅ | ✅ |
| `Administrator` | ✅ | ✅ |

- No/invalid token → **401** with `WWW-Authenticate: Bearer`; valid token but
  insufficient role → **403**. Both are Problem Details carrying the Day 8
  correlation `traceId`, and never leak token/validation details.
- Policies: `Cases.Read` (GETs) and `Cases.Manage` (POST + PATCH), enforced with
  `[Authorize(Policy = …)]`. Roles come from the token's `role` claims.
- `/health/live`, `/health/ready`, and (in Development) `/openapi/v1.json` are
  explicitly **anonymous**. The OpenAPI document defines the Bearer scheme and
  marks every case operation as requiring it.

```bash
# Local development tokens (stored in user-secrets, never the repo):
dotnet user-jwts create --project CasePriority.Api --role Administrator --valid-for 1h
curl -i http://localhost:5075/api/cases -H "Authorization: Bearer <TOKEN>"
```

## Testing

Two intentional layers:

- **Fast, DB-free (local + CI):** unit tests, and API tests that boot the real
  app but swap EF for a shared in-memory repository (`InMemoryApiFactory`).
- **Real SQL Server (CI only):** `CasePriority.Infrastructure.Tests` (mapping,
  cross-scope persistence, case-insensitive keys, unique-constraint handling,
  two-`DbContext` database concurrency) and a few SQL-backed API E2E tests
  (POST → restart/new host → GET; competing PATCH). These **skip visibly** when
  no `ConnectionStrings__CasePriority` is set, so `dotnet test` stays green on a
  Mac; CI starts an x64 SQL Server service, applies migrations to a clean
  database, and runs them for real.


- **`CasePriorityApp.Tests`** — unit tests for the domain, repository, and service.
- **`CasePriority.Api.Tests`** — integration tests that boot the API in-memory with
  `WebApplicationFactory` and exercise routing, model binding, DI, serialization,
  middleware, and HTTP status codes end to end.

Run the whole solution:

```bash
dotnet test
```

Every push to `main` and every pull request targeting `main` also runs
restore → build → test via GitHub Actions (`.github/workflows/ci.yml`).

## Apex → C# quick map

| C# | Apex equivalent |
|----|-----------------|
| `public class SupportCase` | Apex class |
| `{ get; set; }` auto-property | Apex property (same syntax) |
| `List<SupportCase>` | `List<SupportCase>` |
| `.Where(c => c.IsOpen)` | SOQL `WHERE`, but over in-memory collections |
| `.OrderByDescending(c => c.Severity)` | `ORDER BY ... DESC` |
| `foreach` | Apex `for` loop |
| `$"{value}"` | string interpolation |
| `dotnet run` | compile + execute |

## Environment

.NET 10 SDK · VS Code + C# Dev Kit · macOS (Apple Silicon).
