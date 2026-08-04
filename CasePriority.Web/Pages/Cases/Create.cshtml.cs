using System.ComponentModel.DataAnnotations;
using System.Net;
using CasePriority.Web.Api;
using CasePriority.Web.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasePriority.Web.Pages.Cases;

[Authorize(Roles = "CaseManager,Administrator")]
public sealed class CreateModel(CaseApiClient apiClient) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        [Required]
        [StringLength(20, MinimumLength = 1)]
        public string CaseNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Subject { get; set; } = string.Empty;

        [Range(1, 5)]
        public int Severity { get; set; } = 1;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var result = await apiClient.CreateAsync(
                new CreateCaseDto(Input.CaseNumber, Input.Subject, Input.Severity), cancellationToken);

            if (result.IsSuccess && result.Value is not null)
            {
                TempData["SuccessMessage"] = $"Case {result.Value.CaseNumber} was created.";
                return RedirectToPage("./Details", new { caseNumber = result.Value.CaseNumber });
            }

            // Surface API validation errors next to the offending fields.
            if (result.StatusCode == HttpStatusCode.BadRequest && result.Problem?.Errors is { } errors)
            {
                foreach (var (field, messages) in errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(MapField(field), message);
                    }
                }
                return Page();
            }

            ErrorMessage = UiMessages.ForStatus(result.StatusCode, result.Problem);
            return Page();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = UiMessages.Unavailable;
            return Page();
        }
    }

    private static string MapField(string apiField) => apiField switch
    {
        "CaseNumber" => "Input.CaseNumber",
        "Subject" => "Input.Subject",
        "Severity" => "Input.Severity",
        _ => string.Empty,
    };
}
