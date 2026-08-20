using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the rules the README publishes. The rules are binding on every package in this
/// repository, so an edit that drops or reworks one should have to be deliberate and
/// visible in review rather than quietly shipping a weaker promise.
/// </summary>
public sealed class RepositoryRulesTests
{
    private const string LicenceFollowsFunctionRule =
        "moat code = BUSL; funnel code = MIT/Apache";

    private const string ForkRules =
        "fork the dead, ask-with-blessing the sleepy (MiniWord, ClosedXML.Report " +
        "co-maintenance offers), NEVER fork the living (ClosedXML core, MiniExcel - " +
        "active + .NET Foundation)";

    private const string VersionPinningRule =
        "ranges pin to certified upstream versions; widening requires a new certification run first";

    [Fact]
    public void Readme_quotes_the_licence_follows_function_rule_verbatim()
    {
        Assert.Contains(LicenceFollowsFunctionRule, RepositoryLayout.ReadFile("README.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_quotes_the_fork_rules_verbatim()
    {
        Assert.Contains(ForkRules, RepositoryLayout.ReadFile("README.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_quotes_the_version_pinning_rule_verbatim()
    {
        Assert.Contains(VersionPinningRule, RepositoryLayout.ReadFile("README.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_calls_the_engine_source_available_and_never_open_source()
    {
        // The rendering engine is BUSL-licensed. "open source" would be inaccurate, so
        // "source-available" is the term used, and the wrong one must not creep back in.
        string readme = RepositoryLayout.ReadFile("README.md");

        Assert.Contains("source-available", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("open source", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_is_mit_licensed()
    {
        // Rule 1 in the README: everything in this repository is funnel code, so the only
        // licence it can carry is MIT.
        string licence = RepositoryLayout.ReadFile("LICENSE");

        Assert.StartsWith("MIT License", licence, StringComparison.Ordinal);
        Assert.Contains("Permission is hereby granted, free of charge", licence, StringComparison.Ordinal);
    }
}
