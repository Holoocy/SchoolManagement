using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin")]
public class ClassesController : Controller
{
    private readonly ApplicationDbContext _context;

    public ClassesController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var classes = await _context.Classes
            .OrderBy(c => c.Name)
            .Select(c => new ClassListItem
            {
                Id = c.Id,
                Name = c.Name,
                StudentCount = c.Students.Count,
                TeacherCount = c.TeacherClasses.Count
            })
            .ToListAsync();

        return View(classes);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Class @class)
    {
        if (!ModelState.IsValid)
        {
            return View(@class);
        }

        _context.Add(@class);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var @class = await _context.Classes.FindAsync(id);
        if (@class == null) return NotFound();

        return View(@class);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Class @class)
    {
        if (id != @class.Id) return NotFound();
        if (!ModelState.IsValid) return View(@class);

        _context.Update(@class);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var @class = await _context.Classes.FirstOrDefaultAsync(c => c.Id == id);
        if (@class == null) return NotFound();

        return View(@class);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var @class = await _context.Classes.FindAsync(id);
        if (@class != null)
        {
            _context.Classes.Remove(@class);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> TransferStudents(int? classId)
    {
        var classes = await _context.Classes.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Classes = new SelectList(classes, "Id", "Name", classId);
        ViewBag.SourceClassId = classId;

        if (classId == null)
        {
            return View(new List<Student>());
        }

        ViewBag.DestinationClasses = new SelectList(classes.Where(c => c.Id != classId), "Id", "Name");

        var students = await _context.Students
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .ToListAsync();

        return View(students);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferStudents(int sourceClassId, int? destinationClassId, List<int>? selectedIds)
    {
        if (destinationClassId == null || destinationClassId == sourceClassId)
        {
            TempData["ClassError"] = "Choose a different destination grade.";
            return RedirectToAction(nameof(TransferStudents), new { classId = sourceClassId });
        }

        selectedIds ??= new List<int>();
        var students = await _context.Students
            .Where(s => s.ClassId == sourceClassId && selectedIds.Contains(s.Id))
            .ToListAsync();

        foreach (var student in students)
        {
            student.ClassId = destinationClassId;
        }

        await _context.SaveChangesAsync();
        TempData["ClassSuccess"] = $"{students.Count} student(s) transferred.";
        return RedirectToAction(nameof(Index));
    }
}

public class ClassListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StudentCount { get; set; }
    public int TeacherCount { get; set; }
}
