using System.Text.RegularExpressions;

namespace Rendlio.Interop.Sweep;

/// <summary>
/// Applies the recipe patterns to the text a registry publishes about a candidate, and
/// records which of them fired.
/// </summary>
/// <remarks>
/// The scanner holds no patterns of its own. What a watch is looking for is the operator
/// decision this tool carries out, not a claim this repository makes, so the expressions
/// arrive with the recipe and only their ids are ever written down.
/// </remarks>
public sealed class ClaimScanner
{
    /// <summary>
    /// How long one pattern may spend on one candidate. Recipe expressions are written by
    /// hand and edited under time pressure, and the classic way to write one that never
    /// finishes is nested quantifiers over a long description. A weekly job that hangs is
    /// worse than one that fails, so this fails.
    /// </summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private readonly (string Id, Regex Pattern)[] patterns;

    /// <summary>Compiles the recipe patterns.</summary>
    /// <param name="patterns">The patterns to apply. May be empty.</param>
    /// <exception cref="SweepException">A pattern is not a usable expression.</exception>
    public ClaimScanner(IReadOnlyList<ClaimPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        this.patterns = new (string, Regex)[patterns.Count];

        for (int index = 0; index < patterns.Count; index++)
        {
            ClaimPattern pattern = patterns[index];

            try
            {
                this.patterns[index] = (
                    pattern.Id,
                    new Regex(pattern.Expression, RegexOptions.IgnoreCase, MatchTimeout));
            }
            catch (ArgumentException error)
            {
                throw new SweepException(
                    $"Pattern {pattern.Id} is not a usable expression: {error.Message}", error);
            }
        }
    }

    /// <summary>Whether there is anything to scan for.</summary>
    public bool IsEmpty => patterns.Length == 0;

    /// <summary>Runs the patterns over one candidate.</summary>
    /// <param name="text">Everything the registry publishes about it.</param>
    /// <returns>The ids that matched, sorted, so two runs order them the same way.</returns>
    /// <exception cref="SweepException">A pattern ran past <see cref="MatchTimeout"/>.</exception>
    public IReadOnlyList<string> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (patterns.Length == 0)
        {
            return [];
        }

        List<string> matched = [];

        foreach ((string id, Regex pattern) in patterns)
        {
            try
            {
                if (pattern.IsMatch(text))
                {
                    matched.Add(id);
                }
            }
            catch (RegexMatchTimeoutException error)
            {
                throw new SweepException(
                    $"Pattern {id} ran for longer than {MatchTimeout.TotalSeconds} seconds on one "
                    + "candidate. It backtracks; rewrite it before the next run.", error);
            }
        }

        matched.Sort(StringComparer.Ordinal);

        return matched;
    }
}
