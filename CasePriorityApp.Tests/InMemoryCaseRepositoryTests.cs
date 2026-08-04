using CasePriority.Core.Domain;
using CasePriority.Core.Repositories;

namespace CasePriorityApp.Tests;

public class InMemoryCaseRepositoryTests
{
    private static SupportCase NewCase(string caseNumber, int severity = 3) =>
        new SupportCase(caseNumber, "Subject", severity);

    // ---- Add --------------------------------------------------------------

    [Fact]
    public async Task Add_StoresCase()
    {
        var repository = new InMemoryCaseRepository();
        var supportCase = NewCase("0001");

        repository.Add(supportCase);

        Assert.Same(supportCase, await repository.GetByCaseNumberAsync("0001"));
    }

    [Fact]
    public void Add_NullCase_Throws()
    {
        var repository = new InMemoryCaseRepository();
        Assert.Throws<ArgumentNullException>(() => repository.Add(null!));
    }

    [Fact]
    public async Task Add_DuplicateCaseNumber_Throws_AndKeepsOriginal()
    {
        var repository = new InMemoryCaseRepository();
        var original = new SupportCase("0001", "Original", severity: 3);
        var duplicate = new SupportCase("0001", "Duplicate", severity: 5);
        repository.Add(original);

        Assert.Throws<InvalidOperationException>(() => repository.Add(duplicate));
        Assert.Same(original, await repository.GetByCaseNumberAsync("0001"));
    }

    [Fact]
    public void Add_DuplicateWithDifferentCasing_Throws()
    {
        var repository = new InMemoryCaseRepository();
        repository.Add(new SupportCase("abc1", "First", severity: 3));

        Assert.Throws<InvalidOperationException>(
            () => repository.Add(new SupportCase("ABC1", "Second", severity: 4)));
    }

    // ---- GetByCaseNumberAsync ---------------------------------------------

    [Fact]
    public async Task GetByCaseNumber_ExistingCase_ReturnsCase()
    {
        var repository = new InMemoryCaseRepository();
        var supportCase = NewCase("0002");
        repository.Add(supportCase);

        Assert.Same(supportCase, await repository.GetByCaseNumberAsync("0002"));
    }

    [Fact]
    public async Task GetByCaseNumber_MissingCase_ReturnsNull()
    {
        var repository = new InMemoryCaseRepository();
        Assert.Null(await repository.GetByCaseNumberAsync("nope"));
    }

    [Fact]
    public async Task GetByCaseNumber_IsCaseInsensitive()
    {
        var repository = new InMemoryCaseRepository();
        var supportCase = new SupportCase("abc1", "Subject", severity: 3);
        repository.Add(supportCase);

        Assert.Same(supportCase, await repository.GetByCaseNumberAsync("ABC1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetByCaseNumber_BlankCaseNumber_Throws(string? caseNumber)
    {
        var repository = new InMemoryCaseRepository();
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => repository.GetByCaseNumberAsync(caseNumber!));
        Assert.Equal("caseNumber", ex.ParamName);
    }

    // ---- GetAllAsync ------------------------------------------------------

    [Fact]
    public async Task GetAll_ReturnsAllCases()
    {
        var repository = new InMemoryCaseRepository();
        repository.Add(NewCase("0001"));
        repository.Add(NewCase("0002"));
        repository.Add(NewCase("0003"));

        var all = await repository.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, c => c.CaseNumber == "0001");
        Assert.Contains(all, c => c.CaseNumber == "0002");
        Assert.Contains(all, c => c.CaseNumber == "0003");
    }

    [Fact]
    public async Task GetAll_ReturnsSnapshot_NotLiveView()
    {
        var repository = new InMemoryCaseRepository();
        repository.Add(NewCase("0001"));

        var snapshot = await repository.GetAllAsync();
        repository.Add(NewCase("0002"));

        Assert.Single(snapshot);                       // earlier snapshot unchanged
        Assert.Equal(2, (await repository.GetAllAsync()).Count); // repo reflects the add
    }

    // ---- Concurrency ------------------------------------------------------

    [Fact]
    public void Add_ConcurrentDuplicateAttempts_StoresExactlyOneCase()
    {
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
    }
}
