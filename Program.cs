using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Data;
using SchoolManagement.Services;

// This app only ever deals in plain calendar dates/times for a single school (no
// cross-timezone requirements), but Npgsql's modern default strictly enforces an
// exact DateTime.Kind match per column type (Utc-only for "timestamptz", Unspecified-only
// for "timestamp"). SQL Server never checked Kind at all, so the codebase mixes
// DateTime.UtcNow, DateTime.Today and form-bound (Kind=Unspecified) values freely.
// This switch restores Npgsql's pre-6.0 lenient behavior so any Kind is accepted.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=SchoolManagementDb;Username=postgres;Password=postgres";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<ITenantProvider, TenantProvider>();

builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";

        // Every controller lives under a /{schoolSlug}/... prefix, so authentication
        // redirects must carry that prefix forward instead of using a fixed path.
        options.Events.OnRedirectToLogin = context =>
        {
            var slug = context.Request.RouteValues.TryGetValue("schoolSlug", out var s) ? s?.ToString() : null;
            var loginUrl = string.IsNullOrEmpty(slug) ? "/Auth/Login" : $"/{slug}/Auth/Login";
            context.Response.Redirect(loginUrl + context.Request.QueryString);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            var slug = context.Request.RouteValues.TryGetValue("schoolSlug", out var s) ? s?.ToString() : null;
            var url = string.IsNullOrEmpty(slug) ? "/Auth/AccessDenied" : $"/{slug}/Auth/AccessDenied";
            context.Response.Redirect(url);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IPasswordService, PasswordService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Dashboard/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Resolve the current school from the {schoolSlug} route segment before anything else runs.
app.Use(async (context, next) =>
{
    var slug = context.GetRouteValue("schoolSlug")?.ToString();
    if (!string.IsNullOrEmpty(slug))
    {
        var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
        var school = await db.Schools.FirstOrDefaultAsync(s => s.Slug == slug && s.IsActive);
        if (school == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("School not found.");
            return;
        }

        var tenantProvider = context.RequestServices.GetRequiredService<ITenantProvider>();
        tenantProvider.SetSchool(school.Id, school.Slug, school.Name, school.LogoPath, school.ThemeColor);
    }

    await next();
});

app.UseAuthentication();

// Guard against a signed-in user (from School A's cookie) browsing to School B's URL.
app.Use(async (context, next) =>
{
    var tenantProvider = context.RequestServices.GetRequiredService<ITenantProvider>();
    if (tenantProvider.IsResolved && context.User.Identity?.IsAuthenticated == true)
    {
        var claimSchoolId = context.User.FindFirst("SchoolId")?.Value;
        if (claimSchoolId != tenantProvider.SchoolId.ToString())
        {
            await context.SignOutAsync("CookieAuth");
            context.Response.Redirect($"/{tenantProvider.Slug}/Auth/Login");
            return;
        }
    }

    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "onboard",
    pattern: "Onboard/{action=Index}",
    defaults: new { controller = "Onboard" });

app.MapControllerRoute(
    name: "home",
    pattern: "",
    defaults: new { controller = "Home", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{schoolSlug}/{controller=Dashboard}/{action=Index}/{id?}");

// Initialize database
await DbInitializer.InitializeAsync(app);

app.Run();
