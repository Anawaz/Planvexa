namespace Planvexa.Api.Auth;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

/// <summary>
/// Development/test authentication. Reads identity from request headers so integration tests can
/// exercise authenticated + authorized endpoints without a running Keycloak:
///   X-Debug-Subject, X-Debug-Email, X-Debug-Name.
/// NEVER registered in Production (Program.cs selects JWT Bearer / Keycloak there).
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";
    public const string SubjectHeader = "X-Debug-Subject";
    public const string EmailHeader = "X-Debug-Email";
    public const string NameHeader = "X-Debug-Name";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers[SubjectHeader].ToString();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var email = Request.Headers[EmailHeader].ToString();
        if (string.IsNullOrWhiteSpace(email))
        {
            email = $"{subject}@dev.planvexa.local";
        }

        var name = Request.Headers[NameHeader].ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = email;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim("sub", subject),
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email),
            new Claim(ClaimTypes.Name, name),
            new Claim("name", name),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
