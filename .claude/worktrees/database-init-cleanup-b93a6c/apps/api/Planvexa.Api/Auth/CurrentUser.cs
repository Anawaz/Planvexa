namespace Planvexa.Api.Auth;

using Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// Scoped, mutable current-user holder populated by <see cref="Middleware.UserContextMiddleware"/>
/// after authentication maps the external subject to an application user.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; private set; }
    public Guid UserId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    public void Set(Guid userId, string subject, string email, string displayName)
    {
        UserId = userId;
        Subject = subject;
        Email = email;
        DisplayName = displayName;
        IsAuthenticated = true;
    }
}
