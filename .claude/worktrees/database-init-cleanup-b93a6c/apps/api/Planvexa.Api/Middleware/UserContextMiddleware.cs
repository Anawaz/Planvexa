namespace Planvexa.Api.Middleware;

using System.Security.Claims;
using Planvexa.Api.Auth;
using Planvexa.SharedContracts.Users;

/// <summary>
/// After authentication, maps the external subject to an application user (provisioning on first
/// sight) and populates the scoped <see cref="CurrentUser"/>.
/// </summary>
public sealed class UserContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated == true)
        {
            var subject = principal.FindFirstValue("sub")
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrWhiteSpace(subject))
            {
                var email = principal.FindFirstValue("email")
                    ?? principal.FindFirstValue(ClaimTypes.Email)
                    ?? $"{subject}@unknown.local";
                var name = principal.FindFirstValue("name")
                    ?? principal.FindFirstValue(ClaimTypes.Name)
                    ?? email;

                var directory = context.RequestServices.GetRequiredService<IUserDirectory>();
                var info = await directory.GetOrProvisionAsync(subject, email, name, context.RequestAborted);

                var currentUser = context.RequestServices.GetRequiredService<CurrentUser>();
                currentUser.Set(info.UserId, subject, info.Email, info.DisplayName);
            }
        }

        await next(context);
    }
}
