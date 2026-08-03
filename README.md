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
