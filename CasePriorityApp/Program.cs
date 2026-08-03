using CasePriorityApp;

// Cases are now built through the constructor, which validates every argument,
// so a SupportCase can never exist in an invalid state.
var cases = new List<SupportCase>
{
    new SupportCase("0001", "User cannot log in", severity: 3),
    new SupportCase("0002", "Update email address", severity: 1, isOpen: false),
    new SupportCase("0003", "Payment integration failed", severity: 5),
    new SupportCase("0004", "VP onboarding blocked", severity: 2, isExecutiveEscalation: true)
};

// Basic exception handling: a bad case is rejected by the constructor and the
// program keeps running instead of crashing.
try
{
    cases.Add(new SupportCase("0005", "Impossible severity", severity: 9));
}
catch (ArgumentOutOfRangeException ex)
{
    Console.WriteLine($"Rejected case 0005: {ex.Message}");
}

// State-changing methods: reopen a closed case and escalate an open one.
// These go through validated methods, not direct field assignment.
cases.Single(currentCase => currentCase.CaseNumber == "0002").Reopen();
cases.Single(currentCase => currentCase.CaseNumber == "0001").Escalate();

var openCases = cases
    .Where(currentCase => currentCase.IsOpen)
    .OrderByDescending(currentCase => currentCase.Severity);

Console.WriteLine();
Console.WriteLine("Open cases:");

foreach (SupportCase currentCase in openCases)
{
    Console.WriteLine(
        $"{currentCase.CaseNumber}: {currentCase.Subject} " +
        $"— Priority: {currentCase.GetPriority()}"
    );
}
