using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;

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

    [Fact]
    public void GetAll_ReturnsSnapshot_NotLiveView()
    {
        // Stronger contract test: independent of the returned concrete type,
        // a later Add must not appear in an earlier snapshot.
        var repository = new InMemoryCaseRepository();
        repository.Add(NewCase("0001"));

        var snapshot = repository.GetAll();
        repository.Add(NewCase("0002"));

        Assert.Single(snapshot);                 // earlier snapshot unchanged
        Assert.Equal(2, repository.GetAll().Count); // repository reflects the add
    }

    // ---- Concurrency ------------------------------------------------------

    [Fact]
    public void Add_ConcurrentDuplicateAttempts_StoresExactlyOneCase()
    {
        // The ConcurrentDictionary-backed repo must let exactly one racing add
        // win when many threads insert the same case number at once.
        var repository = new InMemoryCaseRepository();
        var successfulAdds = 0;
        var rejectedAdds = 0;

        var duplicateCases = Enumerable
            .Range(1, 20)
            .Select(index => new SupportCase("0001", $"Attempt {index}", severity: 3))
            .ToList();

        Parallel.ForEach(duplicateCases, supportCase =>
        {
            try
            {
                repository.Add(supportCase);
                Interlocked.Increment(ref successfulAdds);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref rejectedAdds);
            }
        });

        Assert.Equal(1, successfulAdds);
        Assert.Equal(19, rejectedAdds);
        Assert.Single(repository.GetAll());
    }
}
