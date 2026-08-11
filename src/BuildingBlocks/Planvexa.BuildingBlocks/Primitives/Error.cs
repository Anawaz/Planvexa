namespace Planvexa.BuildingBlocks.Primitives;

/// <summary>A machine-readable error with a stable code and a human-readable message.</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string message) => new("validation", message);
    public static Error NotFound(string message) => new("not_found", message);
    public static Error Conflict(string message) => new("conflict", message);
    public static Error Forbidden(string message) => new("forbidden", message);
    public static Error Unauthorized(string message) => new("unauthorized", message);
}
