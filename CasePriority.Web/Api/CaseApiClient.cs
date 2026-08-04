using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CasePriority.Web.Api.Contracts;

namespace CasePriority.Web.Api;

/// <summary>
/// Typed HTTP client for the case API. Expected API statuses (400/401/403/404/
/// 409/412/428) become <see cref="ApiResult{T}"/> values with parsed Problem
/// Details; only network failure, timeout, or an invalid JSON contract throw.
/// </summary>
public sealed class CaseApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<IReadOnlyList<CaseDto>>> GetAllAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<CaseDto>>(new HttpRequestMessage(HttpMethod.Get, "/api/cases"), cancellationToken);

    public Task<ApiResult<CaseDto>> GetByNumberAsync(string caseNumber, CancellationToken cancellationToken) =>
        SendAsync<CaseDto>(new HttpRequestMessage(HttpMethod.Get, CasePath(caseNumber)), cancellationToken);

    public Task<ApiResult<CaseDto>> CreateAsync(CreateCaseDto request, CancellationToken cancellationToken) =>
        SendAsync<CaseDto>(
            new HttpRequestMessage(HttpMethod.Post, "/api/cases") { Content = JsonContent.Create(request) },
            cancellationToken);

    public Task<ApiResult<CaseDto>> CloseAsync(string caseNumber, string entityTag, CancellationToken cancellationToken) =>
        PatchAsync($"{CasePath(caseNumber)}/close", entityTag, body: null, cancellationToken);

    public Task<ApiResult<CaseDto>> ReopenAsync(string caseNumber, string entityTag, CancellationToken cancellationToken) =>
        PatchAsync($"{CasePath(caseNumber)}/reopen", entityTag, body: null, cancellationToken);

    public Task<ApiResult<CaseDto>> EscalateAsync(string caseNumber, string entityTag, CancellationToken cancellationToken) =>
        PatchAsync($"{CasePath(caseNumber)}/escalate", entityTag, body: null, cancellationToken);

    public Task<ApiResult<CaseDto>> ChangeSeverityAsync(
        string caseNumber, string entityTag, ChangeSeverityDto request, CancellationToken cancellationToken) =>
        PatchAsync($"{CasePath(caseNumber)}/severity", entityTag, request, cancellationToken);

    private static string CasePath(string caseNumber) => $"/api/cases/{Uri.EscapeDataString(caseNumber)}";

    private Task<ApiResult<CaseDto>> PatchAsync(
        string path, string entityTag, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path);
        // Send the exact ETag the API returned.
        request.Headers.TryAddWithoutValidation("If-Match", entityTag);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return SendAsync<CaseDto>(request, cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Network failure / timeout propagate; the caller renders a friendly message.
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var entityTag = response.Headers.ETag?.Tag;

        if (response.IsSuccessStatusCode)
        {
            // Invalid JSON contract will throw here, which is intentional.
            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
            return new ApiResult<T>(response.StatusCode, value, Problem: null, entityTag);
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);
        return new ApiResult<T>(response.StatusCode, Value: default, problem, entityTag);
    }

    private static async Task<ApiProblemDto?> TryReadProblemAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblemDto>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            return null; // a non-JSON error body is not fatal to error handling
        }
    }
}
