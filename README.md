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

## Architecture (Day 6)

The domain/repository/service layers live in a reusable class library; the API and
the console app are two front ends that assemble the same Core the same way. Mutations
use **optimistic concurrency** — each case has a version, surfaced as an HTTP ETag.

```
HTTP If-Match                     console
    │                                │
    ▼                                ▼
CasesController                 Program.cs (composition root)
    │  expected version              │
    └───────────────┬────────────────┘
                    ▼
             CaseService              coordinates use cases; mutations require a version
                    │
                    ▼
             ICaseRepository          persistence contract the service depends on
                    │
                    ▼
          InMemoryCaseRepository      ConcurrentDictionary — collection-safe singleton
                    │
                    ▼
             SupportCase              per-case lock: atomic version-check + mutation + bump
                    │
                    ▼
          SupportCaseSnapshot         immutable view returned by service → CaseResponse + ETag
```

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

Then use `CasePriority.Api/CasePriority.Api.http`, or:

```bash
# Create (returns ETag: "1")
curl -i -X POST http://localhost:5075/api/cases \
  -H "Content-Type: application/json" \
  -d '{"caseNumber":"WEB-0001","subject":"User cannot access the portal","severity":3}'

# Close using the current version (returns ETag: "2")
curl -i -X PATCH http://localhost:5075/api/cases/WEB-0001/close -H 'If-Match: "1"'

# Re-using the stale version now returns 412 Precondition Failed
curl -i -X PATCH http://localhost:5075/api/cases/WEB-0001/escalate -H 'If-Match: "1"'
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
