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
    public void Readme_calls_the_engine_source_available()
    {
        // The rendering engine is BUSL-licensed, and this is the page that has to say so.
        //
        // The matching half of the rule — that the inaccurate term never appears — is no
        // longer asserted here. It holds for every page this repository publishes now, in
        // PublicSurfaceRulesTests, which reaches further than this page and is also the only
        // file allowed to quote the phrase in order to search for it. Repeating it here
        // would put the phrase into a shipped file and redden the wider rule.
        Assert.Contains(
            "source-available",
            RepositoryLayout.ReadFile("README.md"),
            StringComparison.OrdinalIgnoreCase);
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
