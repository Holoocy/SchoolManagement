using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin,Teacher")]
public class WeeklyChecklistController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public WeeklyChecklistController(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task PopulateTeacherNamesAsync(string? selected = null)
    {
        List<string> names;
        if (User.IsInRole("Admin"))
        {
            names = await _context.Teachers.OrderBy(t => t.FirstName).ThenBy(t => t.LastName)
                .Select(t => t.FirstName + " " + t.LastName).ToListAsync();
        }
        else
        {
            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
            names = teacher != null ? new List<string> { teacher.FullName } : new List<string>();
        }

        ViewBag.TeacherNames = new SelectList(names, selected);
    }

    private bool CanSeeDocument(WeeklyChecklistItem item) =>
        item.DocumentPath != null &&
        (item.DocumentVisibility == ChecklistDocumentVisibility.Admin || item.UploadedByUserId == CurrentUserId);

    private async Task<string?> GetOwnTeacherNameIfTeacherAsync()
    {
        if (User.IsInRole("Admin")) return null;

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        return teacher?.FullName;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _context.WeeklyChecklistItems
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.Status)
            .ToListAsync();

        ViewBag.VisibleDocumentIds = items.Where(CanSeeDocument).Select(i => i.Id).ToHashSet();
        return View(items);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateTeacherNamesAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WeeklyChecklistItem item, IFormFile? documentFile)
    {
        var ownTeacherName = await GetOwnTeacherNameIfTeacherAsync();
        if (ownTeacherName != null)
        {
            item.AssignedTo = ownTeacherName;
            ModelState.Remove(nameof(WeeklyChecklistItem.AssignedTo));
        }

        if (!ModelState.IsValid)
        {
            await PopulateTeacherNamesAsync(item.AssignedTo);
            return View(item);
        }

        if (documentFile != null && documentFile.Length > 0)
        {
            item.DocumentPath = await SaveDocumentAsync(documentFile);
            item.UploadedByUserId = CurrentUserId;
        }
        else
        {
            item.DocumentVisibility = ChecklistDocumentVisibility.TeacherOnly;
        }

        _context.Add(item);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var item = await _context.WeeklyChecklistItems.FirstOrDefaultAsync(m => m.Id == id);
        if (item == null)
        {
            return NotFound();
        }

        ViewBag.CanSeeDocument = CanSeeDocument(item);
        return View(item);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var item = await _context.WeeklyChecklistItems.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }

        await PopulateTeacherNamesAsync(item.AssignedTo);
        ViewBag.CanSeeDocument = CanSeeDocument(item);
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, WeeklyChecklistItem item, IFormFile? documentFile)
    {
        if (id != item.Id)
        {
            return NotFound();
        }

        var ownTeacherName = await GetOwnTeacherNameIfTeacherAsync();
        if (ownTeacherName != null)
        {
            item.AssignedTo = ownTeacherName;
            ModelState.Remove(nameof(WeeklyChecklistItem.AssignedTo));
        }

        if (!ModelState.IsValid)
        {
            await PopulateTeacherNamesAsync(item.AssignedTo);
            return View(item);
        }

        try
        {
            var existing = await _context.WeeklyChecklistItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);

            if (documentFile != null && documentFile.Length > 0)
            {
                item.DocumentPath = await SaveDocumentAsync(documentFile);
                item.UploadedByUserId = CurrentUserId;
            }
            else
            {
                item.DocumentPath = existing?.DocumentPath;
                item.UploadedByUserId = existing?.UploadedByUserId;
                if (item.DocumentPath == null)
                {
                    item.DocumentVisibility = ChecklistDocumentVisibility.TeacherOnly;
                }
            }

            _context.Update(item);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!WeeklyChecklistItemExists(item.Id))
            {
                return NotFound();
            }

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var item = await _context.WeeklyChecklistItems.FirstOrDefaultAsync(m => m.Id == id);
        if (item == null)
        {
            return NotFound();
        }

        return View(item);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var item = await _context.WeeklyChecklistItems.FindAsync(id);
        if (item != null)
        {
            _context.WeeklyChecklistItems.Remove(item);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    private bool WeeklyChecklistItemExists(int id)
    {
        return _context.WeeklyChecklistItems.Any(e => e.Id == id);
    }

    private async Task<string> SaveDocumentAsync(IFormFile documentFile)
    {
        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "checklist");
        Directory.CreateDirectory(uploadsFolder);

        var extension = Path.GetExtension(documentFile.FileName);
        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await documentFile.CopyToAsync(stream);
        }

        return $"/uploads/checklist/{fileName}";
    }
}
