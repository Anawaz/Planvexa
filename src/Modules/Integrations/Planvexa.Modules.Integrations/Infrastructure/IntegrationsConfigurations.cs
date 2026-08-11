namespace Planvexa.Modules.Integrations.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.Modules.Integrations.Domain;

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> b)
    {
        b.ToTable("webhook_subscriptions", IntegrationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        b.Property(x => x.Secret).HasMaxLength(128).IsRequired();
        b.Property(x => x.EventTypesCsv).HasMaxLength(512).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.IsActive });
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.EventTypes);
    }
}

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.ToTable("webhook_deliveries", IntegrationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(500);
        b.Property(x => x.PayloadJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.SubscriptionId, x.EventId }).IsUnique();
        b.HasIndex(x => new { x.SubscriptionId, x.OccurredAtUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> b)
    {
        b.ToTable("personal_access_tokens", IntegrationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Subject).HasMaxLength(256).IsRequired();
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ScopesCsv).HasMaxLength(512).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.WorkspaceId, x.UserId });
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.Scopes);
    }
}

public sealed class OAuthApplicationConfiguration : IEntityTypeConfiguration<OAuthApplication>
{
    public void Configure(EntityTypeBuilder<OAuthApplication> b)
    {
        b.ToTable("oauth_applications", IntegrationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
        b.Property(x => x.ClientSecretHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.RedirectUrisCsv).HasMaxLength(2048).IsRequired();
        b.Property(x => x.AllowedScopesCsv).HasMaxLength(512).IsRequired();
        b.HasIndex(x => x.ClientId).IsUnique();
        b.HasIndex(x => x.WorkspaceId);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.RedirectUris);
        b.Ignore(x => x.AllowedScopes);
    }
}

public sealed class OAuthAuthorizationCodeConfiguration : IEntityTypeConfiguration<OAuthAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<OAuthAuthorizationCode> b)
    {
        b.ToTable("oauth_authorization_codes", IntegrationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.RedirectUri).HasMaxLength(2048).IsRequired();
        b.Property(x => x.ScopesCsv).HasMaxLength(512).IsRequired();
        b.HasIndex(x => x.CodeHash).IsUnique();
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.Scopes);
    }
}

public sealed class OAuthTokenConfiguration : IEntityTypeConfiguration<OAuthToken>
{
    public void Configure(EntityTypeBuilder<OAuthToken> b)
    {
        b.ToTable("oauth_tokens", IntegrationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccessTokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.RefreshTokenHash).HasMaxLength(128);
        b.Property(x => x.ScopesCsv).HasMaxLength(512).IsRequired();
        b.HasIndex(x => x.AccessTokenHash).IsUnique();
        b.HasIndex(x => x.RefreshTokenHash).IsUnique();
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.Scopes);
    }
}

public sealed class IntegrationProviderSettingsConfiguration : IEntityTypeConfiguration<IntegrationProviderSettings>
{
    public void Configure(EntityTypeBuilder<IntegrationProviderSettings> b)
    {
        b.ToTable("provider_settings", IntegrationsModule.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        b.Property(x => x.ConfigJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.SecretEncrypted).IsRequired();
        b.HasIndex(x => new { x.WorkspaceId, x.Provider }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}
