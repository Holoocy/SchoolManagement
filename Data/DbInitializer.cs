using Microsoft.EntityFrameworkCore;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();

        // Creates every table/column directly from the current EF model. This only
        // takes effect on a brand-new, empty database (a no-op if one already exists).
        await context.Database.EnsureCreatedAsync();

        // Seed a default school + its first Admin account, but only on a first-ever run.
        var defaultSchool = await context.Schools.FirstOrDefaultAsync(s => s.Slug == "heavens-gate-academy");
        if (defaultSchool == null)
        {
            defaultSchool = new School
            {
                Name = "Heavens Gate Academy",
                Slug = "heavens-gate-academy",
                IsActive = true
            };
            context.Schools.Add(defaultSchool);
            await context.SaveChangesAsync();
        }

        // Query filters are ignored here because no tenant is resolved at startup (no HTTP request yet).
        if (!context.Users.IgnoreQueryFilters().Any(u => u.Username == "admin" && u.SchoolId == defaultSchool.Id))
        {
            var adminUser = new User
            {
                SchoolId = defaultSchool.Id,
                Username = "admin",
                Email = "admin@schoolmanagement.com",
                FullName = "Administrator",
                PasswordHash = passwordService.HashPassword("admin123"),
                IsActive = true
            };

            context.Users.Add(adminUser);
            await context.SaveChangesAsync();

            var adminRole = context.Roles.FirstOrDefault(r => r.Name == "Admin");
            if (adminRole != null)
            {
                context.UserRoles.Add(new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id });
                await context.SaveChangesAsync();
            }
        }
    }
}
