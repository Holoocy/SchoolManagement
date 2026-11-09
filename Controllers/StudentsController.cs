using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

// Admins manage every student. Teachers manage the students in the grades assigned to
// them: every lookup goes through ScopedStudentsAsync, and grade choices are limited
// to their own grades.
[Authorize(Roles = "Admin,Teacher")]
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

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

    // Null for admins (no restriction); otherwise the grades assigned to this teacher.
    private async Task<List<int>?> GetAllowedClassIdsAsync()
    {
        if (IsAdmin) return null;

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return new List<int>();

        return await _context.TeacherClasses
            .Where(tc => tc.TeacherId == teacher.Id)
            .Select(tc => tc.ClassId)
            .ToListAsync();
    }

    private async Task<IQueryable<Student>> ScopedStudentsAsync()
    {
        var allowed = await GetAllowedClassIdsAsync();
        var query = _context.Students.AsQueryable();
        return allowed == null ? query : query.Where(s => s.ClassId != null && allowed.Contains(s.ClassId.Value));
    }

    private async Task<Student?> FindScopedStudentAsync(int id, bool includeClass = false)
    {
        var query = await ScopedStudentsAsync();
        if (includeClass) query = query.Include(s => s.Class);
        return await query.FirstOrDefaultAsync(s => s.Id == id);
    }

    // Teachers must keep students in one of their own grades.
    private async Task<bool> IsAllowedClassAsync(int? classId)
    {
        var allowed = await GetAllowedClassIdsAsync();
        return allowed == null || (classId != null && allowed.Contains(classId.Value));
    }

    public async Task<IActionResult> Index(string? search, int? classId)
    {
        var allowed = await GetAllowedClassIdsAsync();
        if (allowed != null && classId != null && !allowed.Contains(classId.Value)) classId = null;

        var query = (await ScopedStudentsAsync()).Include(s => s.Class).AsQueryable();
        if (classId != null)
        {
            query = query.Where(s => s.ClassId == classId);
        }

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
        ViewBag.ClassId = classId;
        ViewBag.IsTeacher = allowed != null;
        ViewBag.HasGrades = allowed == null || allowed.Any();
        await PopulateClassesAsync(classId);
        return View(students);
    }

    public async Task<IActionResult> Create(int? classId)
    {
        var allowed = await GetAllowedClassIdsAsync();
        if (allowed != null && !allowed.Any())
        {
            TempData["StudentError"] = "You need a grade assigned to you before you can enroll students. Ask an administrator to assign one.";
            return RedirectToAction(nameof(Index));
        }

        int? preselected = allowed == null ? classId
            : classId != null && allowed.Contains(classId.Value) ? classId
            : allowed.Count == 1 ? allowed[0] : null;
        await PopulateClassesAsync(preselected);
        return View(preselected == null ? null : new Student { ClassId = preselected });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Student student, IFormFile? photoFile)
    {
        if (!await IsAllowedClassAsync(student.ClassId))
        {
            ModelState.AddModelError(nameof(Student.ClassId), "You can only enroll students into a grade assigned to you.");
        }

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
        return RedirectToAction(nameof(Index), IsAdmin ? null : new { classId = student.ClassId });
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportStudents()
    {
        await PopulateClassesAsync();
        return View(new StudentImportViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
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

    [Authorize(Roles = "Admin")]
    public IActionResult ImportTemplate() =>
        File(StudentCsvImporter.TemplateFile(), "text/csv", "students-template.csv");

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var student = await FindScopedStudentAsync(id.Value, includeClass: true);
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

        var student = await FindScopedStudentAsync(id.Value);
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

        var existing = await (await ScopedStudentsAsync()).AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null)
        {
            return NotFound();
        }

        if (!await IsAllowedClassAsync(student.ClassId))
        {
            ModelState.AddModelError(nameof(Student.ClassId), "Keep the student in a grade assigned to you.");
        }

        // The admission number is generated by the system and can't be changed here.
        student.AdmissionNumber = existing.AdmissionNumber;
        ModelState.Remove(nameof(Student.AdmissionNumber));

        if (!ModelState.IsValid)
        {
            await PopulateClassesAsync(student.ClassId);
            return View(student);
        }

        try
        {
            student.UserId = existing.UserId;

            if (photoFile != null && photoFile.Length > 0)
            {
                student.PhotoPath = await SavePhotoAsync(photoFile);
            }
            else
            {
                student.PhotoPath = existing.PhotoPath;
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

        TempData["StudentSuccess"] = $"{student.FullName}'s details have been updated.";
        return RedirectToAction(nameof(Index), IsAdmin ? null : new { classId = student.ClassId });
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var student = await FindScopedStudentAsync(id.Value, includeClass: true);
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
        var student = await FindScopedStudentAsync(id);
        if (student == null)
        {
            return NotFound();
        }

        _context.Students.Remove(student);
        await _context.SaveChangesAsync();

        TempData["StudentSuccess"] = $"{student.FullName} has been deleted.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Transfer(int? id)
    {
        if (id == null) return NotFound();

        var student = await FindScopedStudentAsync(id.Value, includeClass: true);
        if (student == null) return NotFound();

        await PopulateClassesAsync(student.ClassId);
        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(int id, int? newClassId)
    {
        var student = await FindScopedStudentAsync(id);
        if (student == null) return NotFound();

        if (!await IsAllowedClassAsync(newClassId))
        {
            TempData["StudentError"] = "You can only transfer students to a grade assigned to you.";
            return RedirectToAction(nameof(Transfer), new { id });
        }

        student.ClassId = newClassId;
        await _context.SaveChangesAsync();

        TempData["StudentSuccess"] = $"{student.FullName} has been transferred.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var student = await FindScopedStudentAsync(id);
        if (student == null) return NotFound();

        student.IsActive = !student.IsActive;
        await _context.SaveChangesAsync();

        TempData["StudentSuccess"] = $"{student.FullName} has been {(student.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> IdCard(int? id)
    {
        if (id == null) return NotFound();

        var student = await FindScopedStudentAsync(id.Value, includeClass: true);
        if (student == null) return NotFound();

        return View(student);
    }

    private bool StudentExists(int id)
    {
        return _context.Students.Any(e => e.Id == id);
    }

    private async Task PopulateClassesAsync(int? selectedId = null)
    {
        var allowed = await GetAllowedClassIdsAsync();
        var classes = _context.Classes.AsQueryable();
        if (allowed != null) classes = classes.Where(c => allowed.Contains(c.Id));

        ViewBag.Classes = new SelectList(await classes.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", selectedId);
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
