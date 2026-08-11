namespace Planvexa.Modules.WorkManagement.Domain;

using System.Globalization;
using System.Text.Json;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// Pure raw-string -&gt; typed-<see cref="CustomFieldValue"/> coercion, extracted from
/// <c>CustomFieldService.ApplyTypedValueAsync</c> so <c>TaskWriteApi.SetCustomFieldValueAsync</c>
/// (Forms' custom-field mapping, an unauthenticated system-actor write) can reuse the exact same
/// validation/parsing without going through <c>CustomFieldService</c>'s per-viewer authorization gate. Does
/// NOT handle <see cref="CustomFieldType.User"/> (needs an async workspace-membership check — stays in the
/// caller). <see cref="CustomFieldType.Team"/> DOES parse/set here, but every caller must run its own async
/// <c>ITeamDirectoryQuery.TeamExistsAsync</c> workspace-ownership check first — a syntactically valid GUID
/// from another workspace must be rejected, not stored. Does not handle
/// <see cref="CustomFieldType.Relationship"/>/Formula/Rollup (never a plain stored value — callers must
/// reject those before calling this).
/// </summary>
public static class CustomFieldValueCoercion
{
    public static void Apply(CustomFieldDefinition definition, CustomFieldValue value, string? rawValue, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            value.SetText(null, now); // clears all projections
            return;
        }

        switch (definition.Type)
        {
            case CustomFieldType.Text:
            case CustomFieldType.LongText:
            case CustomFieldType.Url:
            case CustomFieldType.Email:
                value.SetText(rawValue, now);
                break;

            // Free-text address string — the simpler of the two shapes the design brief
            // offered over structured lat/lng (documented choice; see CustomFieldType.Location).
            case CustomFieldType.Location:
                if (rawValue.Length > 500)
                {
                    throw new ValidationAppException($"'{definition.Name}' must be at most 500 characters.");
                }

                value.SetText(rawValue, now);
                break;

            // Basic format validation only — digits, spaces and the usual phone
            // punctuation, 7-20 characters of content. Not full E.164 validation (documented ceiling).
            case CustomFieldType.Phone:
                if (!System.Text.RegularExpressions.Regex.IsMatch(rawValue, @"^[+\d][\d\s().-]{6,19}$"))
                {
                    throw new ValidationAppException($"'{definition.Name}' does not look like a phone number.");
                }

                value.SetText(rawValue, now);
                break;

            case CustomFieldType.Number:
            case CustomFieldType.Currency:
            case CustomFieldType.Rating:
                if (!decimal.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                {
                    throw new ValidationAppException($"'{definition.Name}' expects a number.");
                }

                value.SetNumber(number, now);
                break;

            // A 0-100 numeric percentage (documented choice over a 0-1 fraction).
            case CustomFieldType.Progress:
                if (!decimal.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var progress) || progress < 0 || progress > 100)
                {
                    throw new ValidationAppException($"'{definition.Name}' expects a number between 0 and 100.");
                }

                value.SetNumber(progress, now);
                break;

            case CustomFieldType.Boolean:
                if (!bool.TryParse(rawValue, out var boolean))
                {
                    throw new ValidationAppException($"'{definition.Name}' expects true or false.");
                }

                value.SetBool(boolean, now);
                break;

            case CustomFieldType.Date:
            case CustomFieldType.DateTime:
                if (!DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                {
                    throw new ValidationAppException($"'{definition.Name}' expects a date.");
                }

                value.SetDate(date, now);
                break;

            case CustomFieldType.Dropdown:
                if (!Guid.TryParse(rawValue, out var optionId) || definition.Options.All(o => o.Id != optionId))
                {
                    throw new ValidationAppException($"'{definition.Name}' expects a valid option id.");
                }

                value.SetOption(optionId, now);
                break;

            case CustomFieldType.MultiSelect:
                var ids = ParseGuidArray(rawValue);
                if (ids.Any(id => definition.Options.All(o => o.Id != id)))
                {
                    throw new ValidationAppException($"'{definition.Name}' contains an unknown option id.");
                }

                value.SetMultiSelect(JsonSerializer.Serialize(ids), now);
                break;

            case CustomFieldType.Team:
                if (!Guid.TryParse(rawValue, out var teamId))
                {
                    throw new ValidationAppException($"'{definition.Name}' expects a team id.");
                }

                value.SetTeam(teamId, now);
                break;

            default:
                throw new ValidationAppException($"'{definition.Name}' cannot be set through this endpoint.");
        }
    }

    private static List<Guid> ParseGuidArray(string raw)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<List<Guid>>(raw);
            if (parsed is not null)
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            // Fall through to comma-separated parsing.
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();
    }
}
