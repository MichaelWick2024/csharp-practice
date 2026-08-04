using System.ComponentModel.DataAnnotations;
using System.Net;
using CasePriority.Web.Api;
using CasePriority.Web.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasePriority.Web.Pages.Cases;

[Authorize]
public sealed class DetailsModel(CaseApiClient apiClient) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string CaseNumber { get; set; } = string.Empty;

    // The exact ETag last returned by the API, round-tripped through the form.
    [BindProperty]
    public string EntityTag { get; set; } = string.Empty;

    [BindProperty]
    [Range(1, 5, ErrorMessage = "Severity must be between 1 and 5.")]
    public int NewSeverity { get; set; } = 1;

    public CaseDto? Case { get; private set; }
    public string? ErrorMessage { get; private set; }

    public bool CanManage => User.IsInRole("CaseManager") || User.IsInRole("Administrator");

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public Task<IActionResult> OnPostCloseAsync(CancellationToken ct) =>
        MutateAsync((number, tag) => apiClient.CloseAsync(number, tag, ct), ct);

    public Task<IActionResult> OnPostReopenAsync(CancellationToken ct) =>
        MutateAsync((number, tag) => apiClient.ReopenAsync(number, tag, ct), ct);

    public Task<IActionResult> OnPostEscalateAsync(CancellationToken ct) =>
        MutateAsync((number, tag) => apiClient.EscalateAsync(number, tag, ct), ct);

    public async Task<IActionResult> OnPostChangeSeverityAsync(CancellationToken ct)
    {
        // A crafted post (e.g. NewSeverity=abc) fails binding and leaves the int at
        // its default (1); reject it here rather than silently changing the case.
        if (!ModelState.IsValid)
        {
            return await LoadAsync(ct);
        }

        return await MutateAsync(
            (number, tag) => apiClient.ChangeSeverityAsync(number, tag, new ChangeSeverityDto(NewSeverity), ct), ct);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await apiClient.GetByNumberAsync(CaseNumber, cancellationToken);
            if (result.IsSuccess && result.Value is not null)
            {
                Case = result.Value;
                EntityTag = result.EntityTag ?? $"\"{result.Value.Version}\"";
                return Page();
            }

            if (result.StatusCode == HttpStatusCode.NotFound)
            {
                return NotFound();
            }

            ErrorMessage ??= UiMessages.ForStatus(result.StatusCode, result.Problem);
            return Page();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage ??= UiMessages.Unavailable;
            return Page();
        }
    }

    private async Task<IActionResult> MutateAsync(
        Func<string, string, Task<ApiResult<CaseDto>>> mutation, CancellationToken cancellationToken)
    {
        // The API is the final authority, but check here too rather than relying
        // only on hidden buttons.
        if (!CanManage)
        {
            return Forbid();
        }

        try
        {
            var result = await mutation(CaseNumber, EntityTag);

            if (result.IsSuccess && result.Value is not null)
            {
                TempData["SuccessMessage"] =
                    $"Case {result.Value.CaseNumber} updated to version {result.Value.Version}.";
                return RedirectToPage("./Details", new { caseNumber = CaseNumber });
            }

            if (result.StatusCode == HttpStatusCode.Forbidden)
            {
                return Forbid();
            }

            if (result.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Stale: reload latest and prompt review — never auto-retry.
                TempData["ErrorMessage"] = UiMessages.StaleVersion;
                return RedirectToPage("./Details", new { caseNumber = CaseNumber });
            }

            // 409 / 401 / 428 / other: stay on the page with a message + latest state.
            ErrorMessage = UiMessages.ForStatus(result.StatusCode, result.Problem);
            return await LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = UiMessages.Unavailable;
            return await LoadAsync(cancellationToken);
        }
    }
}
