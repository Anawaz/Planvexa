namespace Planvexa.BuildingBlocks.Platform;

/// <summary>
/// One captured log record, persisted so the host administration console can answer "what is broken
/// on this box right now?" without shell access to the server.
///
/// Deliberately NOT a replacement for the OpenTelemetry → Loki/Grafana pipeline, which stays the
/// system of record for full-fidelity logs across replicas and retains far more. This is the
/// operator-visible slice: warnings and errors, short retention, searchable in the browser.
///
/// Plain data with no behaviour and no aggregate identity — a log line is never edited. Lives in
/// BuildingBlocks alongside <see cref="InstanceSettings"/> and <c>OutboxMessage</c> for the same
/// reason: the <c>platform</c> schema belongs to no bounded context.
///
/// PRIVACY: <see cref="Message"/> and <see cref="Exception"/> are whatever the application logged and
/// may contain user data. That is why retention defaults to two weeks and the minimum level defaults
/// to Warning rather than capturing everything.
/// </summary>
public sealed class InstanceLogEntry
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>The <c>LogLevel</c> name — "Warning", "Error", "Critical", ...</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>The logger category, i.e. the fully-qualified type that logged it.</summary>
    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Formatted exception including stack trace, or null when none was supplied.</summary>
    public string? Exception { get; set; }

    /// <summary>Ties the record back to the request that produced it (the <c>X-Correlation-Id</c>
    /// response header), when it was produced during one.</summary>
    public string? CorrelationId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? WorkspaceId { get; set; }
}
