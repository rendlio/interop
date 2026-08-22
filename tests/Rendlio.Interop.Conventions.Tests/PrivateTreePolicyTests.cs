using System.Diagnostics;
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

    /// <summary>
    /// A path shaped like the notes that are actually written. The rule names a directory,
    /// and git applies a directory rule to what is under it rather than to the bare name, so
    /// the probe has to reach inside the tree to ask the question that matters.
    /// </summary>
    private const string NoteInPrivateTree = $"{PrivateWorkingTree}/working-notes/probe.md";

    /// <summary>
    /// How <c>git check-ignore</c> answers: the verdict is the process exit status, and any
    /// other status is the query itself having failed rather than an answer.
    /// </summary>
    private const int Ignored = 0;

    private const int NotIgnored = 1;

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
    /// Guards the test below, and is the reason its answer can be believed. A query that
    /// reported "ignored" for everything — because git never started, ran against some other
    /// directory, or was asked a question it does not answer the way this assumes — would
    /// satisfy that test while checking nothing. This asks the same question about a file the
    /// repository publishes and requires the opposite answer.
    /// </summary>
    [Fact]
    public void The_ignore_query_reports_a_published_file_as_not_ignored()
    {
        (int verdict, string source) = IgnoreQuery("README.md");

        Assert.Equal(NotIgnored, verdict);
        Assert.Equal(string.Empty, source);
    }

    [Fact]
    public void Git_ignores_a_note_written_to_the_private_working_tree()
    {
        // The tests above read the file; this one asks git, and git's answer is the claim
        // that protects anything. A rule can be spelled so that it reads correctly to a
        // person and matches nothing, and precedence is settled across more than one file.
        // The source of the answer is asserted with the answer for that second reason: a
        // rule in one machine's own global ignore file would otherwise stand in for the rule
        // this repository has to carry itself, and pass here while a fresh clone leaks.
        (int verdict, string source) = IgnoreQuery(NoteInPrivateTree);

        Assert.Equal(Ignored, verdict);
        Assert.StartsWith($"{IgnoreFile}:", source, StringComparison.Ordinal);
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

    /// <summary>
    /// Asks git whether it would ignore <paramref name="relativePath"/>, and returns the
    /// verdict together with the rule that produced it. The index is deliberately left out of
    /// the question: the claim is about the rules this repository carries, not about which
    /// paths happen to be committed on the branch under test.
    /// </summary>
    private static (int Verdict, string Source) IgnoreQuery(string relativePath)
    {
        string[] arguments = ["check-ignore", "--no-index", "--verbose", "--", relativePath];

        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = RepositoryLayout.Root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process git = Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not start 'git'. This fixture asks git what it does with the private "
                + "working tree, so the suite has to run from a checkout with git available.");

        // One line at most either way, so neither pipe can fill and stall the process.
        string source = git.StandardOutput.ReadToEnd().Trim();
        string failure = git.StandardError.ReadToEnd().Trim();
        git.WaitForExit();

        Assert.True(
            git.ExitCode is Ignored or NotIgnored,
            $"'git {string.Join(' ', arguments)}' did not answer: it exited with "
            + $"{git.ExitCode}. {failure}");

        return (git.ExitCode, source);
    }
}
