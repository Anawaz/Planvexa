namespace Planvexa.Api.Platform;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;

/// <summary>Configuration for the host console's instance log store (<c>InstanceLogs:*</c>).</summary>
public sealed class InstanceLogOptions
{
    public const string SectionName = "InstanceLogs";

    /// <summary>
    /// Lowest level captured. Warning, not Information, because these records are persisted, may
    /// contain user data, and the OpenTelemetry pipeline already has the full stream — the console
    /// exists to surface what went wrong, not to mirror everything.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;

    /// <summary>Days a record is kept. Bounds how long logged user data lives in the database.</summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>
    /// Bound on the in-memory queue. When it is full, new records are DROPPED rather than blocking the
    /// thread that logged them — a logging call must never be able to stall a request, and a stalled
    /// request would in turn generate more logs.
    /// </summary>
    public int Capacity { get; set; } = 2_000;

    /// <summary>Set false to capture nothing (the console then shows an empty log with an explanation).</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// An <see cref="ILoggerProvider"/> that queues records for <see cref="InstanceLogBackgroundService"/>
/// to persist. No logging dependency is added for this (Serilog and friends are not in
/// Directory.Packages.props and are not needed): <c>ILoggerProvider</c> plus a bounded
/// <see cref="Channel{T}"/> is the whole mechanism.
///
/// Two properties this must never violate:
/// <list type="number">
/// <item><b>Never block the caller.</b> The channel is bounded with <c>DropWrite</c>, so a burst that
/// outruns the writer loses the newest records and increments <see cref="Dropped"/> (surfaced on the
/// health page) instead of stalling a request thread inside a logging call.</item>
/// <item><b>Never feed itself.</b> Categories that can be emitted BY the write path — this namespace
/// and Npgsql's own — are excluded. Without that, one failing insert logs an error, which queues
/// another insert, which fails: an unbounded loop that would survive as long as the database is
/// unhappy, which is exactly when it is most harmful.</item>
/// </list>
/// </summary>
public sealed class InstanceLogProvider : ILoggerProvider
{
    private static readonly string[] ExcludedCategoryPrefixes =
    [
        // This provider, the background writer, and the health service that reads the same table.
        "Planvexa.Api.Platform.",
        // The database driver: a warning raised while persisting a record would queue another record.
        "Npgsql",
    ];

    private readonly Channel<InstanceLogEntry> _channel;
    private readonly ConcurrentDictionary<string, InstanceLogger> _loggers = new(StringComparer.Ordinal);
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IClock _clock;
    private int _dropped;

    public InstanceLogProvider(
        IOptions<InstanceLogOptions> options, IHttpContextAccessor httpContextAccessor, IClock clock)
    {
        Options = options.Value;
        _httpContextAccessor = httpContextAccessor;
        _clock = clock;
        _channel = Channel.CreateBounded<InstanceLogEntry>(
            new BoundedChannelOptions(Math.Max(1, Options.Capacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            });
    }

    public InstanceLogOptions Options { get; }

    public ChannelReader<InstanceLogEntry> Reader => _channel.Reader;

    /// <summary>Records lost to a full queue since start-up. Reported by the health endpoint so a
    /// silently truncated log is visible rather than looking like a quiet system.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new InstanceLogger(this, name));

    public void Dispose() => _channel.Writer.TryComplete();

    private static bool IsExcluded(string category)
        => ExcludedCategoryPrefixes.Any(prefix => category.StartsWith(prefix, StringComparison.Ordinal));

    private void Enqueue(InstanceLogEntry entry)
    {
        if (!_channel.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Best-effort request attribution. The provider is a singleton so it cannot hold scoped services;
    /// they are resolved from the live <see cref="HttpContext"/> instead, and every failure mode
    /// (no request, a scope already disposed, services not registered) simply yields nulls — all three
    /// columns are nullable, and losing attribution must never lose the record or throw inside a
    /// logging call.
    /// </summary>
    private (string? CorrelationId, Guid? UserId, Guid? WorkspaceId) Attribution()
    {
        try
        {
            if (_httpContextAccessor.HttpContext is not { } context)
            {
                return (null, null, null);
            }

            // Set by WorkspaceResolutionMiddleware at the very start of the request, so it is present
            // even for a request that failed before reaching an endpoint.
            var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();

            var user = context.RequestServices.GetService<ICurrentUser>();
            var workspace = context.RequestServices.GetService<IWorkspaceContextAccessor>()?.Current;

            return (
                string.IsNullOrWhiteSpace(correlationId) ? null : correlationId,
                user is { IsAuthenticated: true } ? user.UserId : null,
                workspace is { HasWorkspace: true } ? workspace.WorkspaceId : null);
        }
        catch (ObjectDisposedException)
        {
            // The request scope ended while a background continuation was still logging.
            return (null, null, null);
        }
        catch (InvalidOperationException)
        {
            // Resolving a scoped service outside a usable scope.
            return (null, null, null);
        }
    }

    private sealed class InstanceLogger(InstanceLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
            => provider.Options.Enabled
                && logLevel >= provider.Options.MinimumLevel
                && logLevel != LogLevel.None
                && !IsExcluded(category);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var (correlationId, userId, workspaceId) = provider.Attribution();
            provider.Enqueue(new InstanceLogEntry
            {
                Id = Guid.CreateVersion7(),
                CreatedAtUtc = provider._clock.UtcNow,
                Level = logLevel.ToString(),
                // The column is 256 chars; a generic type's category name can exceed that.
                Category = Truncate(category, 256) ?? string.Empty,
                Message = formatter(state, exception),
                Exception = exception?.ToString(),
                CorrelationId = Truncate(correlationId, 128),
                UserId = userId,
                WorkspaceId = workspaceId,
            });
        }

        /// <summary>Null-preserving, so an absent correlation id stays NULL rather than becoming "".</summary>
        private static string? Truncate(string? value, int max)
            => value is null || value.Length <= max ? value : value[..max];
    }
}
