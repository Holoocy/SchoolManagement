using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly ITenantProvider _tenantProvider;
    private readonly IWebHostEnvironment _environment;

    public SettingsController(ApplicationDbContext context, IPasswordService passwordService, ITenantProvider tenantProvider, IWebHostEnvironment environment)
    {
        _context = context;
        _passwordService = passwordService;
        _tenantProvider = tenantProvider;
        _environment = environment;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstAsync(u => u.Id == CurrentUserId);

        var model = new SettingsViewModel
        {
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            CreatedAt = user.CreatedAt,
            Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList()
        };

        model.Branding = await BuildBrandingAsync();

        return View(model);
    }

    private async Task<SchoolBrandingViewModel> BuildBrandingAsync()
    {
        if (!User.IsInRole("Admin"))
        {
            return new SchoolBrandingViewModel();
        }

        var school = await _context.Schools.FirstAsync(s => s.Id == _tenantProvider.SchoolId);
        return new SchoolBrandingViewModel
        {
            CurrentLogoPath = school.LogoPath,
            ThemeColor = school.ThemeColor
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateBranding(SchoolBrandingViewModel branding)
    {
        var school = await _context.Schools.FirstAsync(s => s.Id == _tenantProvider.SchoolId);

        school.ThemeColor = string.IsNullOrWhiteSpace(branding.ThemeColor) ? school.ThemeColor : branding.ThemeColor;

        if (branding.LogoFile != null && branding.LogoFile.Length > 0)
        {
            school.LogoPath = await SaveLogoAsync(branding.LogoFile);
        }

        await _context.SaveChangesAsync();

        TempData["SettingsSuccess"] = "School branding updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string> SaveLogoAsync(IFormFile logoFile)
    {
        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "logos");
        Directory.CreateDirectory(uploadsFolder);

        var extension = Path.GetExtension(logoFile.FileName);
        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await logoFile.CopyToAsync(stream);
        }

        return $"/uploads/logos/{fileName}";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel changePassword)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstAsync(u => u.Id == CurrentUserId);

        if (!ModelState.IsValid)
        {
            var model = new SettingsViewModel
            {
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAt = user.CreatedAt,
                Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList(),
                ChangePassword = changePassword,
                Branding = await BuildBrandingAsync()
            };
            return View("Index", model);
        }

        if (!_passwordService.VerifyPassword(changePassword.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(ChangePasswordViewModel.CurrentPassword), "Current password is incorrect.");
            var model = new SettingsViewModel
            {
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAt = user.CreatedAt,
                Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList(),
                ChangePassword = changePassword,
                Branding = await BuildBrandingAsync()
            };
            return View("Index", model);
        }

        user.PasswordHash = _passwordService.HashPassword(changePassword.NewPassword);
        await _context.SaveChangesAsync();

        TempData["SettingsSuccess"] = "Your password has been updated.";
        return RedirectToAction("Index");
    }
}
