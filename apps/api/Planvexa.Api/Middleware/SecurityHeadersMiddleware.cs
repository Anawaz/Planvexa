namespace Planvexa.Api.Middleware;

/// <summary>
/// Security response headers for every API response. This host is a JSON API — it
/// never renders HTML/CSS/JS of its own in production — so the tightest correct Content-Security-Policy is
/// <c>default-src 'none'</c> (no script/style/image/connect/frame source is ever needed). The one
/// exception is the Scalar API-reference UI mapped only in Development (<c>/scalar</c>, plus the
/// <c>/openapi</c> document it fetches), which loads its own JS/CSS bundle and would break under that
/// policy — those two dev-only paths get no CSP header at all rather than a second, looser policy to
/// maintain.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (!environment.IsDevelopment())
            {
                // HTTP-only in dev/test (no local TLS); HSTS on an http:// origin is a browser no-op
                // anyway, so gating this avoids ever appearing to promise HTTPS during local development.
                headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload";
            }

            var path = context.Request.Path;
            if (!path.StartsWithSegments("/scalar") && !path.StartsWithSegments("/openapi"))
            {
                headers["Content-Security-Policy"] = ApiContentSecurityPolicy;
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
