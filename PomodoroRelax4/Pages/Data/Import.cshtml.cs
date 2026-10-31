using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PomodoroRelax4.Services;

namespace PomodoroRelax4.Pages.Data;

// A little over the file limit, for the multipart envelope and antiforgery field
[Authorize]
[RequestSizeLimit(ImportService.MaxFileBytes + 64 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = ImportService.MaxFileBytes + 64 * 1024)]
public class ImportModel : PageModel
{
    private readonly IImportService _importService;
    private readonly UserManager<IdentityUser> _userManager;

    public ImportModel(IImportService importService, UserManager<IdentityUser> userManager)
    {
        _importService = importService;
        _userManager = userManager;
    }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    public string? ErrorMessage { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
            return RedirectToPage("/Account/Login");

        if (Upload is null || Upload.Length == 0)
        {
            ErrorMessage = "Choose a file to import.";
            return Page();
        }

        try
        {
            await using var stream = Upload.OpenReadStream();
            var data = await _importService.ParseAsync(stream, Upload.FileName);
            var result = await _importService.ImportAsync(userId, data);

            var message = $"Imported {result.TasksAdded} tasks and {result.SessionsAdded} sessions.";
            if (result.TasksSkipped + result.SessionsSkipped > 0)
                message += $" Skipped {result.TasksSkipped} tasks and {result.SessionsSkipped} sessions you already had.";
            if (result.RowsIgnored > 0)
                message += $" Ignored {result.RowsIgnored} unusable rows.";
            StatusMessage = message + " Timer settings were taken from the file.";
            return RedirectToPage();
        }
        catch (ImportException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
