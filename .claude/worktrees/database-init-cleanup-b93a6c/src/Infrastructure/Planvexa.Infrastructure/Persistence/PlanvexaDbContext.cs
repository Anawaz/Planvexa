namespace Planvexa.Infrastructure.Persistence;

using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.BuildingBlocks.Outbox;
using Planvexa.Modules.Audit;
using Planvexa.Modules.Audit.Domain;
using Planvexa.Modules.Identity;
using Planvexa.Modules.Identity.Domain;
using Planvexa.Modules.Tenancy;
using Planvexa.Modules.Tenancy.Domain;
using Planvexa.Modules.WorkManagement;
using Planvexa.Modules.Collaboration;
using Planvexa.Modules.Notifications;
using Planvexa.Modules.TimeTracking;
using Planvexa.Modules.Planning;
using Planvexa.Modules.Reporting;
using Planvexa.Modules.Documents;
using Planvexa.Modules.Forms;
using Planvexa.Modules.Automations;
using Planvexa.Modules.Integrations;
using Planvexa.Modules.Governance;
using Planvexa.Modules.Ai;
using Planvexa.Modules.Mobile;
using Planvexa.Modules.Chat;
using Planvexa.Modules.Goals;
using Planvexa.Modules.Whiteboards;
using Planvexa.Modules.Clips;
using Planvexa.Infrastructure.Persistence.Repositories;

