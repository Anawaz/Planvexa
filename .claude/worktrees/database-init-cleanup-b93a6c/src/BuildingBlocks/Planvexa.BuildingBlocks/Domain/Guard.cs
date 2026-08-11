namespace Planvexa.BuildingBlocks.Domain;

/// <summary>Small guard helpers for invariant checks in domain/application code.</summary>
public static class Guard
{
    public static string AgainstNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{paramName}' must not be null or whitespace.", paramName);
        }

        return value;
    }

    public static Guid AgainstEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"'{paramName}' must not be an empty GUID.", paramName);
        }

        return value;
    }
}
