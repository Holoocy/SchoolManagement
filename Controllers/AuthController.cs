using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Models;
using SchoolManagement.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace SchoolManagement.Controllers;

public class AuthController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly ITenantProvider _tenantProvider;

    public AuthController(ApplicationDbContext context, IPasswordService passwordService, ITenantProvider tenantProvider)
    {
        _context = context;
        _passwordService = passwordService;
        _tenantProvider = tenantProvider;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated ?? false)
            return RedirectToAction("Index", "Dashboard");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string portal)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(portal))
        {
            ModelState.AddModelError("", "Username, password and portal are required.");
            return View();
        }

        var user = _context.Users
            .FirstOrDefault(u => u.Username == username && u.IsActive);

        if (user == null || !_passwordService.VerifyPassword(password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Invalid username or password.");
            return View();
        }

        var userRoles = _context.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.Role.Name)
            .ToList();

        if (!userRoles.Contains(portal))
        {
            ModelState.AddModelError("", $"This account is not registered as a {portal}.");
            return View();
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("FullName", user.FullName),
            new Claim("SchoolId", _tenantProvider.SchoolId.ToString())
        };

        foreach (var role in userRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "CookieAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("CookieAuth", principal);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult Signup()
    {
        if (User.Identity?.IsAuthenticated ?? false)
            return RedirectToAction("Index", "Dashboard");
        return View(new SignupViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup(SignupViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.Role != "Teacher" && model.Role != "Student" && model.Role != "Parent")
        {
            ModelState.AddModelError("", "Please select a valid account type.");
            return View(model);
        }

        if (_context.Users.Any(u => u.Username == model.Username))
        {
            ModelState.AddModelError(nameof(model.Username), "That username is already taken.");
            return View(model);
        }

        if (_context.Users.Any(u => u.Email == model.Email))
        {
            ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
            return View(model);
        }

        Teacher? teacher = null;
        Student? student = null;

        if (model.Role == "Teacher")
        {
            teacher = _context.Teachers.FirstOrDefault(t => t.EmployeeId == model.LinkCode);
            if (teacher == null)
            {
                ModelState.AddModelError(nameof(model.LinkCode), "No teacher record found with that Employee ID.");
                return View(model);
            }
            if (teacher.UserId != null)
            {
                ModelState.AddModelError(nameof(model.LinkCode), "That teacher record already has an account linked to it.");
                return View(model);
            }
        }
        else if (model.Role == "Student")
        {
            student = _context.Students.FirstOrDefault(s => s.AdmissionNumber == model.LinkCode);
            if (student == null)
            {
                ModelState.AddModelError(nameof(model.LinkCode), "No student record found with that Admission Number.");
                return View(model);
            }
            if (student.UserId != null)
            {
                ModelState.AddModelError(nameof(model.LinkCode), "That student record already has an account linked to it.");
                return View(model);
            }
        }
        else
        {
            student = _context.Students.FirstOrDefault(s => s.AdmissionNumber == model.LinkCode);
            if (student == null)
            {
                ModelState.AddModelError(nameof(model.LinkCode), "No student record found with that Admission Number.");
                return View(model);
            }
        }

        var user = new User
        {
            Username = model.Username,
            Email = model.Email,
            FullName = model.FullName,
            PasswordHash = _passwordService.HashPassword(model.Password),
            IsActive = true
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var role = _context.Roles.FirstOrDefault(r => r.Name == model.Role);
        if (role != null)
        {
            _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        if (model.Role == "Teacher" && teacher != null)
        {
            teacher.UserId = user.Id;
        }
        else if (model.Role == "Student" && student != null)
        {
            student.UserId = user.Id;
        }
        else if (model.Role == "Parent" && student != null)
        {
            _context.ParentStudents.Add(new ParentStudent { ParentUserId = user.Id, StudentId = student.Id });
        }

        await _context.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("FullName", user.FullName),
            new Claim("SchoolId", _tenantProvider.SchoolId.ToString()),
            new Claim(ClaimTypes.Role, model.Role)
        };

        var identity = new ClaimsIdentity(claims, "CookieAuth");
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync("CookieAuth", principal);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _context.Users.FirstOrDefaultAsync(u =>
            u.Username == model.UsernameOrEmail || u.Email == model.UsernameOrEmail);

        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError("", "No active account found with that username or email.");
            return View(model);
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToHexString(tokenBytes);

        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await _context.SaveChangesAsync();

        var resetUrl = Url.Action("ResetPassword", "Auth", new { token }, Request.Scheme);
        ViewBag.ResetUrl = resetUrl;
        return View("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string token)
    {
        var resetToken = await _context.PasswordResetTokens.FirstOrDefaultAsync(t => t.Token == token);

        if (resetToken == null || resetToken.IsUsed || resetToken.ExpiresAt < DateTime.UtcNow)
        {
            return View("ResetPasswordInvalid");
        }

        return View(new ResetPasswordViewModel { Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var resetToken = await _context.PasswordResetTokens.FirstOrDefaultAsync(t => t.Token == model.Token);

        if (resetToken == null || resetToken.IsUsed || resetToken.ExpiresAt < DateTime.UtcNow)
        {
            return View("ResetPasswordInvalid");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _context.Users.FindAsync(resetToken.UserId);
        if (user == null)
        {
            return View("ResetPasswordInvalid");
        }

        user.PasswordHash = _passwordService.HashPassword(model.NewPassword);
        resetToken.IsUsed = true;
        await _context.SaveChangesAsync();

        return View("ResetPasswordSuccess");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("CookieAuth");
        return RedirectToAction("Login");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }
}
