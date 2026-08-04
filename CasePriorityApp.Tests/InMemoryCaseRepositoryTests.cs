using CasePriorityApp;
using CasePriorityApp.Repositories;

namespace CasePriorityApp.Tests;

public class InMemoryCaseRepositoryTests
{
    private static SupportCase NewCase(string caseNumber, int severity = 3) =>
        new SupportCase(caseNumber, "Subject", severity);

    // ---- Add --------------------------------------------------------------

    [Fact]
    public void Add_StoresCase()
    {
        var repository = new InMemoryCaseRepository();
        var supportCase = NewCase("0001");

        repository.Add(supportCase);

        Assert.Same(supportCase, repository.GetByCaseNumber("0001"));
    }

    [Fact]
    public void Add_NullCase_Throws()
    {
        var repository = new InMemoryCaseRepository();
        Assert.Throws<ArgumentNullException>(() => repository.Add(null!));
    }

    [Fact]
    public void Add_DuplicateCaseNumber_Throws_AndKeepsOriginal()
    {
        var repository = new InMemoryCaseRepository();
        var original = new SupportCase("0001", "Original", severity: 3);
        var duplicate = new SupportCase("0001", "Duplicate", severity: 5);
        repository.Add(original);

        Assert.Throws<InvalidOperationException>(() => repository.Add(duplicate));
        // The rejected add must not replace what was already stored.
        Assert.Same(original, repository.GetByCaseNumber("0001"));
    }

    [Fact]
    public void Add_DuplicateWithDifferentCasing_Throws()
    {
        var repository = new InMemoryCaseRepository();
        repository.Add(new SupportCase("abc1", "First", severity: 3));

        Assert.Throws<InvalidOperationException>(
            () => repository.Add(new SupportCase("ABC1", "Second", severity: 4)));
    }

    // ---- GetByCaseNumber --------------------------------------------------

    [Fact]
    public void GetByCaseNumber_ExistingCase_ReturnsCase()
    {
        var repository = new InMemoryCaseRepository();
        var supportCase = NewCase("0002");
        repository.Add(supportCase);

        Assert.Same(supportCase, repository.GetByCaseNumber("0002"));
    }

    [Fact]
    public void GetByCaseNumber_MissingCase_ReturnsNull()
    {
        var repository = new InMemoryCaseRepository();
        Assert.Null(repository.GetByCaseNumber("nope"));
    }

    [Fact]
    public void GetByCaseNumber_IsCaseInsensitive()
    {
        var repository = new InMemoryCaseRepository();
        var supportCase = new SupportCase("abc1", "Subject", severity: 3);
        repository.Add(supportCase);

        Assert.Same(supportCase, repository.GetByCaseNumber("ABC1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetByCaseNumber_BlankCaseNumber_Throws(string? caseNumber)
    {
        var repository = new InMemoryCaseRepository();
        var ex = Assert.Throws<ArgumentException>(
            () => repository.GetByCaseNumber(caseNumber!));
        Assert.Equal("caseNumber", ex.ParamName);
    }

    // ---- GetAll -----------------------------------------------------------

    [Fact]
    public void GetAll_ReturnsAllCases()
    {
        var repository = new InMemoryCaseRepository();
        repository.Add(NewCase("0001"));
        repository.Add(NewCase("0002"));
        repository.Add(NewCase("0003"));

        var all = repository.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, c => c.CaseNumber == "0001");
        Assert.Contains(all, c => c.CaseNumber == "0002");
        Assert.Contains(all, c => c.CaseNumber == "0003");
    }

    [Fact]
    public void GetAll_DoesNotExposeInternalCollection()
    {
        var repository = new InMemoryCaseRepository();
        repository.Add(NewCase("0001"));

        var snapshot = repository.GetAll();
        // Mutating the returned list must not affect the repository.
        if (snapshot is List<SupportCase> mutable)
        {
            mutable.Add(NewCase("9999"));
        }

        Assert.Single(repository.GetAll());
        Assert.Null(repository.GetByCaseNumber("9999"));
    }
}
