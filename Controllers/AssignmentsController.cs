using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin,Teacher")]
public class AssignmentsController : Controller
{
    private readonly ApplicationDbContext _context;

    public AssignmentsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> CanManageSubjectAsync(int? subjectId)
    {
        if (User.IsInRole("Admin")) return true;
        if (subjectId == null) return false;

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return false;

        return await _context.SubjectTeachers.AnyAsync(st => st.TeacherId == teacher.Id && st.SubjectId == subjectId);
    }

    private async Task<List<int>> GetEligibleStudentIdsAsync(int subjectId)
    {
        var classIds = await _context.SubjectClasses
            .Where(sc => sc.SubjectId == subjectId)
            .Select(sc => sc.ClassId)
            .ToListAsync();

        var classStudentIds = await _context.Students
            .Where(s => s.ClassId != null && classIds.Contains(s.ClassId.Value))
            .Select(s => s.Id)
            .ToListAsync();

        var individualStudentIds = await _context.StudentSubjects
            .Where(ss => ss.SubjectId == subjectId)
            .Select(ss => ss.StudentId)
            .ToListAsync();

        return classStudentIds.Union(individualStudentIds).ToList();
    }

    // Teachers may only give assignments to grades assigned to them; Admins to any grade.
    private async Task<List<int>> GetAllowedClassIdsAsync()
    {
        if (User.IsInRole("Admin"))
        {
            return await _context.Classes.Select(c => c.Id).ToListAsync();
        }

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return new List<int>();

        return await _context.TeacherClasses.Where(tc => tc.TeacherId == teacher.Id).Select(tc => tc.ClassId).ToListAsync();
    }

    private async Task ValidateGradeAsync(int? classId)
    {
        if (classId == null)
        {
            ModelState.AddModelError(nameof(Assignment.ClassId), "Choose the grade that should receive this assignment.");
        }
        else if (!(await GetAllowedClassIdsAsync()).Contains(classId.Value))
        {
            ModelState.AddModelError(nameof(Assignment.ClassId), "Choose a grade assigned to you.");
        }
    }

    // Give the assignment to every active student currently in its grade. Existing rows
    // (including students assigned individually and any recorded grades) are kept.
    private async Task<int> AssignGradeStudentsAsync(Assignment assignment)
    {
        if (assignment.ClassId == null) return 0;

        var gradeStudentIds = await _context.Students
            .Where(s => s.ClassId == assignment.ClassId && s.IsActive)
            .Select(s => s.Id)
            .ToListAsync();
        var alreadyAssigned = (await _context.AssignmentSubmissions
            .Where(s => s.AssignmentId == assignment.Id)
            .Select(s => s.StudentId)
            .ToListAsync()).ToHashSet();

        var added = 0;
        foreach (var studentId in gradeStudentIds.Where(id => !alreadyAssigned.Contains(id)))
        {
            _context.AssignmentSubmissions.Add(new AssignmentSubmission { AssignmentId = assignment.Id, StudentId = studentId });
            added++;
        }

        await _context.SaveChangesAsync();
        return added;
    }

