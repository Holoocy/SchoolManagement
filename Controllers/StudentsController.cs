using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin")]
public class StudentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly StudentCsvImporter _importer;

    public StudentsController(ApplicationDbContext context, IWebHostEnvironment environment, StudentCsvImporter importer)
    {
        _context = context;
        _environment = environment;
        _importer = importer;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var query = _context.Students.Include(s => s.Class).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                s.FirstName.Contains(term) ||
                s.LastName.Contains(term) ||
                s.AdmissionNumber.Contains(term));
        }

        var students = await query
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToListAsync();

        ViewBag.Search = search;
        return View(students);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateClassesAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Student student, IFormFile? photoFile)
    {
        student.AdmissionNumber = await GenerateAdmissionNumberAsync();
        ModelState.Remove(nameof(Student.AdmissionNumber));

        if (!ModelState.IsValid)
        {
            await PopulateClassesAsync(student.ClassId);
            return View(student);
        }

        if (photoFile != null && photoFile.Length > 0)
        {
            student.PhotoPath = await SavePhotoAsync(photoFile);
        }

        _context.Add(student);
        await _context.SaveChangesAsync();
        TempData["StudentSuccess"] = $"{student.FullName} enrolled with admission number {student.AdmissionNumber}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ImportStudents()
    {
        await PopulateClassesAsync();
        return View(new StudentImportViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(StudentCsvImporter.MaxFileBytes + 64 * 1024)]
    public async Task<IActionResult> ImportStudents(IFormFile? file, int? defaultClassId)
    {
        var result = await _importer.ImportAsync(file, allowedClassIds: null, defaultClassId, gradeRequired: false);
        if (!result.Succeeded)
        {
            await PopulateClassesAsync(defaultClassId);
            return View(new StudentImportViewModel { DefaultClassId = defaultClassId, Errors = result.Errors });
        }

        TempData["StudentSuccess"] = $"{result.Students.Count} student{(result.Students.Count == 1 ? "" : "s")} imported from the CSV file.";
        return RedirectToAction(nameof(Index));
    }

    public IActionResult ImportTemplate() =>
        File(StudentCsvImporter.TemplateFile(), "text/csv", "students-template.csv");

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(m => m.Id == id);
        if (student == null)
        {
            return NotFound();
        }

        return View(student);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var student = await _context.Students.FindAsync(id);
        if (student == null)
        {
            return NotFound();
        }

        await PopulateClassesAsync(student.ClassId);
        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Student student, IFormFile? photoFile)
    {
        if (id != student.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await PopulateClassesAsync(student.ClassId);
            return View(student);
        }

        try
        {
            var existing = await _context.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            student.UserId = existing?.UserId;

            if (photoFile != null && photoFile.Length > 0)
            {
                student.PhotoPath = await SavePhotoAsync(photoFile);
            }
            else
            {
                student.PhotoPath = existing?.PhotoPath;
            }

            _context.Update(student);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!StudentExists(student.Id))
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

        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(m => m.Id == id);
        if (student == null)
        {
            return NotFound();
        }

        return View(student);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var student = await _context.Students.FindAsync(id);
        if (student != null)
        {
            _context.Students.Remove(student);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Transfer(int? id)
    {
        if (id == null) return NotFound();

        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.Id == id);
        if (student == null) return NotFound();

        await PopulateClassesAsync(student.ClassId);
        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(int id, int? newClassId)
    {
        var student = await _context.Students.FindAsync(id);
        if (student == null) return NotFound();

        student.ClassId = newClassId;
        await _context.SaveChangesAsync();

        TempData["StudentSuccess"] = $"{student.FullName} has been transferred.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var student = await _context.Students.FindAsync(id);
        if (student == null) return NotFound();

        student.IsActive = !student.IsActive;
        await _context.SaveChangesAsync();

        TempData["StudentSuccess"] = $"{student.FullName} has been {(student.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> IdCard(int? id)
    {
        if (id == null) return NotFound();

        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.Id == id);
        if (student == null) return NotFound();

        return View(student);
    }

    private bool StudentExists(int id)
    {
        return _context.Students.Any(e => e.Id == id);
    }

    private async Task PopulateClassesAsync(int? selectedId = null)
    {
        ViewBag.Classes = new SelectList(
            await _context.Classes.OrderBy(c => c.Name).ToListAsync(),
            "Id", "Name", selectedId);
    }

    private async Task<string> GenerateAdmissionNumberAsync()
    {
        string code;
        do
        {
            code = Random.Shared.Next(100000, 999999).ToString();
        } while (await _context.Students.AnyAsync(s => s.AdmissionNumber == code));

        return code;
    }

    private async Task<string> SavePhotoAsync(IFormFile photoFile)
    {
        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "students");
        Directory.CreateDirectory(uploadsFolder);

        var extension = Path.GetExtension(photoFile.FileName);
        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await photoFile.CopyToAsync(stream);
        }

        return $"/uploads/students/{fileName}";
    }
}
