using System.Net;
using CasePriority.Web.Api.Contracts;

namespace CasePriority.Web.Api;

/// <summary>
/// The outcome of an API call: a value on success, or the parsed Problem Details
/// on an expected error status. (Network/timeout/invalid-JSON throw instead.)
/// </summary>
public sealed record ApiResult<T>(
    HttpStatusCode StatusCode,
    T? Value,
    ApiProblemDto? Problem,
    string? EntityTag)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
}
