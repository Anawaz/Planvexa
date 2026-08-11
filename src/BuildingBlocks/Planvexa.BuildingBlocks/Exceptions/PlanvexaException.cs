namespace Planvexa.BuildingBlocks.Exceptions;

using Planvexa.BuildingBlocks.Primitives;

/// <summary>Base for exceptions that carry a domain <see cref="Error"/> and map to an HTTP status.</summary>
public abstract class PlanvexaException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
    public abstract int StatusCode { get; }
}

public sealed class ValidationAppException(string message)
    : PlanvexaException(Error.Validation(message))
{
    public override int StatusCode => StatusCodes.BadRequest;
}

public sealed class NotFoundException(string message)
    : PlanvexaException(Error.NotFound(message))
{
    public override int StatusCode => StatusCodes.NotFound;
}

public sealed class ConflictException(string message)
    : PlanvexaException(Error.Conflict(message))
{
    public override int StatusCode => StatusCodes.Conflict;
}

public sealed class ForbiddenException(string message)
    : PlanvexaException(Error.Forbidden(message))
{
    public override int StatusCode => StatusCodes.Forbidden;
}

public sealed class UnauthorizedAppException(string message)
    : PlanvexaException(Error.Unauthorized(message))
{
    public override int StatusCode => StatusCodes.Unauthorized;
}

/// <summary>
/// A workspace-configured external dependency (e.g. the workspace's AI provider endpoint) failed. Surfaced as
/// 502 so the caller can tell "your provider is misconfigured/down" from "our server broke".
/// </summary>
public sealed class ExternalServiceException(string message)
    : PlanvexaException(new Error("external_service", message))
{
    public override int StatusCode => StatusCodes.BadGateway;
}

/// <summary>
/// A cross-workspace access attempt was detected in the domain/persistence layer. This is a security
/// event and should never occur under correct authorization — it is a defence in depth.
/// </summary>
public sealed class CrossWorkspaceAccessException(string message)
    : PlanvexaException(Error.Forbidden(message))
{
    public override int StatusCode => StatusCodes.Forbidden;
}

/// <summary>The workspace's monthly AI credit limit (real-provider calls only) has been reached.
/// The offline extractive fallback is never blocked by this.</summary>
public sealed class CreditLimitExceededException(string message)
    : PlanvexaException(new Error("credit_limit_exceeded", message))
{
    public override int StatusCode => StatusCodes.TooManyRequests;
}

internal static class StatusCodes
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int TooManyRequests = 429;
    public const int BadGateway = 502;
}
