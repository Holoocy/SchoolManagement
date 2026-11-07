using Microsoft.EntityFrameworkCore;
using SchoolManagement.Models;
using SchoolManagement.Services;

namespace SchoolManagement.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<School> Schools { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<Student> Students { get; set; }
    public DbSet<Teacher> Teachers { get; set; }
    public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
    public DbSet<Assignment> Assignments { get; set; }
    public DbSet<Subject> Subjects { get; set; }
    public DbSet<WeeklyChecklistItem> WeeklyChecklistItems { get; set; }
    public DbSet<Class> Classes { get; set; }
    public DbSet<TeacherClass> TeacherClasses { get; set; }
    public DbSet<SubjectTeacher> SubjectTeachers { get; set; }
    public DbSet<SubjectClass> SubjectClasses { get; set; }
    public DbSet<ParentStudent> ParentStudents { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
    public DbSet<StudentSubject> StudentSubjects { get; set; }
    public DbSet<AssignmentSubmission> AssignmentSubmissions { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<MessageRecipient> MessageRecipients { get; set; }
    public DbSet<Exam> Exams { get; set; }
    public DbSet<ExamQuestion> ExamQuestions { get; set; }
    public DbSet<ExamSubmission> ExamSubmissions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User-Role many-to-many relationship
        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed initial roles
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "Admin", Description = "Administrator" },
            new Role { Id = 2, Name = "Teacher", Description = "Teacher" },
            new Role { Id = 3, Name = "Student", Description = "Student" },
            new Role { Id = 4, Name = "Parent", Description = "Parent" }
        );

        modelBuilder.Entity<Student>()
            .HasOne(s => s.Class)
            .WithMany(c => c.Students)
            .HasForeignKey(s => s.ClassId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Student>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Teacher>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Assignment>()
            .HasOne(a => a.Subject)
            .WithMany()
            .HasForeignKey(a => a.SubjectId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TeacherClass>()
            .HasOne(tc => tc.Teacher)
            .WithMany(t => t.TeacherClasses)
            .HasForeignKey(tc => tc.TeacherId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TeacherClass>()
            .HasOne(tc => tc.Class)
            .WithMany(c => c.TeacherClasses)
            .HasForeignKey(tc => tc.ClassId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SubjectTeacher>()
            .HasOne(st => st.Subject)
            .WithMany(s => s.SubjectTeachers)
            .HasForeignKey(st => st.SubjectId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SubjectTeacher>()
            .HasOne(st => st.Teacher)
            .WithMany(t => t.SubjectTeachers)
            .HasForeignKey(st => st.TeacherId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SubjectClass>()
            .HasOne(sc => sc.Subject)
            .WithMany(s => s.SubjectClasses)
            .HasForeignKey(sc => sc.SubjectId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<SubjectClass>()
            .HasOne(sc => sc.Class)
            .WithMany(c => c.SubjectClasses)
            .HasForeignKey(sc => sc.ClassId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ParentStudent>()
            .HasOne(ps => ps.ParentUser)
            .WithMany()
            .HasForeignKey(ps => ps.ParentUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ParentStudent>()
            .HasOne(ps => ps.Student)
            .WithMany()
            .HasForeignKey(ps => ps.StudentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StudentSubject>()
            .HasOne(ss => ss.Student)
            .WithMany(s => s.StudentSubjects)
            .HasForeignKey(ss => ss.StudentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StudentSubject>()
            .HasOne(ss => ss.Subject)
            .WithMany(s => s.StudentSubjects)
            .HasForeignKey(ss => ss.SubjectId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssignmentSubmission>()
            .HasOne(a => a.Assignment)
            .WithMany(a => a.Submissions)
            .HasForeignKey(a => a.AssignmentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<AssignmentSubmission>()
            .HasOne(a => a.Student)
            .WithMany(s => s.AssignmentSubmissions)
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.SenderUser)
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Class)
            .WithMany()
            .HasForeignKey(m => m.ClassId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<MessageRecipient>()
            .HasOne(r => r.Message)
            .WithMany(m => m.Recipients)
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MessageRecipient>()
            .HasOne(r => r.RecipientUser)
            .WithMany()
            .HasForeignKey(r => r.RecipientUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Exam>()
            .HasOne(e => e.Teacher)
            .WithMany()
            .HasForeignKey(e => e.TeacherId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Exam>()
            .HasOne(e => e.Subject)
            .WithMany()
            .HasForeignKey(e => e.SubjectId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Exam>()
            .HasOne(e => e.Class)
            .WithMany()
            .HasForeignKey(e => e.ClassId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExamQuestion>()
            .HasOne(q => q.Exam)
            .WithMany(e => e.Questions)
            .HasForeignKey(q => q.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamSubmission>()
            .HasOne(s => s.Exam)
            .WithMany(e => e.Submissions)
            .HasForeignKey(s => s.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamSubmission>()
            .HasOne(s => s.Student)
            .WithMany()
            .HasForeignKey(s => s.StudentId)
            .OnDelete(DeleteBehavior.NoAction);

        // Multi-tenancy: every tenant-scoped entity is transparently filtered to the
        // current request's school. Reads can never cross a tenant boundary by accident.
        modelBuilder.Entity<User>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<Student>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<Teacher>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<Class>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<Subject>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<Assignment>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<AttendanceRecord>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<WeeklyChecklistItem>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<TeacherClass>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<SubjectTeacher>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<SubjectClass>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<StudentSubject>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<AssignmentSubmission>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<ParentStudent>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<Message>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<MessageRecipient>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<Exam>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<ExamQuestion>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);
        modelBuilder.Entity<ExamSubmission>().HasQueryFilter(e => e.SchoolId == _tenantProvider.SchoolId);

        // The portal stores calendar dates and local date-times rather than timezone-aware
        // instants. PostgreSQL maps these consistently as timestamp without time zone.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetColumnType("timestamp without time zone");
                }
            }
        }

    }

    public override int SaveChanges()
    {
        StampTenantId();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantId();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void StampTenantId()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.SchoolId == 0)
            {
                entry.Entity.SchoolId = _tenantProvider.SchoolId;
            }
            else if (entry.State == EntityState.Modified)
            {
                // Edit forms bind a fresh entity straight from posted fields and never
                // include SchoolId (it's an internal field, not user-editable), so it
                // arrives as 0. _context.Update() marks every property as changed, which
                // would otherwise overwrite the real SchoolId in the database with that 0,
                // silently orphaning the record from every tenant's query filter. Excluding
                // it from the UPDATE statement keeps the original value untouched.
                entry.Property(nameof(ITenantEntity.SchoolId)).IsModified = false;
            }
        }
    }
}
