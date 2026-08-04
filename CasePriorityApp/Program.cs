using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;
using CasePriority.Core.Services;

// Composition root: the one place that picks and wires the concrete
// dependencies. The service only ever sees the ICaseRepository abstraction.
ICaseRepository repository = new InMemoryCaseRepository();
var caseService = new CaseService(repository);

caseService.CreateCase("0001", "User cannot log in", severity: 3);
caseService.CreateCase("0002", "Update email address", severity: 1);
caseService.CreateCase("0003", "Payment integration failed", severity: 5);
caseService.CreateCase("0004", "VP onboarding blocked", severity: 2);

caseService.CloseCase("0002");
caseService.EscalateCase("0001");
caseService.EscalateCase("0004");

// A rejected case: domain validation surfaces through the service.
try
{
    caseService.CreateCase("0005", "Impossible severity", severity: 9);
}
catch (ArgumentOutOfRangeException ex)
{
    Console.WriteLine($"Rejected case 0005: {ex.Message}");
}

// A missing case: the service turns the repository's null lookup into an error.
try
{
    caseService.CloseCase("9999");
}
catch (KeyNotFoundException ex)
{
    Console.WriteLine(ex.Message);
}

Console.WriteLine();
Console.WriteLine("Open cases:");

foreach (SupportCase currentCase in caseService.GetOpenCasesBySeverity())
{
    Console.WriteLine(
        $"{currentCase.CaseNumber}: {currentCase.Subject} " +
        $"— Priority: {currentCase.Priority}"
    );
}
