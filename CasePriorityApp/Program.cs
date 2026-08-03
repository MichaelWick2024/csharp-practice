var cases = new List<SupportCase>
{
    new SupportCase
    {
        CaseNumber = "0001",
        Subject = "User cannot log in",
        Severity = 3,
        IsOpen = true
    },
    new SupportCase
    {
        CaseNumber = "0002",
        Subject = "Update email address",
        Severity = 1,
        IsOpen = false
    },
    new SupportCase
    {
        CaseNumber = "0003",
        Subject = "Payment integration failed",
        Severity = 5,
        IsOpen = true
    }
};

var openCases = cases
    .Where(currentCase => currentCase.IsOpen)
    .OrderByDescending(currentCase => currentCase.Severity);

Console.WriteLine("Open cases:");

foreach (SupportCase currentCase in openCases)
{
    Console.WriteLine(
        $"{currentCase.CaseNumber}: {currentCase.Subject} " +
        $"— Priority: {currentCase.GetPriority()}"
    );
}

public class SupportCase
{
    public string CaseNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public int Severity { get; set; }
    public bool IsOpen { get; set; }

    public string GetPriority()
    {
        if (Severity >= 5)
        {
            return "Critical";
        }

        if (Severity >= 3)
        {
            return "High";
        }

        return "Normal";
    }
}
