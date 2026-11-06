using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin")]
public class TeachersController : Controller
{
    private readonly ApplicationDbContext _context;

    public TeachersController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var teachers = await _context.Teachers
            .OrderBy(t => t.LastName)
            .ThenBy(t => t.FirstName)
            .ToListAsync();

        return View(teachers);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Teacher teacher)
    {
        teacher.EmployeeId = await GenerateEmployeeIdAsync();
        ModelState.Remove(nameof(Teacher.EmployeeId));

        if (!ModelState.IsValid)
        {
            return View(teacher);
        }

        _context.Add(teacher);
        await _context.SaveChangesAsync();
        TempData["TeacherSuccess"] = $"{teacher.FullName} added with employee ID {teacher.EmployeeId}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var teacher = await _context.Teachers.FirstOrDefaultAsync(m => m.Id == id);
        if (teacher == null)
        {
            return NotFound();
        }

        ViewBag.Subjects = await _context.SubjectTeachers
            .Where(st => st.TeacherId == id)
            .Include(st => st.Subject)
            .Select(st => st.Subject!.Name)
            .ToListAsync();

        ViewBag.Classes = await _context.TeacherClasses
            .Where(tc => tc.TeacherId == id)
            .Include(tc => tc.Class)
            .Select(tc => tc.Class!.Name)
            .ToListAsync();

        return View(teacher);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null)
        {
            return NotFound();
        }

        return View(teacher);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Teacher teacher)
    {
        if (id != teacher.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(teacher);
        }

        try
        {
            var existing = await _context.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            teacher.UserId = existing?.UserId;

            _context.Update(teacher);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!TeacherExists(teacher.Id))
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

        var teacher = await _context.Teachers.FirstOrDefaultAsync(m => m.Id == id);
        if (teacher == null)
        {
            return NotFound();
        }

        return View(teacher);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher != null)
        {
            _context.Teachers.Remove(teacher);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> AssignSubjects(int? id)
    {
        if (id == null) return NotFound();

        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        var assignedIds = await _context.SubjectTeachers
            .Where(st => st.TeacherId == id)
            .Select(st => st.SubjectId)
            .ToListAsync();

        ViewBag.Teacher = teacher;
        ViewBag.AssignedIds = assignedIds;
        var subjects = await _context.Subjects.OrderBy(s => s.Name).ToListAsync();
        return View(subjects);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignSubjects(int id, List<int>? selectedIds)
    {
        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        selectedIds ??= new List<int>();

        var existingLinks = await _context.SubjectTeachers.Where(st => st.TeacherId == id).ToListAsync();
        _context.SubjectTeachers.RemoveRange(existingLinks.Where(l => !selectedIds.Contains(l.SubjectId)));

        var existingSubjectIds = existingLinks.Select(l => l.SubjectId).ToHashSet();
        foreach (var subjectId in selectedIds.Where(sid => !existingSubjectIds.Contains(sid)))
        {
            _context.SubjectTeachers.Add(new SubjectTeacher { TeacherId = id, SubjectId = subjectId });
        }

        await _context.SaveChangesAsync();
        TempData["TeacherSuccess"] = $"Subjects updated for {teacher.FullName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> AssignClasses(int? id)
    {
        if (id == null) return NotFound();

        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        var assignedIds = await _context.TeacherClasses
            .Where(tc => tc.TeacherId == id)
            .Select(tc => tc.ClassId)
            .ToListAsync();

        ViewBag.Teacher = teacher;
        ViewBag.AssignedIds = assignedIds;
        var classes = await _context.Classes.OrderBy(c => c.Name).ToListAsync();
        return View(classes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignClasses(int id, List<int>? selectedIds)
    {
        var teacher = await _context.Teachers.FindAsync(id);
        if (teacher == null) return NotFound();

        selectedIds ??= new List<int>();

        var existingLinks = await _context.TeacherClasses.Where(tc => tc.TeacherId == id).ToListAsync();
        _context.TeacherClasses.RemoveRange(existingLinks.Where(l => !selectedIds.Contains(l.ClassId)));

        var existingClassIds = existingLinks.Select(l => l.ClassId).ToHashSet();
        foreach (var classId in selectedIds.Where(cid => !existingClassIds.Contains(cid)))
        {
            _context.TeacherClasses.Add(new TeacherClass { TeacherId = id, ClassId = classId });
        }

        await _context.SaveChangesAsync();
        TempData["TeacherSuccess"] = $"Classes updated for {teacher.FullName}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private bool TeacherExists(int id)
    {
        return _context.Teachers.Any(e => e.Id == id);
    }

    private async Task<string> GenerateEmployeeIdAsync()
    {
        var existingIds = await _context.Teachers
            .Where(t => t.EmployeeId.StartsWith("EMP"))
            .Select(t => t.EmployeeId)
            .ToListAsync();

        var maxSuffix = existingIds
            .Select(id => int.TryParse(id.Substring(3), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"EMP{(maxSuffix + 1):D4}";
    }
}