/// <summary>
/// Single application DbContext (see ADR-0002). Entity configurations are contributed by
/// each module and applied here. Module table boundaries are enforced by schema separation plus
/// architecture tests — modules never query another module's tables directly.
/// </summary>
public sealed class PlanvexaDbContext(
    DbContextOptions<PlanvexaDbContext> options,
    IWorkspaceContextAccessor workspaceAccessor)
    : DbContext(options), IUnitOfWork
{
    public const string PlatformSchema = "platform";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();    public DbSet<FeatureEntitlement> FeatureEntitlements => Set<FeatureEntitlement>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissionGrants => Set<RolePermission>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Read live from the accessor so filters evaluate correctly even when the context instance was
    // created before the workspace was resolved (e.g. during resolution in middleware).
    private bool CurrentHasWorkspace => workspaceAccessor.Current.HasWorkspace;
    private Guid CurrentWorkspaceId => workspaceAccessor.Current.WorkspaceId;
    private string CurrentWorkspaceValue() => CurrentHasWorkspace ? CurrentWorkspaceId.ToString() : string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenancyModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkManagementModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollaborationModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TimeTrackingModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanningModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentsModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FormsModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutomationsModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntegrationsModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GovernanceModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MobileModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GoalsModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WhiteboardsModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClipsModule).Assembly);

        // The apps/collaboration-owned Yjs bridge table (see WhiteboardCollabStateRow's doc
        // comment) isn't a Whiteboards module domain concept, so it isn't picked up by the assembly scan
        // above — configured explicitly, the same way ConfigureOutbox is.
        modelBuilder.ApplyConfiguration(new WhiteboardCollabStateRowConfiguration());

        ConfigureOutbox(modelBuilder);
        UseApplicationAssignedGuidKeys(modelBuilder);
        ApplyWorkspaceQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// All identifiers are application-assigned UUIDv7 (ADR-0014), never store-generated. Marking
    /// Guid keys <c>ValueGeneratedNever</c> ensures EF treats a new entity discovered via a tracked
    /// parent's navigation as Added (INSERT) rather than assuming it already exists (UPDATE).
    /// </summary>
    private static void UseApplicationAssignedGuidKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null)
            {
                continue;
            }

            foreach (var property in primaryKey.Properties)
            {
                if (property.ClrType == typeof(Guid))
                {
                    modelBuilder.Entity(entityType.ClrType).Property(property.Name).ValueGeneratedNever();
                }
            }
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceWorkspaceIsolation();
        ConvertDomainEventsToOutbox();
        await ReapplyWorkspaceSessionAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceWorkspaceIsolation();
        ConvertDomainEventsToOutbox();
        ReapplyWorkspaceSession();
        return base.SaveChanges();
    }

    // The connection interceptor stamps app.current_workspace when a connection opens, but the
    // workspace context can legitimately change AFTER that (workspace bootstrap: onboarding sets the
    // context for the workspace it is about to create). Re-apply before writing so the hardened RLS
    // WITH CHECK sees the current context on an already-open connection.
    private async Task ReapplyWorkspaceSessionAsync(CancellationToken cancellationToken)
    {
        if (CurrentHasWorkspace && Database.GetDbConnection().State == System.Data.ConnectionState.Open)
        {
            var workspace = CurrentWorkspaceValue();
            await Database.ExecuteSqlAsync(
                $"SELECT set_config('app.current_workspace', {workspace}, false)", cancellationToken);
        }
    }

    private void ReapplyWorkspaceSession()
    {
        if (CurrentHasWorkspace && Database.GetDbConnection().State == System.Data.ConnectionState.Open)
        {
            var workspace = CurrentWorkspaceValue();
            Database.ExecuteSql($"SELECT set_config('app.current_workspace', {workspace}, false)");
        }
    }

    /// <summary>
    /// Stamps <see cref="IWorkspaceOwned.WorkspaceId"/> on newly-added child entities from the
    /// resolved workspace context (ADR 0015). Aggregate roots set WorkspaceId explicitly from their
    /// parent, so only rows left empty are stamped. A missing workspace context is rejected to keep
    /// workspace-owned rows from being written without an owner. Any row already carrying a
    /// WorkspaceId that does not match the ambient workspace is rejected too — Workspace is the sole
    /// isolation boundary now, so this is the only cross-workspace write guard left.
    /// </summary>
    private void EnforceWorkspaceIsolation()
    {
        var hasWorkspace = CurrentHasWorkspace;
        var workspaceId = CurrentWorkspaceId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not IWorkspaceOwned)
            {
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var property = entry.Property(nameof(IWorkspaceOwned.WorkspaceId));
            var currentValue = (Guid)(property.CurrentValue ?? Guid.Empty);

            if (entry.State == EntityState.Added && currentValue == Guid.Empty)
            {
                if (!hasWorkspace)
                {
                    throw new CrossWorkspaceAccessException(
                        $"Cannot persist workspace-owned '{entry.Entity.GetType().Name}' without a workspace context.");
                }

                property.CurrentValue = workspaceId;
                continue;
            }

            if (hasWorkspace && currentValue != workspaceId)
            {
                throw new CrossWorkspaceAccessException(
                    $"Attempted to write '{entry.Entity.GetType().Name}' for workspace {currentValue} " +
                    $"while operating in workspace {workspaceId}.");
            }
        }
    }

    private void ConvertDomainEventsToOutbox()
    {
        var entities = ChangeTracker.Entries<Entity>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var entity in entities)
        {
            var owningWorkspaceId = entity switch
            {
                IWorkspaceOwned owned => owned.WorkspaceId,
                _ => (Guid?)null,
            };

            foreach (var domainEvent in entity.DomainEvents)
            {
                var type = domainEvent.GetType();
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    WorkspaceId = owningWorkspaceId,
                    Type = type.FullName ?? type.Name,
                    Payload = JsonSerializer.Serialize(domainEvent, type, JsonOptions),
                    OccurredOnUtc = domainEvent.OccurredOnUtc,
                    CorrelationId = CurrentHasWorkspace ? workspaceAccessor.Current.CorrelationId : null,
                });
            }

            entity.ClearDomainEvents();
        }
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages", PlatformSchema);
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Type).HasMaxLength(512).IsRequired();
            builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(m => m.CorrelationId).HasMaxLength(128);
            builder.Property(m => m.Error).HasMaxLength(2048);
            builder.HasIndex(m => m.ProcessedOnUtc);
        });
    }

    /// <summary>
    /// Apply query filters for IWorkspaceOwned entities, filtering by workspace ID. Workspace itself
    /// is excluded — it is the top-level collection, accessed by id/bootstrap membership rather than
    /// filtered by an ambient workspace.
    /// </summary>
    private void ApplyWorkspaceQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IWorkspaceOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            if (entityType.ClrType == typeof(Workspace))
            {
                continue;
            }

            var method = typeof(PlanvexaDbContext)
                .GetMethod(nameof(BuildWorkspaceFilter), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType.ClrType);

            var filter = method.Invoke(this, null);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter((LambdaExpression)filter!);
        }
    }

    private LambdaExpression BuildWorkspaceFilter<TEntity>() where TEntity : class, IWorkspaceOwned
    {
        Expression<Func<TEntity, bool>> filter =
            e => !CurrentHasWorkspace || e.WorkspaceId == CurrentWorkspaceId;
        return filter;
    }
}
