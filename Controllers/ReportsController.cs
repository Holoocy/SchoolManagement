using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin")]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ReportsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);

        var model = new ReportsViewModel
        {
            TotalStudents = await _context.Students.CountAsync(),
            TotalTeachers = await _context.Teachers.CountAsync(),
            TotalSubjects = await _context.Subjects.CountAsync(),
            TotalAssignments = await _context.Assignments.CountAsync(),
            TotalWeeklyChecklists = await _context.WeeklyChecklistItems.CountAsync()
        };

        model.AssignmentStatusBreakdown = await _context.Assignments
            .GroupBy(a => a.Status)
            .Select(g => new StatusBreakdown { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        model.WeeklyChecklistStatusBreakdown = await _context.WeeklyChecklistItems
            .GroupBy(w => w.Status)
            .Select(g => new StatusBreakdown { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var recentAttendance = await _context.AttendanceRecords
            .Where(a => a.AttendanceDate.Date >= sevenDaysAgo)
            .ToListAsync();

        model.AttendanceLastSevenDays = Enumerable.Range(0, 7)
            .Select(offset => sevenDaysAgo.AddDays(offset))
            .Select(date => new AttendanceDaySummary
            {
                Date = date,
                Total = recentAttendance.Count(a => a.AttendanceDate.Date == date),
                Present = recentAttendance.Count(a => a.AttendanceDate.Date == date && a.Status == "Present")
            })
            .ToList();

        var totalRecords = recentAttendance.Count;
        var totalPresent = recentAttendance.Count(a => a.Status == "Present");
        model.OverallAttendanceRate = totalRecords > 0
            ? Math.Round(totalPresent * 100m / totalRecords, 1)
            : 0m;

        return View(model);
    }
}
