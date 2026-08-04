using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;

// Composition root: the one place that picks and wires the concrete
// dependencies. The in-memory repository is both the store and the unit of work.
var repository = new InMemoryCaseRepository();
var caseService = new CaseService(repository, repository);

await caseService.CreateCaseAsync("0001", "User cannot log in", severity: 3);
await caseService.CreateCaseAsync("0002", "Update email address", severity: 1);
await caseService.CreateCaseAsync("0003", "Payment integration failed", severity: 5);
await caseService.CreateCaseAsync("0004", "VP onboarding blocked", severity: 2);

// Mutations require the caller's expected version — the same optimistic
// concurrency sequence the HTTP API uses: read snapshot -> submit its version.
var caseToClose = await caseService.GetCaseByNumberAsync("0002");
await caseService.CloseCaseAsync("0002", caseToClose.Version);

var caseToEscalate = await caseService.GetCaseByNumberAsync("0001");
await caseService.EscalateCaseAsync("0001", caseToEscalate.Version);

var vpCase = await caseService.GetCaseByNumberAsync("0004");
await caseService.EscalateCaseAsync("0004", vpCase.Version);

// A rejected case: domain validation surfaces through the service.
try
{
    await caseService.CreateCaseAsync("0005", "Impossible severity", severity: 9);
}
catch (ArgumentOutOfRangeException ex)
{
    Console.WriteLine($"Rejected case 0005: {ex.Message}");
}

// A missing case: the service turns the repository's null lookup into an error.
try
{
    await caseService.CloseCaseAsync("9999", expectedVersion: 1);
}
catch (KeyNotFoundException ex)
{
    Console.WriteLine(ex.Message);
}

Console.WriteLine();
Console.WriteLine("Open cases:");

foreach (SupportCaseSnapshot currentCase in await caseService.GetOpenCasesBySeverityAsync())
{
    Console.WriteLine(
        $"{currentCase.CaseNumber}: {currentCase.Subject} " +
        $"— Priority: {currentCase.Priority} (v{currentCase.Version})"
    );
}
