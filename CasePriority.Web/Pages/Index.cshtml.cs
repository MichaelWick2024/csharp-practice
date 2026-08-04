using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasePriority.Web.Pages;

public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Cases/Index");
}
