using System.Globalization;

namespace CasePriority.Api.Http;

/// <summary>
/// Translates between a case's numeric version and a strong ETag / If-Match
/// value. Deliberately supports exactly ONE strong ETag (e.g. <c>"1"</c>) — no
/// weak validators, lists, or wildcards — to keep the accepted format explicit.
/// </summary>
public static class EntityTagVersion
{
    public static string Format(long version)
    {
        return $"\"{version}\"";
    }

    public static long ParseRequired(string? rawIfMatch)
    {
        if (string.IsNullOrWhiteSpace(rawIfMatch))
        {
            throw new PreconditionRequiredException(
                "This operation requires an If-Match header.");
        }

        var value = rawIfMatch.Trim();

        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ||
            value.Length < 3 ||
            value[0] != '"' ||
            value[^1] != '"' ||
            value.Contains(','))
        {
            throw new ArgumentException(
                "If-Match must contain one strong version ETag, for example \"1\".",
                "ifMatch");
        }

        var versionText = value[1..^1];

        if (!long.TryParse(
                versionText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var version) ||
            version < 1)
        {
            throw new ArgumentException(
                "If-Match must contain a positive numeric version.",
                "ifMatch");
        }

        return version;
    }
}
