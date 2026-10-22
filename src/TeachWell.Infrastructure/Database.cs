using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TeachWell.Domain;
namespace TeachWell.Infrastructure;

public class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseElement> Elements => Set<CourseElement>();
    public DbSet<CourseAccess> CourseAccess => Set<CourseAccess>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CanvasConnection> CanvasConnections => Set<CanvasConnection>();
    public DbSet<ElementSnapshot> ElementSnapshots => Set<ElementSnapshot>();
    public DbSet<RubricProfile> RubricProfiles => Set<RubricProfile>();
    public DbSet<RubricVersion> RubricVersions => Set<RubricVersion>();
    public DbSet<RubricCriterion> RubricCriteria => Set<RubricCriterion>();
    public DbSet<ElementReview> Reviews => Set<ElementReview>();
    public DbSet<CriterionEvaluation> CriterionEvaluations => Set<CriterionEvaluation>();
    public DbSet<ReviewFinding> ReviewFindings => Set<ReviewFinding>();
    public DbSet<RevisionTask> RevisionTasks => Set<RevisionTask>();
    public DbSet<RevisionTaskFinding> RevisionTaskFindings => Set<RevisionTaskFinding>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportComment> SupportComments => Set<SupportComment>();
    public DbSet<SupportEvent> SupportEvents => Set<SupportEvent>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<GlossaryEntry> GlossaryEntries => Set<GlossaryEntry>();
    public DbSet<Workshop> Workshops => Set<Workshop>();
    public DbSet<WorkshopRegistration> WorkshopRegistrations => Set<WorkshopRegistration>();
    public DbSet<CourseCompetency> CourseCompetencies => Set<CourseCompetency>();
    public DbSet<CompetencyMapping> CompetencyMappings => Set<CompetencyMapping>();
    public DbSet<StandardSet> StandardSets => Set<StandardSet>();
    public DbSet<StandardSetVersion> StandardSetVersions => Set<StandardSetVersion>();
    public DbSet<GuideProgress> GuideProgress => Set<GuideProgress>();
    public DbSet<CheckResourceLink> CheckResourceLinks => Set<CheckResourceLink>();
    public DbSet<ReadinessRequest> ReadinessRequests => Set<ReadinessRequest>();
    public DbSet<Endorsement> Endorsements => Set<Endorsement>();
    public DbSet<CourseGuideRecord> CourseGuideRecords => Set<CourseGuideRecord>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardImmutableRecords();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardImmutableRecords();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    // Application-level guard; PostgreSQL triggers enforce the same rules for direct SQL.
    private void GuardImmutableRecords()
    {
        if (ChangeTracker.Entries<ElementSnapshot>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Content snapshots are append-only.");
        if (ChangeTracker.Entries<TaskEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Task history events are append-only.");
        if (ChangeTracker.Entries<SupportEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Support ticket history events are append-only.");
        static bool WasPublished(EntityEntry e) =>
            e.State is EntityState.Modified or EntityState.Deleted && (string?)e.OriginalValues["Status"] == ReviewVocabulary.Published;
        if (ChangeTracker.Entries<RubricVersion>().Any(WasPublished)) throw new InvalidOperationException("Published rubric versions are immutable.");
        if (ChangeTracker.Entries<StandardSetVersion>().Any(WasPublished)) throw new InvalidOperationException("Published standard-set versions are immutable.");
        if (ChangeTracker.Entries<ElementReview>().Any(WasPublished)) throw new InvalidOperationException("Published reviews are immutable.");
        if (ChangeTracker.Entries<Endorsement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Endorsements are immutable.");
        if (ChangeTracker.Entries<ReadinessRequest>().Any(e => e.State is EntityState.Modified or EntityState.Deleted
            && ReadinessVocabulary.Decided.Contains((string)e.OriginalValues[nameof(ReadinessRequest.Status)]!)))
            throw new InvalidOperationException("Decided readiness requests are immutable.");
        static bool Changed(EntityEntry e) => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;
        var versionIds = ChangeTracker.Entries<RubricCriterion>().Where(Changed).Select(e => (Guid)e.OriginalValues[nameof(RubricCriterion.VersionId)]!).Distinct().ToList();
        if (versionIds.Count > 0 && RubricVersions.AsNoTracking().Any(v => versionIds.Contains(v.Id) && v.Status == ReviewVocabulary.Published))
            throw new InvalidOperationException("Published rubric criteria are immutable.");
        var reviewIds = ChangeTracker.Entries<CriterionEvaluation>().Where(Changed).Select(e => (Guid)e.OriginalValues[nameof(CriterionEvaluation.ReviewId)]!)
            .Concat(ChangeTracker.Entries<ReviewFinding>().Where(Changed).Select(e => (Guid)e.OriginalValues[nameof(ReviewFinding.ReviewId)]!)).Distinct().ToList();
        if (reviewIds.Count > 0 && Reviews.AsNoTracking().Any(r => reviewIds.Contains(r.Id) && r.Status == ReviewVocabulary.Published))
            throw new InvalidOperationException("Published review results are immutable.");
    }
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Course>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<CourseElement>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ElementSnapshot>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Course>().HasMany(x => x.Elements).WithOne().HasForeignKey(x => x.CourseId);
        b.Entity<Course>().HasMany(x => x.Access).WithOne().HasForeignKey(x => x.CourseId);
        b.Entity<Course>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Course>().HasIndex(x => new { x.TenantId, x.OwnerId });
        b.Entity<CourseAccess>().HasKey(x => new { x.CourseId, x.UserId });
        b.Entity<CourseAccess>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourseAccess>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourseElement>().HasOne<CourseElement>().WithMany().HasForeignKey(x => x.ParentElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourseElement>().HasIndex(x => new { x.CourseId, x.Type, x.SourceElementId }).IsUnique();
        b.Entity<CourseElement>().HasOne<CourseElement>().WithMany().HasForeignKey(x => x.TargetElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourseElement>().HasMany(x => x.Snapshots).WithOne().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementSnapshot>().HasIndex(x => new { x.ElementId, x.FetchedAt });
        b.Entity<ElementSnapshot>().Property(x => x.ContentHash).HasMaxLength(64);
        b.Entity<Course>().HasOne<CanvasConnection>().WithMany().HasForeignKey(x => x.CanvasConnectionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Course>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Course>().HasIndex(x => new { x.TenantId, x.OwnerId, x.SourceOrigin, x.SourceCourseId }).IsUnique().HasFilter("\"SourceOrigin\" IS NOT NULL");
        b.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(120);
        b.Entity<Course>().Property(x => x.Title).HasMaxLength(240);
        b.Entity<CanvasConnection>().HasIndex(x => new { x.TenantId, x.OwnerId, x.BaseUrl }).IsUnique();
        b.Entity<CanvasConnection>().Property(x => x.BaseUrl).HasMaxLength(300);
        b.Entity<CanvasConnection>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<CanvasConnection>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<RubricProfile>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<RubricProfile>().Property(x => x.Name).HasMaxLength(160);
        b.Entity<RubricProfile>().Property(x => x.DeliveryMethod).HasMaxLength(60);
        b.Entity<RubricProfile>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.Entity<RubricProfile>().HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RubricProfile>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RubricVersion>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<RubricVersion>().HasIndex(x => new { x.ProfileId, x.VersionNumber }).IsUnique();
        b.Entity<RubricVersion>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<RubricVersion>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<RubricVersion>().HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RubricCriterion>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<RubricCriterion>().HasIndex(x => new { x.VersionId, x.Code }).IsUnique();
        b.Entity<RubricCriterion>().Property(x => x.Code).HasMaxLength(20);
        b.Entity<RubricCriterion>().Property(x => x.Title).HasMaxLength(160);
        b.Entity<RubricCriterion>().Property(x => x.Category).HasMaxLength(60);

        b.Entity<ElementReview>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ElementReview>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<ElementReview>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<ElementReview>().HasIndex(x => new { x.TenantId, x.CourseId, x.ElementId });
        b.Entity<ElementReview>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementReview>().HasOne<CourseElement>().WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementReview>().HasOne<ElementSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementReview>().HasOne<RubricVersion>().WithMany().HasForeignKey(x => x.RubricVersionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementReview>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementReview>().HasMany(x => x.Evaluations).WithOne().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementReview>().HasMany(x => x.Findings).WithOne().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CriterionEvaluation>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<CriterionEvaluation>().HasIndex(x => new { x.ReviewId, x.CriterionId }).IsUnique();
        b.Entity<CriterionEvaluation>().HasOne<RubricCriterion>().WithMany().HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CriterionEvaluation>().Property(x => x.Outcome).HasMaxLength(20);
        b.Entity<ReviewFinding>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ReviewFinding>().HasOne<RubricCriterion>().WithMany().HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReviewFinding>().HasIndex(x => new { x.ReviewId, x.Fingerprint }).IsUnique().HasFilter("\"Fingerprint\" IS NOT NULL");
        b.Entity<ReviewFinding>().HasOne<CourseElement>().WithMany().HasForeignKey(x => x.SubjectElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReviewFinding>().Property(x => x.Title).HasMaxLength(200);
        b.Entity<ReviewFinding>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<ReviewFinding>().Property(x => x.Severity).HasMaxLength(20);
        b.Entity<ReviewFinding>().Property(x => x.Fingerprint).HasMaxLength(80);

        b.Entity<RevisionTask>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<RevisionTask>().Property(x => x.Title).HasMaxLength(200);
        b.Entity<RevisionTask>().Property(x => x.Priority).HasMaxLength(20);
        b.Entity<RevisionTask>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<RevisionTask>().Property(x => x.BlockedFromStatus).HasMaxLength(20);
        b.Entity<RevisionTask>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<RevisionTask>().HasIndex(x => new { x.TenantId, x.CourseId });
        b.Entity<RevisionTask>().HasIndex(x => x.AssigneeId);
        b.Entity<RevisionTask>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTask>().HasOne<CourseElement>().WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTask>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTask>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTask>().HasOne<ElementSnapshot>().WithMany().HasForeignKey(x => x.SourceSnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTask>().HasOne<ElementSnapshot>().WithMany().HasForeignKey(x => x.ReadyForReviewSnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTask>().HasMany(x => x.TaskFindings).WithOne().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RevisionTask>().HasMany(x => x.Comments).WithOne().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTask>().HasMany(x => x.Events).WithOne().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RevisionTaskFinding>().HasKey(x => new { x.TaskId, x.FindingId });
        b.Entity<RevisionTaskFinding>().HasOne<ReviewFinding>().WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TaskComment>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<TaskComment>().Property(x => x.Body).HasMaxLength(4000);
        b.Entity<TaskComment>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TaskEvent>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<TaskEvent>().Property(x => x.FromStatus).HasMaxLength(20);
        b.Entity<TaskEvent>().Property(x => x.ToStatus).HasMaxLength(20);
        b.Entity<TaskEvent>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<TaskEvent>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TaskEvent>().HasIndex(x => new { x.TaskId, x.CreatedAt });

        b.Entity<SupportTicket>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<SupportTicket>().Property(x => x.Title).HasMaxLength(200);
        b.Entity<SupportTicket>().Property(x => x.Category).HasMaxLength(20);
        b.Entity<SupportTicket>().Property(x => x.Priority).HasMaxLength(20);
        b.Entity<SupportTicket>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<SupportTicket>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<SupportTicket>().HasIndex(x => x.TenantId);
        b.Entity<SupportTicket>().HasIndex(x => x.RequesterId);
        b.Entity<SupportTicket>().HasIndex(x => x.AssigneeId);
        b.Entity<SupportTicket>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<CourseElement>().WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<RevisionTask>().WithMany().HasForeignKey(x => x.RevisionTaskId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<KnowledgeArticle>().WithMany().HasForeignKey(x => x.RecommendedArticleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<Workshop>().WithMany().HasForeignKey(x => x.RecommendedWorkshopId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasMany(x => x.Comments).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasMany(x => x.Events).WithOne().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportComment>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<SupportComment>().Property(x => x.Body).HasMaxLength(4000);
        b.Entity<SupportComment>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportEvent>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<SupportEvent>().Property(x => x.FromStatus).HasMaxLength(20);
        b.Entity<SupportEvent>().Property(x => x.ToStatus).HasMaxLength(20);
        b.Entity<SupportEvent>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<SupportEvent>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportEvent>().HasIndex(x => new { x.TicketId, x.CreatedAt });

        b.Entity<KnowledgeArticle>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<KnowledgeArticle>().Property(x => x.Title).HasMaxLength(160);
        b.Entity<KnowledgeArticle>().Property(x => x.Slug).HasMaxLength(180);
        b.Entity<KnowledgeArticle>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<KnowledgeArticle>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<KnowledgeArticle>().HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        b.Entity<KnowledgeArticle>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<GlossaryEntry>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<GlossaryEntry>().Property(x => x.TermId).HasMaxLength(60);
        b.Entity<GlossaryEntry>().Property(x => x.Term).HasMaxLength(80);
        b.Entity<GlossaryEntry>().Property(x => x.Definition).HasMaxLength(1200);
        b.Entity<GlossaryEntry>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<GlossaryEntry>().HasIndex(x => new { x.TenantId, x.TermId }).IsUnique();
        b.Entity<GlossaryEntry>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Workshop>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Workshop>().Property(x => x.Title).HasMaxLength(200);
        b.Entity<Workshop>().Property(x => x.Modality).HasMaxLength(20);
        b.Entity<Workshop>().HasIndex(x => new { x.TenantId, x.StartsAt });
        b.Entity<Workshop>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Workshop>().HasMany(x => x.Registrations).WithOne().HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkshopRegistration>().HasKey(x => new { x.WorkshopId, x.UserId });
        b.Entity<WorkshopRegistration>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<WorkshopRegistration>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<CourseCompetency>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<CourseCompetency>().Property(x => x.Code).HasMaxLength(20);
        b.Entity<CourseCompetency>().Property(x => x.Title).HasMaxLength(200);
        b.Entity<CourseCompetency>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<CourseCompetency>().HasIndex(x => new { x.CourseId, x.Code }).IsUnique();
        b.Entity<CourseCompetency>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CourseCompetency>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourseCompetency>().HasMany(x => x.Mappings).WithOne().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CompetencyMapping>().HasKey(x => new { x.CompetencyId, x.ElementId });
        b.Entity<CompetencyMapping>().HasOne<CourseElement>().WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Course>().Property(x => x.DeliveryMode).HasMaxLength(20);
        b.Entity<Course>().Property(x => x.Department).HasMaxLength(StandardSetVocabulary.MaxDepartmentLength);
        b.Entity<Course>().Property(x => x.Program).HasMaxLength(StandardSetVocabulary.MaxDepartmentLength);
        // The administrator's audit viewer reads newest first, optionally by person.
        b.Entity<AuditLog>().HasIndex(x => x.Timestamp);
        b.Entity<AuditLog>().HasIndex(x => new { x.ActorId, x.Timestamp });
        b.Entity<StandardSet>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<StandardSet>().Property(x => x.Name).HasMaxLength(StandardSetVocabulary.MaxNameLength);
        b.Entity<StandardSet>().Property(x => x.DeliveryMode).HasMaxLength(20);
        b.Entity<StandardSet>().Property(x => x.Department).HasMaxLength(StandardSetVocabulary.MaxDepartmentLength);
        b.Entity<StandardSet>().Property(x => x.ScopeKey).HasMaxLength(StandardSetVocabulary.MaxDepartmentLength + 30);
        b.Entity<StandardSet>().HasIndex(x => new { x.TenantId, x.ScopeKey }).IsUnique();
        b.Entity<StandardSet>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StandardSet>().HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.SetId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StandardSetVersion>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<StandardSetVersion>().HasIndex(x => new { x.SetId, x.VersionNumber }).IsUnique();
        b.Entity<StandardSetVersion>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<StandardSetVersion>().Property(x => x.Notes).HasMaxLength(StandardSetVocabulary.MaxNotesLength);
        b.Entity<StandardSetVersion>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<StandardSetVersion>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StandardSetVersion>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementReview>().HasOne<StandardSetVersion>().WithMany().HasForeignKey(x => x.StandardSetVersionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<GuideProgress>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<GuideProgress>().Property(x => x.Key).HasMaxLength(GuideVocabulary.MaxKeyLength);
        b.Entity<GuideProgress>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<GuideProgress>().Property(x => x.Reason).HasMaxLength(GuideVocabulary.MaxReasonLength);
        b.Entity<GuideProgress>().HasIndex(x => new { x.CourseId, x.Key }).IsUnique();
        b.Entity<GuideProgress>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GuideProgress>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.SetBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CheckResourceLink>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<CheckResourceLink>().Property(x => x.CheckCode).HasMaxLength(60);
        b.Entity<CheckResourceLink>().HasIndex(x => new { x.TenantId, x.CheckCode });
        b.Entity<CheckResourceLink>().HasOne<KnowledgeArticle>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CheckResourceLink>().HasOne<Workshop>().WithMany().HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CheckResourceLink>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReadinessRequest>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ReadinessRequest>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<ReadinessRequest>().Property(x => x.Note).HasMaxLength(ReadinessVocabulary.MaxNoteLength);
        b.Entity<ReadinessRequest>().Property(x => x.DecisionSummary).HasMaxLength(ReadinessVocabulary.MaxSummaryLength);
        b.Entity<ReadinessRequest>().Property(x => x.StandardsName).HasMaxLength(StandardSetVocabulary.MaxNameLength);
        b.Entity<ReadinessRequest>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<ReadinessRequest>().HasIndex(x => new { x.TenantId, x.Status });
        b.Entity<ReadinessRequest>().HasIndex(x => x.CourseId);
        b.Entity<ReadinessRequest>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReadinessRequest>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReadinessRequest>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReadinessRequest>().HasOne<StandardSetVersion>().WithMany().HasForeignKey(x => x.StandardSetVersionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Notification>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Notification>().Property(x => x.Kind).HasMaxLength(40);
        b.Entity<Notification>().Property(x => x.Title).HasMaxLength(NotificationVocabulary.MaxTitleLength);
        b.Entity<Notification>().Property(x => x.Body).HasMaxLength(NotificationVocabulary.MaxBodyLength);
        b.Entity<Notification>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<Notification>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CourseGuideRecord>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<CourseGuideRecord>().Property(x => x.StandardsName).HasMaxLength(StandardSetVocabulary.MaxNameLength);
        b.Entity<CourseGuideRecord>().HasIndex(x => new { x.CourseId, x.Day }).IsUnique();
        b.Entity<CourseGuideRecord>().HasIndex(x => new { x.TenantId, x.Day });
        b.Entity<CourseGuideRecord>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SupportTicket>().Property(x => x.Origin).HasMaxLength(20);
        b.Entity<Endorsement>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Endorsement>().Property(x => x.Summary).HasMaxLength(ReadinessVocabulary.MaxSummaryLength);
        b.Entity<Endorsement>().Property(x => x.StandardsName).HasMaxLength(StandardSetVocabulary.MaxNameLength);
        b.Entity<Endorsement>().Ignore(x => x.Wording);
        b.Entity<Endorsement>().HasIndex(x => x.CourseId);
        b.Entity<Endorsement>().HasIndex(x => x.RequestId).IsUnique();
        b.Entity<Endorsement>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Endorsement>().HasOne<ReadinessRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Endorsement>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Endorsement>().HasOne<StandardSetVersion>().WithMany().HasForeignKey(x => x.StandardSetVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
