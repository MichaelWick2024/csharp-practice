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
/// Actions are async and flow the request's <see cref="CancellationToken"/>
/// down to the database.
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
    public async Task<ActionResult<IReadOnlyList<CaseResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var cases = await _caseService.GetAllCasesAsync(cancellationToken);
        return Ok(cases.Select(CaseResponse.FromSnapshot).ToList());
    }

    [HttpGet("{caseNumber}")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CaseResponse>> GetByCaseNumber(
        string caseNumber, CancellationToken cancellationToken)
    {
        var snapshot = await _caseService.GetCaseByNumberAsync(caseNumber, cancellationToken);

        Response.Headers[HeaderNames.ETag] = EntityTagVersion.Format(snapshot.Version);
        return Ok(CaseResponse.FromSnapshot(snapshot));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CaseResponse>> Create(
        CreateCaseRequest request, CancellationToken cancellationToken)
    {
        var created = await _caseService.CreateCaseAsync(
            request.CaseNumber, request.Subject, request.Severity, cancellationToken);

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
    public async Task<ActionResult<CaseResponse>> Close(
        string caseNumber,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = await _caseService.CloseCaseAsync(caseNumber, expectedVersion, cancellationToken);
        return Updated(updated);
    }

    [HttpPatch("{caseNumber}/reopen")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<CaseResponse>> Reopen(
        string caseNumber,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = await _caseService.ReopenCaseAsync(caseNumber, expectedVersion, cancellationToken);
        return Updated(updated);
    }

    [HttpPatch("{caseNumber}/escalate")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<CaseResponse>> Escalate(
        string caseNumber,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = await _caseService.EscalateCaseAsync(caseNumber, expectedVersion, cancellationToken);
        return Updated(updated);
    }

    [HttpPatch("{caseNumber}/severity")]
    [ProducesResponseType(typeof(CaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<CaseResponse>> ChangeSeverity(
        string caseNumber,
        ChangeSeverityRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagVersion.ParseRequired(ifMatch);
        var updated = await _caseService.ChangeCaseSeverityAsync(
            caseNumber, request.Severity, expectedVersion, cancellationToken);
        return Updated(updated);
    }

    // Shared success path for mutations: emit the new version as the ETag.
    private ActionResult<CaseResponse> Updated(SupportCaseSnapshot snapshot)
    {
        Response.Headers[HeaderNames.ETag] = EntityTagVersion.Format(snapshot.Version);
        return Ok(CaseResponse.FromSnapshot(snapshot));
    }
}
