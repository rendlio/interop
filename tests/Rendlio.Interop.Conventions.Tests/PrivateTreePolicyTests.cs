using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the ignore rule that keeps the private working tree out of a public history. The
/// publish-hygiene scan covers what this repository ships, and a private note is by
/// definition not in that set — the rules that let it carry internal vocabulary are the same
/// rules that prune it from the scan, so a note that was ever committed would arrive already
/// exempt from every check there is. The ignore rule is the whole defence. The directory is
/// present on essentially every branch, which leaves it one <c>git add -A</c> away from the
/// history of a repository that is read by strangers.
/// </summary>
public sealed class PrivateTreePolicyTests
{
    /// <summary>
    /// The private working tree, named here rather than shared with
    /// <see cref="RepositoryLayout"/>. That type names it in order to prune it, which is a
    /// statement about where the publish scan looks; this is a statement about what git does
    /// with the same directory. Sharing one constant would let a rename satisfy both claims
    /// by moving, and a rename is exactly the case where they should be checked separately.
    /// </summary>
    private const string PrivateWorkingTree = ".conductor";

    private const string IgnoreFile = ".gitignore";

    [Fact]
    public void The_ignore_file_parses_into_individual_rules()
    {
        // Guards the tests below. What can go wrong here is the parse rather than the file:
        // a rule list that is one unsplit blob, or one that still has the commentary in it,
        // would find no re-inclusion for the same reason it would find no rule at all — and
        // only one of those two tests fails when that happens.
        string[] rules = IgnoreRules();

        Assert.Contains("[Bb]in/", rules, StringComparer.Ordinal);
        Assert.DoesNotContain(rules, rule => rule.StartsWith('#'));
    }

    [Fact]
    public void The_private_working_tree_is_ignored()
    {
        bool ignored = IgnoreRules().Any(rule => !IsNegation(rule) && Names(rule));

        Assert.True(
            ignored,
            $"'{IgnoreFile}' carries no rule for '{PrivateWorkingTree}/'. Private notes are "
            + "written there on every branch, and this repository is public.");
    }

    [Fact]
    public void No_rule_re_includes_the_private_working_tree()
    {
        // A negation would undo the rule above without removing it, leaving the line in the
        // file for a reader to find and be reassured by.
        string? negation = IgnoreRules().FirstOrDefault(rule => IsNegation(rule) && Names(rule));

        Assert.True(
            negation is null,
            $"'{IgnoreFile}' puts the private working tree back with \"{negation}\".");
    }

    /// <summary>
    /// The rules git evaluates: commentary and blank lines dropped, surrounding whitespace
    /// and line endings left out of it so a reformat of the file is not a failure.
    /// </summary>
    private static string[] IgnoreRules() =>
    [
        .. RepositoryLayout.ReadFile(IgnoreFile).Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#')),
    ];

    private static bool IsNegation(string rule) => rule.StartsWith('!');

    /// <summary>
    /// Whether a rule names the private tree. A leading slash anchors a pattern to the
    /// repository root, a trailing one restricts it to directories, and a leading
    /// <c>**/</c> is the explicit spelling of the default; each changes where a rule
    /// applies without changing what it names. The negation marker is stripped too, so that
    /// what a rule names can be asked separately from what it then does about it.
    /// </summary>
    private static bool Names(string rule) =>
        rule.TrimStart('!')
            .Replace("**/", string.Empty, StringComparison.Ordinal)
            .Trim('/')
            .Equals(PrivateWorkingTree, StringComparison.Ordinal);
}