    public async Task<IActionResult> Index()
    {
        var query = _context.Assignments.Include(a => a.Subject).Include(a => a.Class).Include(a => a.Submissions).AsQueryable();

        if (User.IsInRole("Teacher") && !User.IsInRole("Admin"))
        {
            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
            var ownedSubjectIds = teacher == null
                ? new List<int>()
                : await _context.SubjectTeachers.Where(st => st.TeacherId == teacher.Id).Select(st => st.SubjectId).ToListAsync();

            query = query.Where(a => a.SubjectId != null && ownedSubjectIds.Contains(a.SubjectId.Value));
        }

        var assignments = await query.OrderBy(a => a.DueDate).ToListAsync();
        return View(assignments);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateSubjectsAsync();
        await PopulateClassesAsync();
        await PopulateTeacherSubjectMapAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Assignment assignment)
    {
        await ValidateGradeAsync(assignment.ClassId);
        if (!ModelState.IsValid || !await CanManageSubjectAsync(assignment.SubjectId))
        {
            if (ModelState.IsValid) ModelState.AddModelError("", "You can only create assignments for subjects assigned to you.");
            await PopulateSubjectsAsync(assignment.SubjectId);
            await PopulateClassesAsync(assignment.ClassId);
            await PopulateTeacherSubjectMapAsync();
            return View(assignment);
        }

        _context.Add(assignment);
        await _context.SaveChangesAsync();
        var added = await AssignGradeStudentsAsync(assignment);

        TempData["AssignmentSuccess"] = $"\"{assignment.Title}\" created and given to {added} student(s) in the grade.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var assignment = await _context.Assignments.FindAsync(id);
        if (assignment == null) return NotFound();
        if (!await CanManageSubjectAsync(assignment.SubjectId)) return Forbid();

        await PopulateSubjectsAsync(assignment.SubjectId);
        await PopulateClassesAsync(assignment.ClassId);
        return View(assignment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Assignment assignment)
    {
        if (id != assignment.Id) return NotFound();

        var existing = await _context.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (existing == null) return NotFound();
        if (!await CanManageSubjectAsync(existing.SubjectId) || !await CanManageSubjectAsync(assignment.SubjectId))
        {
            return Forbid();
        }

        await ValidateGradeAsync(assignment.ClassId);
        if (!ModelState.IsValid)
        {
            await PopulateSubjectsAsync(assignment.SubjectId);
            await PopulateClassesAsync(assignment.ClassId);
            return View(assignment);
        }

        _context.Update(assignment);
        await _context.SaveChangesAsync();
        var added = await AssignGradeStudentsAsync(assignment);

        TempData["AssignmentSuccess"] = added > 0
            ? $"\"{assignment.Title}\" updated and given to {added} more student(s) in the grade."
            : $"\"{assignment.Title}\" updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var assignment = await _context.Assignments
            .Include(a => a.Subject)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (assignment == null) return NotFound();
        if (!await CanManageSubjectAsync(assignment.SubjectId)) return Forbid();

        return View(assignment);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var assignment = await _context.Assignments.FindAsync(id);
        if (assignment != null)
        {
            if (!await CanManageSubjectAsync(assignment.SubjectId)) return Forbid();

            _context.Assignments.Remove(assignment);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> AssignStudents(int? id, int? classId)
    {
        if (id == null) return NotFound();

        var assignment = await _context.Assignments.Include(a => a.Subject).FirstOrDefaultAsync(a => a.Id == id);
        if (assignment == null) return NotFound();
        if (!await CanManageSubjectAsync(assignment.SubjectId)) return Forbid();

        if (assignment.SubjectId == null)
        {
            TempData["AssignmentError"] = "Assign a subject to this assignment first.";
            return RedirectToAction(nameof(Index));
        }

        var eligibleIds = await GetEligibleStudentIdsAsync(assignment.SubjectId.Value);
        var studentsQuery = _context.Students
            .Include(s => s.Class)
            .Where(s => eligibleIds.Contains(s.Id));

        if (classId != null)
        {
            studentsQuery = studentsQuery.Where(s => s.ClassId == classId);
        }

        var students = await studentsQuery
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .ToListAsync();

        var assignedIds = await _context.AssignmentSubmissions
            .Where(sub => sub.AssignmentId == id)
            .Select(sub => sub.StudentId)
            .ToListAsync();

        var eligibleClassIds = await _context.Students
            .Where(s => eligibleIds.Contains(s.Id) && s.ClassId != null)
            .Select(s => s.ClassId!.Value)
            .Distinct()
            .ToListAsync();

        ViewBag.Assignment = assignment;
        ViewBag.AssignedIds = assignedIds;
        ViewBag.SelectedClassId = classId;
        ViewBag.Classes = new SelectList(
            await _context.Classes.Where(c => eligibleClassIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(),
            "Id", "Name", classId);
        return View(students);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignStudents(int id, int? classId, List<int>? selectedIds)
    {
        var assignment = await _context.Assignments.FindAsync(id);
        if (assignment == null) return NotFound();
        if (!await CanManageSubjectAsync(assignment.SubjectId)) return Forbid();
        if (assignment.SubjectId == null) return RedirectToAction(nameof(Index));

        selectedIds ??= new List<int>();

        // Scope changes to only the students that were actually shown on this (possibly filtered) page,
        // so filtering by grade level doesn't unassign students outside the current filter.
        var eligibleIds = await GetEligibleStudentIdsAsync(assignment.SubjectId.Value);
        var shownStudentIds = classId == null
            ? eligibleIds.ToHashSet()
            : (await _context.Students.Where(s => eligibleIds.Contains(s.Id) && s.ClassId == classId).Select(s => s.Id).ToListAsync()).ToHashSet();

        var existingSubmissions = await _context.AssignmentSubmissions.Where(s => s.AssignmentId == id).ToListAsync();

        // Only remove submissions that were shown, unselected, and haven't been touched yet, to avoid destroying recorded grades.
        _context.AssignmentSubmissions.RemoveRange(
            existingSubmissions.Where(s => shownStudentIds.Contains(s.StudentId) && !selectedIds.Contains(s.StudentId) && s.Status == "Assigned" && s.Grade == null));

        var existingStudentIds = existingSubmissions.Select(s => s.StudentId).ToHashSet();
        foreach (var studentId in selectedIds.Where(sid => shownStudentIds.Contains(sid) && !existingStudentIds.Contains(sid)))
        {
            _context.AssignmentSubmissions.Add(new AssignmentSubmission { AssignmentId = id, StudentId = studentId });
        }

        await _context.SaveChangesAsync();
        TempData["AssignmentSuccess"] = $"Students updated for {assignment.Title}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Grade(int? id)
    {
        if (id == null) return NotFound();

        var assignment = await _context.Assignments.Include(a => a.Subject).FirstOrDefaultAsync(a => a.Id == id);
        if (assignment == null) return NotFound();
        if (!await CanManageSubjectAsync(assignment.SubjectId)) return Forbid();

        var rows = await _context.AssignmentSubmissions
            .Include(s => s.Student)
            .Where(s => s.AssignmentId == id)
            .OrderBy(s => s.Student!.LastName).ThenBy(s => s.Student!.FirstName)
            .Select(s => new GradeSubmissionRow
            {
                SubmissionId = s.Id,
                StudentName = s.Student!.FullName,
                Status = s.Status,
                Grade = s.Grade,
                Feedback = s.Feedback
            })
            .ToListAsync();

        ViewBag.Assignment = assignment;
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Grade(int id, List<GradeSubmissionRow> submissions)
    {
        var assignment = await _context.Assignments.FindAsync(id);
        if (assignment == null) return NotFound();
        if (!await CanManageSubjectAsync(assignment.SubjectId)) return Forbid();

        var submissionIds = submissions.Select(r => r.SubmissionId).ToList();
        var entities = await _context.AssignmentSubmissions
            .Where(s => s.AssignmentId == id && submissionIds.Contains(s.Id))
            .ToListAsync();

        foreach (var row in submissions)
        {
            var entity = entities.FirstOrDefault(e => e.Id == row.SubmissionId);
            if (entity == null) continue;

            entity.Status = row.Status;
            entity.Grade = row.Grade;
            entity.Feedback = row.Feedback;
            if (row.Status == "Submitted" && entity.SubmittedOn == null)
            {
                entity.SubmittedOn = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        TempData["AssignmentSuccess"] = "Grades saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateSubjectsAsync(int? selectedId = null)
    {
        var query = _context.Subjects.Where(s => s.IsActive).AsQueryable();

        if (User.IsInRole("Teacher") && !User.IsInRole("Admin"))
        {
            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
            var ownedSubjectIds = teacher == null
                ? new List<int>()
                : await _context.SubjectTeachers.Where(st => st.TeacherId == teacher.Id).Select(st => st.SubjectId).ToListAsync();

            query = query.Where(s => ownedSubjectIds.Contains(s.Id));
        }

        ViewBag.Subjects = new SelectList(await query.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", selectedId);
    }

    private async Task PopulateClassesAsync(int? selectedId = null)
    {
        var allowedIds = await GetAllowedClassIdsAsync();
        ViewBag.Classes = new SelectList(
            await _context.Classes.Where(c => allowedIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(),
            "Id", "Name", selectedId);
    }

    private async Task PopulateTeacherSubjectMapAsync()
    {
        if (!User.IsInRole("Admin"))
        {
            ViewBag.Teachers = new SelectList(Enumerable.Empty<Teacher>(), "Id", "FullName");
            ViewBag.TeacherSubjectIds = new Dictionary<int, List<int>>();
            return;
        }

        var teachers = await _context.Teachers.OrderBy(t => t.FirstName).ToListAsync();
        ViewBag.Teachers = new SelectList(teachers, "Id", "FullName");

        var subjectTeachers = await _context.SubjectTeachers.ToListAsync();
        ViewBag.TeacherSubjectIds = subjectTeachers
            .GroupBy(st => st.TeacherId)
            .ToDictionary(g => g.Key, g => g.Select(st => st.SubjectId).ToList());
    }
}

public class GradeSubmissionRow
{
    public int SubmissionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Status { get; set; } = "Assigned";
    public decimal? Grade { get; set; }
    public string? Feedback { get; set; }
}
