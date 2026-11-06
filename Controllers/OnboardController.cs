using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Controllers;

public class OnboardController : Controller
{
    private static readonly string[] ReservedSlugs = { "onboard", "auth", "dashboard", "home" };

    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly IWebHostEnvironment _environment;

    public OnboardController(ApplicationDbContext context, IPasswordService passwordService, IWebHostEnvironment environment)
    {
        _context = context;
        _passwordService = passwordService;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new OnboardViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(OnboardViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var slug = await GenerateSlugAsync(model.SchoolName);

        var school = new School
        {
            Name = model.SchoolName.Trim(),
            Slug = slug,
            IsActive = true,
            ThemeColor = string.IsNullOrWhiteSpace(model.ThemeColor) ? "#2563eb" : model.ThemeColor
        };

        if (model.LogoFile != null && model.LogoFile.Length > 0)
        {
            school.LogoPath = await SaveLogoAsync(model.LogoFile);
        }

        _context.Schools.Add(school);
        await _context.SaveChangesAsync();

        var user = new User
        {
            SchoolId = school.Id,
            Username = model.Username,
            Email = model.Email,
            FullName = model.FullName,
            PasswordHash = _passwordService.HashPassword(model.Password),
            IsActive = true
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
        if (adminRole != null)
        {
            _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
            await _context.SaveChangesAsync();
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("FullName", user.FullName),
            new Claim("SchoolId", school.Id.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, "CookieAuth");
        await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(identity));

        return Redirect($"/{slug}/Dashboard");
    }

    private async Task<string> GenerateSlugAsync(string schoolName)
    {
        var baseSlug = Regex.Replace(schoolName.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(baseSlug))
        {
            baseSlug = "school";
        }

        var slug = baseSlug;
        var counter = 2;
        while (ReservedSlugs.Contains(slug) || await _context.Schools.AnyAsync(s => s.Slug == slug))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
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
}
