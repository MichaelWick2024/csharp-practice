# csharp-practice

[![CI](https://github.com/MichaelWick2024/csharp-practice/actions/workflows/ci.yml/badge.svg)](https://github.com/MichaelWick2024/csharp-practice/actions/workflows/ci.yml)

Hands-on C#/.NET practice as I transfer my Salesforce/Apex development experience to
C#, ASP.NET Core, and SQL Server. Learning by building, not by watching — each folder
is a small, runnable project, growing toward a full ASP.NET Core case-management API.

## Projects

### CasePriorityApp (Day 1)
A console app that models support cases, filters to the open ones, sorts them by
severity (highest first), and computes a priority label — including an
executive-escalation rule that forces `Critical` regardless of severity.

Practices: C# classes, auto-properties, `List<T>`, LINQ (`Where` / `OrderByDescending`),
lambdas, `foreach`, string interpolation, and conditional logic.

Run it:

```bash
cd CasePriorityApp
dotnet run
```

## Architecture (Day 4)

The app is layered so each piece has one job, and dependencies point at
abstractions rather than concrete types:

```
Program.cs            composition root — picks and wires the concrete objects
    │
    ▼
CaseService           coordinates use cases (create, close, escalate, query)
    │
    ▼
ICaseRepository       persistence contract the service depends on
    │
    ▼
InMemoryCaseRepository   current storage (dictionary keyed by case number)
    │
    ▼
SupportCase           domain object — guards its own state & invariants
```

- **`SupportCase`** owns the rules about one case (validation, guarded state transitions, computed `Priority`).
- **`ICaseRepository`** describes persistence operations without a storage mechanism.
- **`InMemoryCaseRepository`** provides the current dictionary-backed storage; a database-backed one can replace it later without touching the service.
- **`CaseService`** coordinates application use cases and depends on `ICaseRepository` (constructor injection), never on the concrete repository.
- **`Program.cs`** is only the composition root plus a demonstration — it holds no case list and does no filtering, lookup, or state mutation itself.

`CaseService → ICaseRepository` is the key direction: the service knows *what* storage must do, not *how* it does it. ASP.NET Core will later perform this same constructor injection through its built-in container.

## Testing

xUnit tests live in `CasePriorityApp.Tests`. Run the whole solution:

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
