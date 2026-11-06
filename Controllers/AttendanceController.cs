using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = "Admin,Teacher")]
public class AttendanceController : Controller
{
    private readonly ApplicationDbContext _context;

    public AttendanceController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<List<int>?> GetMyClassIdsIfTeacherAsync()
    {
        if (User.IsInRole("Admin")) return null;

        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == CurrentUserId);
        if (teacher == null) return new List<int>();

        return await _context.TeacherClasses.Where(tc => tc.TeacherId == teacher.Id)
            .Select(tc => tc.ClassId).ToListAsync();
    }

    public async Task<IActionResult> Index(DateTime? searchDate)
    {
        if (searchDate != null)
        {
            return RedirectToAction(nameof(Day), new { date = searchDate.Value.ToString("yyyy-MM-dd") });
        }

        var days = await _context.AttendanceRecords
            .GroupBy(a => a.AttendanceDate)
            .Select(g => new AttendanceDaySummaryRow
            {
                Date = g.Key,
                TotalMarked = g.Count(),
                PresentCount = g.Count(a => a.Status == "Present")
            })
            .OrderByDescending(d => d.Date)
            .ToListAsync();

        return View(days);
    }

    public async Task<IActionResult> Day(DateTime date)
    {
        var day = date.Date;

        var records = await _context.AttendanceRecords
            .Include(a => a.Student).ThenInclude(s => s!.Class)
            .Where(a => a.AttendanceDate == day)
            .OrderBy(a => a.Student!.LastName).ThenBy(a => a.Student!.FirstName)
            .ToListAsync();

        ViewBag.Date = day;
        return View(records);
    }

    public async Task<IActionResult> Create(DateTime? date, int? classId)
    {
        var attendanceDate = (date ?? DateTime.UtcNow.Date).Date;
        var myClassIds = await GetMyClassIdsIfTeacherAsync();

        if (myClassIds != null)
        {
            // Teacher: restrict to their own grade(s). With more than one grade assigned,
            // require an explicit pick instead of mixing every grade's roster together.
            if (classId != null && !myClassIds.Contains(classId.Value))
            {
                classId = null;
            }

            if (classId == null && myClassIds.Count == 1)
            {
                classId = myClassIds[0];
            }
        }

        ViewBag.Date = attendanceDate;
        ViewBag.SelectedClassId = classId;
        var classesQuery = _context.Classes.AsQueryable();
        if (myClassIds != null)
        {
            classesQuery = classesQuery.Where(c => myClassIds.Contains(c.Id));
        }
        ViewBag.Classes = new SelectList(await classesQuery.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", classId);

        if (myClassIds != null && classId == null)
        {
            // Teacher with multiple grades and none picked yet - show the grade picker only.
            return View(new List<TakeAttendanceRow>());
        }

        var studentsQuery = _context.Students.Include(s => s.Class).Where(s => s.IsActive).AsQueryable();
        if (classId != null)
        {
            studentsQuery = studentsQuery.Where(s => s.ClassId == classId);
        }

        var students = await studentsQuery
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .ToListAsync();

        var existingRecords = await _context.AttendanceRecords
            .Where(a => a.AttendanceDate == attendanceDate && students.Select(s => s.Id).Contains(a.StudentId))
            .ToListAsync();

        var rows = students.Select(s => new TakeAttendanceRow
        {
            StudentId = s.Id,
            StudentName = s.FullName,
            ClassName = s.Class?.Name,
            Status = existingRecords.FirstOrDefault(r => r.StudentId == s.Id)?.Status
        }).ToList();

        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DateTime date, int? classId, List<TakeAttendanceRow> rows)
    {
        var attendanceDate = date.Date;
        rows ??= new List<TakeAttendanceRow>();

        var studentIds = rows.Select(r => r.StudentId).ToList();
        var existingRecords = await _context.AttendanceRecords
            .Where(a => a.AttendanceDate == attendanceDate && studentIds.Contains(a.StudentId))
            .ToListAsync();

        var marked = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.Status)) continue;

            var existing = existingRecords.FirstOrDefault(r => r.StudentId == row.StudentId);
            if (existing != null)
            {
                existing.Status = row.Status;
            }
            else
            {
                _context.AttendanceRecords.Add(new AttendanceRecord
                {
                    StudentId = row.StudentId,
                    AttendanceDate = attendanceDate,
                    Status = row.Status
                });
            }
            marked++;
        }

        await _context.SaveChangesAsync();
        TempData["AttendanceSuccess"] = $"Attendance recorded for {marked} student(s) on {attendanceDate.ToShortDateString()}.";
        return RedirectToAction(nameof(Create), new { date = attendanceDate.ToString("yyyy-MM-dd"), classId });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var record = await _context.AttendanceRecords.FindAsync(id);
        if (record == null) return NotFound();

        ViewBag.Students = new SelectList(await _context.Students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync(), "Id", "FullName", record.StudentId);
        return View(record);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AttendanceRecord record)
    {
        if (id != record.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Students = new SelectList(await _context.Students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync(), "Id", "FullName", record.StudentId);
            return View(record);
        }

        _context.Update(record);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var record = await _context.AttendanceRecords
            .Include(a => a.Student)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (record == null) return NotFound();

        return View(record);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var record = await _context.AttendanceRecords.FindAsync(id);
        if (record != null)
        {
            _context.AttendanceRecords.Remove(record);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }
}

public class TakeAttendanceRow
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public string? Status { get; set; }
}

public class AttendanceDaySummaryRow
{
    public DateTime Date { get; set; }
    public int TotalMarked { get; set; }
    public int PresentCount { get; set; }
}
