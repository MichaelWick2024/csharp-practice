using CasePriority.Web.Api;
using CasePriority.Web.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasePriority.Web.Pages.Cases;

[Authorize]
public sealed class IndexModel(CaseApiClient apiClient) : PageModel
{
    public IReadOnlyList<CaseDto> Cases { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await apiClient.GetAllAsync(cancellationToken);
            if (result.IsSuccess)
            {
                Cases = result.Value ?? [];
            }
            else
            {
                ErrorMessage = UiMessages.ForStatus(result.StatusCode, result.Problem);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = UiMessages.Unavailable;
        }
    }
}
