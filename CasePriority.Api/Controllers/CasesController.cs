using CasePriority.Api.Contracts;
using CasePriority.Api.Http;
using CasePriority.Core.Domain;
using CasePriority.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace CasePriority.Api.Controllers;

/// <summary>
/// HTTP surface for cases. Reads/writes go through <see cref="CaseService"/>;
/// the controller only does HTTP work: DTO mapping, status codes, and the
/// ETag / If-Match conditional-request plumbing for optimistic concurrency.
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
        // A collection response doesn't carry a single ETag.
        var response = _caseService
            .GetAllCases()
            .Select(CaseResponse.FromSnapshot)
            .ToList();

        return Ok(response);
    }

    [HttpGet("{caseNumber}")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<CaseResponse> GetByCaseNumber(string caseNumber)
    {
        var snapshot = _caseService.GetCaseByNumber(caseNumber);

        Response.Headers[HeaderNames.ETag] = EntityTagVersion.Format(snapshot.Version);
        return Ok(CaseResponse.FromSnapshot(snapshot));
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

        Response.Headers[HeaderNames.ETag] = EntityTagVersion.Format(created.Version);

        return CreatedAtAction(
            nameof(GetByCaseNumber),
            new { caseNumber = created.CaseNumber },
            CaseResponse.FromSnapshot(created));
    }

    [HttpPatch("{caseNumber}/close")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public ActionResult<CaseResponse> Close(
        string caseNumber,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = _caseService.CloseCase(caseNumber, expectedVersion);
        return Updated(updated);
    }

    [HttpPatch("{caseNumber}/reopen")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public ActionResult<CaseResponse> Reopen(
        string caseNumber,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = _caseService.ReopenCase(caseNumber, expectedVersion);
        return Updated(updated);
    }

    [HttpPatch("{caseNumber}/escalate")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public ActionResult<CaseResponse> Escalate(
        string caseNumber,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = _caseService.EscalateCase(caseNumber, expectedVersion);
        return Updated(updated);
    }

    [HttpPatch("{caseNumber}/severity")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public ActionResult<CaseResponse> ChangeSeverity(
        string caseNumber,
        ChangeSeverityRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = _caseService.ChangeCaseSeverity(caseNumber, request.Severity, expectedVersion);
        return Updated(updated);
    }

    // Shared success path for mutations: emit the new version as the ETag.
    private ActionResult<CaseResponse> Updated(SupportCaseSnapshot snapshot)
    {
        Response.Headers[HeaderNames.ETag] = EntityTagVersion.Format(snapshot.Version);
        return Ok(CaseResponse.FromSnapshot(snapshot));
    }
}
