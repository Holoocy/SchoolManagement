using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Parent")]
public class ParentPortalController : Controller
{
    private readonly ApplicationDbContext _context;

    public ParentPortalController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var childIds = await _context.ParentStudents
            .Where(ps => ps.ParentUserId == CurrentUserId)
            .Select(ps => ps.StudentId)
            .Distinct()
            .ToListAsync();

        var children = await _context.Students
            .Include(s => s.Class)
            .Where(s => childIds.Contains(s.Id))
            .OrderBy(s => s.FirstName)
            .ToListAsync();

        return View(children);
    }

    public async Task<IActionResult> Child(int studentId)
    {
        var isLinked = await _context.ParentStudents
            .AnyAsync(ps => ps.ParentUserId == CurrentUserId && ps.StudentId == studentId);

        if (!isLinked)
        {
            return Forbid();
        }

        var student = await _context.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.Id == studentId);
        if (student == null) return NotFound();

        var classSubjectIds = student.ClassId == null
            ? new List<int>()
            : await _context.SubjectClasses.Where(sc => sc.ClassId == student.ClassId).Select(sc => sc.SubjectId).ToListAsync();
        var individualSubjectIds = await _context.StudentSubjects
            .Where(ss => ss.StudentId == student.Id)
            .Select(ss => ss.SubjectId)
            .ToListAsync();
        var subjectIds = classSubjectIds.Union(individualSubjectIds).ToList();

        ViewBag.Subjects = await _context.Subjects.Where(s => subjectIds.Contains(s.Id)).Select(s => s.Name).ToListAsync();

        var submissions = await _context.AssignmentSubmissions
            .Include(s => s.Assignment).ThenInclude(a => a!.Subject)
            .Where(s => s.StudentId == studentId)
            .OrderByDescending(s => s.Assignment!.DueDate)
            .Take(10)
            .ToListAsync();
        ViewBag.Submissions = submissions;

        var gradedSubmissions = await _context.AssignmentSubmissions
            .Where(s => s.StudentId == studentId && s.Grade != null)
            .Select(s => s.Grade!.Value)
            .ToListAsync();
        ViewBag.AverageGrade = gradedSubmissions.Any() ? Math.Round(gradedSubmissions.Average(), 1) : (decimal?)null;

        var attendance = await _context.AttendanceRecords
            .Where(a => a.StudentId == studentId)
            .OrderByDescending(a => a.AttendanceDate)
            .ToListAsync();

        ViewBag.RecentAttendance = attendance.Take(10).ToList();
        ViewBag.PresentCount = attendance.Count(a => a.Status == "Present");
        ViewBag.TotalAttendance = attendance.Count;
        ViewBag.AttendanceRate = attendance.Count > 0
            ? Math.Round(attendance.Count(a => a.Status == "Present") * 100m / attendance.Count, 1)
            : 0m;

        return View(student);
    }
}
