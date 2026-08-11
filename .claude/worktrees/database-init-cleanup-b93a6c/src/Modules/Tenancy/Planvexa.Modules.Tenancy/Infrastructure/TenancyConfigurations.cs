namespace Planvexa.Modules.Tenancy.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Tenancy.Domain;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces", TenancyModule.Schema);
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Slug).HasMaxLength(63).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();

        // Slug is globally unique now that there is no tenant to scope it by.
        builder.HasIndex(w => w.Slug).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

public sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members", TenancyModule.Schema);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(m => m.JoinedAtUtc).IsRequired();

        builder.HasIndex(m => new { m.WorkspaceId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.RoleId);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(m => m.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Nullable: null means "use the fast-path MembershipRole enum value" (ADR-0003). SET
        // NULL on delete so removing a custom role later degrades a member to the enum
        // fast path instead of failing the delete or cascading into the member row.
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(m => m.RoleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(m => m.DomainEvents);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", TenancyModule.Schema);
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Key).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.IsBuiltIn).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();

        builder.HasIndex(r => new { r.WorkspaceId, r.Key }).IsUnique();

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(r => r.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(r => r.DomainEvents);
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", TenancyModule.Schema);
        builder.HasKey(p => new { p.RoleId, p.PermissionKey });

        builder.Property(p => p.PermissionKey).HasMaxLength(64).IsRequired();

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(p => p.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations", TenancyModule.Schema);
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email).HasMaxLength(320).IsRequired();
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.ExpiresAtUtc).IsRequired();

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.WorkspaceId, i.Email });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(i => i.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(i => i.DomainEvents);
    }
}

public sealed class FeatureEntitlementConfiguration : IEntityTypeConfiguration<FeatureEntitlement>
{
    public void Configure(EntityTypeBuilder<FeatureEntitlement> builder)
    {
        builder.ToTable("feature_entitlements", TenancyModule.Schema);
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FeatureKey).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Source).HasMaxLength(64).IsRequired();
        builder.Property(f => f.WorkspaceId).IsRequired();

        builder.HasIndex(f => new { f.WorkspaceId, f.FeatureKey });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(f => f.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(f => f.DomainEvents);
    }
}

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams", TenancyModule.Schema);
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        builder.HasIndex(t => t.WorkspaceId);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(t => t.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(t => t.DomainEvents);
    }
}

public sealed class ResourcePermissionConfiguration : IEntityTypeConfiguration<ResourcePermission>
{
    public void Configure(EntityTypeBuilder<ResourcePermission> builder)
    {
        builder.ToTable("resource_permissions", TenancyModule.Schema);
        builder.HasKey(p => p.Id);

        builder.Property(p => p.ResourceType).HasMaxLength(64).IsRequired();

        // Stored as the exact lower_snake_case values from the ADR-0003 schema spec
        // ("user"/"team"/"role", "view"/"comment"/"edit"/"full_edit"/"share"/"manage"), not EF's
        // default PascalCase enum-name conversion.
        builder.Property(p => p.PrincipalType)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<ResourcePrincipalType>(v, ignoreCase: true))
            .HasMaxLength(16).IsRequired();
        builder.Property(p => p.Level)
            .HasConversion(v => ResourcePermissionLevelText.ToText(v), v => ResourcePermissionLevelText.FromText(v))
            .HasColumnName("permission_level")
            .HasMaxLength(16).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.ResourceType, p.ResourceId, p.PrincipalType, p.PrincipalId }).IsUnique();
        builder.HasIndex(p => new { p.WorkspaceId, p.ResourceType, p.ResourceId });
        builder.HasIndex(p => new { p.WorkspaceId, p.PrincipalType, p.PrincipalId });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(p => p.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(p => p.DomainEvents);
    }
}

public sealed class TeamMembershipConfiguration : IEntityTypeConfiguration<TeamMembership>
{
    public void Configure(EntityTypeBuilder<TeamMembership> builder)
    {
        builder.ToTable("team_members", TenancyModule.Schema);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.AddedAtUtc).IsRequired();

        builder.HasIndex(m => new { m.TeamId, m.UserId }).IsUnique();

        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(m => m.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(m => m.DomainEvents);
    }
}
