# csharp-practice

[![CI](https://github.com/MichaelWick2024/csharp-practice/actions/workflows/ci.yml/badge.svg)](https://github.com/MichaelWick2024/csharp-practice/actions/workflows/ci.yml)

Hands-on C#/.NET practice as I transfer my Salesforce/Apex development experience to
C#, ASP.NET Core, and SQL Server. Learning by building, not by watching — each folder
is a small, runnable project, growing toward a full ASP.NET Core case-management API.

## Projects

| Project | Type | Role |
|---------|------|------|
| **`CasePriority.Core`** | class library | Reusable domain, repository, and service — the heart, referenced by everything |
| **`CasePriority.Api`** | ASP.NET Core Web API | Controller-based HTTP API over the Core service |
| **`CasePriorityApp`** | console app | Composition root that demonstrates Core as a console program |
| **`CasePriorityApp.Tests`** | xUnit | Unit tests for the domain, repository, and service |
| **`CasePriority.Api.Tests`** | xUnit | Integration tests driving the API through `WebApplicationFactory` |

## Architecture (Day 5)

The domain/repository/service layers live in a reusable class library; the API and
the console app are two front ends that assemble the same Core the same way.

```
HTTP client                       console
    │                                │
    ▼                                ▼
CasesController                 Program.cs (composition root)
    └───────────────┬────────────────┘
                    ▼
             CaseService              coordinates use cases (create, query, ...)
                    │
                    ▼
             ICaseRepository          persistence contract the service depends on
                    │
                    ▼
          InMemoryCaseRepository      ConcurrentDictionary-backed storage (thread-safe singleton)
                    │
                    ▼
             SupportCase              domain object — guards its own state & invariants
```

- **`SupportCase`** owns the rules about one case (validation, guarded transitions, computed `Priority`).
- **`ICaseRepository`** describes persistence operations without a storage mechanism.
- **`InMemoryCaseRepository`** uses a `ConcurrentDictionary`, making its **collection** operations safe across concurrent requests, so it can be a shared singleton; a database-backed one can replace it later. (Coordination for concurrent mutation of an individual `SupportCase` is deferred until mutation endpoints are introduced — Day 5 exposes only GET and POST.)
- **`CaseService`** coordinates use cases and depends on `ICaseRepository` (constructor injection), never the concrete repository.
- **`CasesController`** does only HTTP work — validated request DTOs in, `CaseResponse` DTOs out, REST status codes — and delegates business work to the service. Domain/service exceptions map to Problem Details centrally (`ApiExceptionHandler`): `KeyNotFoundException` → 404, `InvalidOperationException` → 409, `ArgumentException` → 400.

### Run the API

```bash
dotnet run --project CasePriority.Api        # http://localhost:5075
```

Then use `CasePriority.Api/CasePriority.Api.http`, or:

```bash
curl -i -X POST http://localhost:5075/api/cases \
  -H "Content-Type: application/json" \
  -d '{"caseNumber":"WEB-0001","subject":"User cannot access the portal","severity":3}'
```

### Run the console demo

```bash
dotnet run --project CasePriorityApp
```

## Testing

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
