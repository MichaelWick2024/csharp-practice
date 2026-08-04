using CasePriority.Api.Contracts;
using CasePriority.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace CasePriority.Api.Controllers;

/// <summary>
/// HTTP surface for cases. Does only HTTP work: receive a request, call the
/// service, map the domain object to a response DTO, choose the HTTP result.
/// No repositories, no searching, no business validation, no manual try/catch.
/// </summary>
[ApiController]
[Route("api/cases")]
public sealed class CasesController : ControllerBase
{
    private readonly CaseService _caseService;

    public CasesController(CaseService caseService)
    {
        ArgumentNullException.ThrowIfNull(caseService);
        _caseService = caseService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CaseResponse>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CaseResponse>> GetAll()
    {
        var response = _caseService
            .GetAllCases()
            .Select(CaseResponse.FromDomain)
            .ToList();

        return Ok(response);
    }

    [HttpGet("{caseNumber}")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<CaseResponse> GetByCaseNumber(string caseNumber)
    {
        var supportCase = _caseService.GetCaseByNumber(caseNumber);

        return Ok(CaseResponse.FromDomain(supportCase));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<CaseResponse> Create(CreateCaseRequest request)
    {
        var created = _caseService.CreateCase(
            request.CaseNumber,
            request.Subject,
            request.Severity);

        var response = CaseResponse.FromDomain(created);

        return CreatedAtAction(
            nameof(GetByCaseNumber),
            new { caseNumber = created.CaseNumber },
            response);
    }
}
