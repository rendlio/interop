using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Rendlio.Interop.Sweep.Sources;

/// <summary>
/// Reads one registry and projects what it publishes into <see cref="Observation"/>. One
/// implementation per registry, because the normalizing is the whole job: the diff downstream
/// is only as comparable as the records these produce.
/// </summary>
public interface IObservationSource
{
    /// <summary>Which registry this reads.</summary>
    SweepSource Source { get; }

    /// <summary>How this source reads a query's term, for the recipe author.</summary>
    string TermMeaning { get; }

    /// <summary>Runs one query.</summary>
    /// <param name="query">The query. Its source must be this source's.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What the registry returned, normalized; empty when it returned nothing.</returns>
    /// <exception cref="SweepException">The registry failed or answered in an unknown shape.</exception>
    Task<IReadOnlyList<Observation>> CollectAsync(SweepQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Shared reading of registry JSON. Each registry publishes a different shape, but every one
/// of them omits fields rather than sending nulls, changes a number's width between releases,
/// and dates things in more than one format — so every projection wants the same tolerance.
/// </summary>
internal static class RegistryJson
{
    /// <summary>Parses a response body, naming the registry when it is not JSON.</summary>
    public static JsonDocument Parse(string body, SweepSource source)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException error)
        {
            throw new SweepException(
                $"{SweepSources.Name(source)} answered with something that is not JSON.", error);
        }
    }

    /// <summary>The named array, or an empty one when the registry omitted it.</summary>
    public static JsonElement.ArrayEnumerator Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement array) && array.ValueKind is JsonValueKind.Array
            ? array.EnumerateArray()
            : default;

    /// <summary>The named object, or null when the registry omitted it.</summary>
    public static JsonElement? Object(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement child) && child.ValueKind is JsonValueKind.Object
            ? child
            : null;

    /// <summary>The named string, trimmed, or null when absent, null, or blank.</summary>
    public static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString()?.Trim();

        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>The named number as a count, or null when absent or not a whole number.</summary>
    public static long? Count(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.Number
        && value.TryGetInt64(out long count)
            ? count
            : null;

    /// <summary>
    /// The named timestamp, normalized to UTC so two runs on machines in different places
    /// cannot disagree about whether a candidate moved.
    /// </summary>
    public static DateTimeOffset? Time(JsonElement element, string name)
    {
        string? text = Text(element, name);

        return text is not null
            && DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTimeOffset time)
            ? time
            : null;
    }

    /// <summary>The text a recipe's patterns are applied to: everything the registry says.</summary>
    public static string Published(string name, string? description) =>
        description is null ? name : $"{name} {description}";
}

/// <summary>
/// Builds request URIs. The host and path come from a constant the source owns and the recipe
/// contributes only escaped query values, so no recipe can send a run somewhere the source did
/// not intend to go.
/// </summary>
internal static class RegistryUri
{
    public static Uri Build(string endpoint, params (string Name, string Value)[] parameters)
    {
        StringBuilder built = new(endpoint);
        char separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        foreach ((string name, string value) in parameters)
        {
            built.Append(separator)
                .Append(Uri.EscapeDataString(name))
                .Append('=')
                .Append(Uri.EscapeDataString(value));

            separator = '&';
        }

        return new Uri(built.ToString(), UriKind.Absolute);
    }

    /// <summary>Renders a page size the way every registry here spells it.</summary>
    public static string Size(int take) => take.ToString(CultureInfo.InvariantCulture);
}
