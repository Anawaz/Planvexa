namespace Planvexa.Modules.Tenancy.Domain;

using System.Text;

/// <summary>Produces a valid workspace slug from arbitrary display text.</summary>
public static class SlugGenerator
{
    public static string Generate(string input, string fallback = "workspace")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        var sb = new StringBuilder(input.Length);
        var previousHyphen = false;
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(ch);
                previousHyphen = false;
            }
            else if (!previousHyphen && sb.Length > 0)
            {
                sb.Append('-');
                previousHyphen = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length < 2)
        {
            slug = fallback;
        }

        return slug.Length > 63 ? slug[..63].Trim('-') : slug;
    }
}
