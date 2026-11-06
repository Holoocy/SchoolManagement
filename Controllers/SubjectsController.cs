using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin")]
public class SubjectsController : Controller
{
    private readonly ApplicationDbContext _context;

    public SubjectsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var subjects = await _context.Subjects
            .OrderBy(s => s.Name)
            .ToListAsync();

        return View(subjects);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Subject subject)
    {
        if (!ModelState.IsValid)
        {
            return View(subject);
        }

        _context.Add(subject);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var subject = await _context.Subjects.FirstOrDefaultAsync(m => m.Id == id);
        if (subject == null)
        {
            return NotFound();
        }

        ViewBag.Teachers = await _context.SubjectTeachers
            .Where(st => st.SubjectId == id)
            .Include(st => st.Teacher)
            .Select(st => st.Teacher!.FullName)
            .ToListAsync();

        ViewBag.Classes = await _context.SubjectClasses
            .Where(sc => sc.SubjectId == id)
            .Include(sc => sc.Class)
            .Select(sc => sc.Class!.Name)
            .ToListAsync();

        ViewBag.Students = await _context.StudentSubjects
            .Where(ss => ss.SubjectId == id)
            .Include(ss => ss.Student)
            .Select(ss => ss.Student!.FullName)
            .ToListAsync();

        return View(subject);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null)
        {
            return NotFound();
        }

        return View(subject);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Subject subject)
    {
        if (id != subject.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(subject);
        }

        try
        {
            _context.Update(subject);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!SubjectExists(subject.Id))
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

        var subject = await _context.Subjects.FirstOrDefaultAsync(m => m.Id == id);
        if (subject == null)
        {
            return NotFound();
        }

        return View(subject);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var subject = await _context.Subjects.FindAsync(id);
        if (subject != null)
        {
            _context.Subjects.Remove(subject);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> AssignTeachers(int? id)
    {
        if (id == null) return NotFound();

        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null) return NotFound();

        var assignedIds = await _context.SubjectTeachers
            .Where(st => st.SubjectId == id)
            .Select(st => st.TeacherId)
            .ToListAsync();

        ViewBag.Subject = subject;
        ViewBag.AssignedIds = assignedIds;
        var teachers = await _context.Teachers.OrderBy(t => t.LastName).ThenBy(t => t.FirstName).ToListAsync();
        return View(teachers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTeachers(int id, List<int>? selectedIds)
    {
        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null) return NotFound();

        selectedIds ??= new List<int>();

        var existingLinks = await _context.SubjectTeachers.Where(st => st.SubjectId == id).ToListAsync();
        _context.SubjectTeachers.RemoveRange(existingLinks.Where(l => !selectedIds.Contains(l.TeacherId)));

        var existingTeacherIds = existingLinks.Select(l => l.TeacherId).ToHashSet();
        foreach (var teacherId in selectedIds.Where(tid => !existingTeacherIds.Contains(tid)))
        {
            _context.SubjectTeachers.Add(new SubjectTeacher { SubjectId = id, TeacherId = teacherId });
        }

        await _context.SaveChangesAsync();
        TempData["SubjectSuccess"] = $"Teachers updated for {subject.Name}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> AssignClasses(int? id)
    {
        if (id == null) return NotFound();

        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null) return NotFound();

        var assignedIds = await _context.SubjectClasses
            .Where(sc => sc.SubjectId == id)
            .Select(sc => sc.ClassId)
            .ToListAsync();

        ViewBag.Subject = subject;
        ViewBag.AssignedIds = assignedIds;
        var classes = await _context.Classes.OrderBy(c => c.Name).ToListAsync();
        return View(classes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignClasses(int id, List<int>? selectedIds)
    {
        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null) return NotFound();

        selectedIds ??= new List<int>();

        var existingLinks = await _context.SubjectClasses.Where(sc => sc.SubjectId == id).ToListAsync();
        _context.SubjectClasses.RemoveRange(existingLinks.Where(l => !selectedIds.Contains(l.ClassId)));

        var existingClassIds = existingLinks.Select(l => l.ClassId).ToHashSet();
        foreach (var classId in selectedIds.Where(cid => !existingClassIds.Contains(cid)))
        {
            _context.SubjectClasses.Add(new SubjectClass { SubjectId = id, ClassId = classId });
        }

        await _context.SaveChangesAsync();
        TempData["SubjectSuccess"] = $"Classes updated for {subject.Name}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> AssignStudents(int? id, int? classId)
    {
        if (id == null) return NotFound();

        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null) return NotFound();

        var assignedIds = await _context.StudentSubjects
            .Where(ss => ss.SubjectId == id)
            .Select(ss => ss.StudentId)
            .ToListAsync();

        var studentsQuery = _context.Students.Include(s => s.Class).AsQueryable();
        if (classId != null)
        {
            studentsQuery = studentsQuery.Where(s => s.ClassId == classId);
        }

        var students = await studentsQuery
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .ToListAsync();

        ViewBag.Subject = subject;
        ViewBag.AssignedIds = assignedIds;
        ViewBag.SelectedClassId = classId;
        ViewBag.Classes = new SelectList(await _context.Classes.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", classId);
        return View(students);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignStudents(int id, int? classId, List<int>? selectedIds)
    {
        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null) return NotFound();

        selectedIds ??= new List<int>();

        // Scope changes to only the students shown on this (possibly filtered) page,
        // so filtering by grade level doesn't unassign students outside the current filter.
        var shownStudentIds = classId == null
            ? (await _context.Students.Select(s => s.Id).ToListAsync()).ToHashSet()
            : (await _context.Students.Where(s => s.ClassId == classId).Select(s => s.Id).ToListAsync()).ToHashSet();

        var existingLinks = await _context.StudentSubjects.Where(ss => ss.SubjectId == id).ToListAsync();
        _context.StudentSubjects.RemoveRange(
            existingLinks.Where(l => shownStudentIds.Contains(l.StudentId) && !selectedIds.Contains(l.StudentId)));

        var existingStudentIds = existingLinks.Select(l => l.StudentId).ToHashSet();
        foreach (var studentId in selectedIds.Where(sid => shownStudentIds.Contains(sid) && !existingStudentIds.Contains(sid)))
        {
            _context.StudentSubjects.Add(new StudentSubject { SubjectId = id, StudentId = studentId });
        }

        await _context.SaveChangesAsync();
        TempData["SubjectSuccess"] = $"Students updated for {subject.Name}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private bool SubjectExists(int id)
    {
        return _context.Subjects.Any(e => e.Id == id);
    }
}
