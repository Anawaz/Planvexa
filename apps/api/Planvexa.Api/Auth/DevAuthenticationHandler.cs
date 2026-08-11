namespace Planvexa.Api.Auth;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

/// <summary>
/// Development/test authentication. Reads identity from request headers so integration tests can
/// exercise authenticated + authorized endpoints without a running Keycloak:
///   X-Debug-Subject, X-Debug-Email, X-Debug-Name, X-Debug-Amr.
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

    /// <summary>Comma-separated "amr" values, e.g. "pwd,otp" — lets tests simulate an MFA-verified
    /// session without a running Keycloak (see UserContextMiddleware.MfaAmrValues).</summary>
    public const string AmrHeader = "X-Debug-Amr";

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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new("sub", subject),
            new(ClaimTypes.Email, email),
            new("email", email),
            new(ClaimTypes.Name, name),
            new("name", name),
        };

        var amr = Request.Headers[AmrHeader].ToString();
        if (!string.IsNullOrWhiteSpace(amr))
        {
            claims.AddRange(amr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => new Claim("amr", value)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
