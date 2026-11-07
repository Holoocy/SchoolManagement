using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Teacher")]
public class TeacherPortalController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public TeacherPortalController(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<List<int>> GetMyClassIdsAsync(int teacherId)
    {
        return await _context.TeacherClasses
            .Where(tc => tc.TeacherId == teacherId)
            .Select(tc => tc.ClassId)
            .ToListAsync();
    }

    public async Task<IActionResult> MySubjects()
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return View(new List<Models.Subject>());

        var subjectIds = await _context.SubjectTeachers
            .Where(st => st.TeacherId == teacher.Id)
            .Select(st => st.SubjectId)
            .ToListAsync();

        var subjects = await _context.Subjects
            .Include(s => s.SubjectClasses).ThenInclude(sc => sc.Class)
            .Where(s => subjectIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToListAsync();

        ViewBag.Classes = await _context.TeacherClasses
            .Where(tc => tc.TeacherId == teacher.Id)
            .Include(tc => tc.Class)
            .Select(tc => tc.Class!.Name)
            .ToListAsync();

        return View(subjects);
    }

    public async Task<IActionResult> AssignStudents(int? subjectId, int? classId)
    {
        if (subjectId == null) return NotFound();

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return Forbid();

        var owns = await _context.SubjectTeachers.AnyAsync(st => st.TeacherId == teacher.Id && st.SubjectId == subjectId);
        if (!owns) return Forbid();

        var subject = await _context.Subjects.FindAsync(subjectId);
        if (subject == null) return NotFound();

        var assignedIds = await _context.StudentSubjects
            .Where(ss => ss.SubjectId == subjectId)
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

        var myClassIds = await GetMyClassIdsAsync(teacher.Id);

        ViewBag.Subject = subject;
        ViewBag.AssignedIds = assignedIds;
        ViewBag.SelectedClassId = classId;
        ViewBag.Classes = new SelectList(
            await _context.Classes.Where(c => myClassIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", classId);
        return View(students);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignStudents(int subjectId, int? classId, List<int>? selectedIds)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return Forbid();

        var owns = await _context.SubjectTeachers.AnyAsync(st => st.TeacherId == teacher.Id && st.SubjectId == subjectId);
        if (!owns) return Forbid();

        var subject = await _context.Subjects.FindAsync(subjectId);
        if (subject == null) return NotFound();

        selectedIds ??= new List<int>();

        var shownStudentIds = classId == null
            ? (await _context.Students.Select(s => s.Id).ToListAsync()).ToHashSet()
            : (await _context.Students.Where(s => s.ClassId == classId).Select(s => s.Id).ToListAsync()).ToHashSet();

        var existingLinks = await _context.StudentSubjects.Where(ss => ss.SubjectId == subjectId).ToListAsync();
        _context.StudentSubjects.RemoveRange(
            existingLinks.Where(l => shownStudentIds.Contains(l.StudentId) && !selectedIds.Contains(l.StudentId)));

        var existingStudentIds = existingLinks.Select(l => l.StudentId).ToHashSet();
        foreach (var studentId in selectedIds.Where(sid => shownStudentIds.Contains(sid) && !existingStudentIds.Contains(sid)))
        {
            _context.StudentSubjects.Add(new Models.StudentSubject { SubjectId = subjectId, StudentId = studentId });
        }

        await _context.SaveChangesAsync();
        TempData["TeacherPortalSuccess"] = $"Students updated for {subject.Name}.";
        return RedirectToAction(nameof(MySubjects));
    }

    public async Task<IActionResult> Students(int? classId)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return View(new List<Student>());

        var classIds = await GetMyClassIdsAsync(teacher.Id);
        if (classId != null && !classIds.Contains(classId.Value))
        {
            classId = null;
        }

        if (classId == null && classIds.Count == 1)
        {
            classId = classIds[0];
        }

        var classes = await _context.Classes.Where(c => classIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, StudentCount = c.Students.Count(s => s.IsActive) })
            .ToListAsync();
        ViewBag.Classes = classes.Select(c => new ClassPickerItem { Id = c.Id, Name = c.Name, StudentCount = c.StudentCount }).ToList();
        ViewBag.SelectedClassId = classId;

        if (classId == null)
        {
            // Multiple grades assigned and none picked yet - show the grade picker only.
            return View(new List<Student>());
        }

        var students = await _context.Students
            .Include(s => s.Class)
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .ToListAsync();

        return View(students);
    }

    public async Task<IActionResult> CreateStudent(int? classId)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return Forbid();

        var classIds = await GetMyClassIdsAsync(teacher.Id);
        if (!classIds.Any())
        {
            TempData["TeacherPortalError"] = "You need a grade assigned to you before you can enroll students. Ask an administrator to assign one.";
            return RedirectToAction(nameof(Students));
        }

        // Preselect the grade the teacher was viewing, or their only grade.
        int? preselected = classId != null && classIds.Contains(classId.Value) ? classId
            : classIds.Count == 1 ? classIds[0] : null;

        await PopulateMyClassesAsync(teacher.Id, preselected);
        return View(new Student { ClassId = preselected });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStudent(Student student, IFormFile? photoFile)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return Forbid();

        var classIds = await GetMyClassIdsAsync(teacher.Id);
        if (student.ClassId == null || !classIds.Contains(student.ClassId.Value))
        {
            ModelState.AddModelError(nameof(Student.ClassId), "You can only enroll students into a grade assigned to you.");
        }

        student.AdmissionNumber = await GenerateAdmissionNumberAsync();
        ModelState.Remove(nameof(Student.AdmissionNumber));

        if (!ModelState.IsValid)
        {
            await PopulateMyClassesAsync(teacher.Id, student.ClassId);
            return View(student);
        }

        if (photoFile != null && photoFile.Length > 0)
        {
            student.PhotoPath = await SavePhotoAsync(photoFile);
        }

        _context.Add(student);
        await _context.SaveChangesAsync();
        TempData["TeacherPortalSuccess"] = $"{student.FullName} enrolled with admission number {student.AdmissionNumber}.";
        return RedirectToAction(nameof(Students), new { classId = student.ClassId });
    }

    public async Task<IActionResult> TransferStudent(int? id)
    {
        if (id == null) return NotFound();

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return Forbid();

        var classIds = await GetMyClassIdsAsync(teacher.Id);

        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.Id == id);
        if (student == null) return NotFound();
        if (student.ClassId == null || !classIds.Contains(student.ClassId.Value)) return Forbid();

        await PopulateMyClassesAsync(teacher.Id, student.ClassId);
        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferStudent(int id, int? newClassId)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return Forbid();

        var classIds = await GetMyClassIdsAsync(teacher.Id);

        var student = await _context.Students.FindAsync(id);
        if (student == null) return NotFound();
        if (student.ClassId == null || !classIds.Contains(student.ClassId.Value)) return Forbid();
        if (newClassId == null || !classIds.Contains(newClassId.Value))
        {
            TempData["TeacherPortalError"] = "You can only transfer students to a grade assigned to you.";
            return RedirectToAction(nameof(TransferStudent), new { id });
        }

        student.ClassId = newClassId;
        await _context.SaveChangesAsync();

        TempData["TeacherPortalSuccess"] = $"{student.FullName} has been transferred.";
        return RedirectToAction(nameof(Students));
    }

    private async Task PopulateMyClassesAsync(int teacherId, int? selectedId = null)
    {
        var classIds = await GetMyClassIdsAsync(teacherId);
        var classes = await _context.Classes.Where(c => classIds.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Classes = new SelectList(classes, "Id", "Name", selectedId);
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

public class ClassPickerItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StudentCount { get; set; }
}
