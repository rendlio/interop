using System.Xml.Linq;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the build settings the README promises a consumer. Each one is a published claim —
/// that warnings are errors, that a package can only ship from <c>src/</c>, that an
/// undocumented public member fails the build, that an upstream is always the published
/// package. Turning one off is a change to what this repository promises, so it should show
/// up as a failing test rather than as a quiet edit to a props file.
/// </summary>
public sealed class BuildContractTests
{
    private const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    [Theory]
    [InlineData("Nullable", "enable")]
    [InlineData("TreatWarningsAsErrors", "true")]
    [InlineData("EnableNETAnalyzers", "true")]
    public void The_repository_wide_build_settings_hold(string property, string expected)
    {
        Assert.Equal(expected, Property("Directory.Build.props", property));
    }

    [Fact]
    public void Nothing_packs_unless_it_opts_in()
    {
        // The default is off repository-wide and back on only under src/, so a project can
        // ship a package only by being placed there.
        Assert.Equal("false", Property("Directory.Build.props", "IsPackable"));
        Assert.Equal("true", Property("src/Directory.Build.props", "IsPackable"));
        Assert.Equal("false", Property("tests/Directory.Build.props", "IsPackable"));

        // Restated under tools/ rather than left to the default. A tool is run from a
        // checkout, and one that started shipping to consumers by accident would be a change
        // to what this repository publishes.
        Assert.Equal("false", Property("tools/Directory.Build.props", "IsPackable"));
    }

    [Fact]
    public void A_package_documents_its_public_members()
    {
        // With warnings as errors this is what turns an undocumented public member into a
        // build failure, which is the promise the README's Contributing section makes.
        Assert.Equal("true", Property("src/Directory.Build.props", "GenerateDocumentationFile"));
    }

    [Fact]
    public void Every_package_this_repository_produces_is_mit()
    {
        // Rule 1: funnel code is MIT. This property is what stamps that onto the .nupkg.
        Assert.Equal("MIT", Property("src/Directory.Build.props", "PackageLicenseExpression"));
    }

    [Fact]
    public void Package_versions_are_managed_centrally()
    {
        // Rule 3 is enforced by the ranges in Directory.Packages.props, which only bind
        // while central management is on.
        Assert.Equal("true", Property("Directory.Packages.props", "ManagePackageVersionsCentrally"));
    }

    [Fact]
    public void Restore_resolves_upstreams_from_nuget_org_only()
    {
        // Rule 2: an adapter's upstream is the published, unmodified package. Any other
        // feed could satisfy that dependency from somewhere else.
        XElement sources = Document("NuGet.config").Root?.Element("packageSources")
            ?? throw new InvalidOperationException("NuGet.config declares no <packageSources>.");

        Assert.Equal("clear", sources.Elements().First().Name.LocalName);

        string[] configured = [.. sources.Elements("add").Select(add => add.Attribute("value")?.Value ?? string.Empty)];

        Assert.Equal([NuGetOrg], configured);
    }

    private static string? Property(string relativePath, string name) =>
        Document(relativePath).Descendants(name).FirstOrDefault()?.Value.Trim();

    private static XDocument Document(string relativePath) =>
        XDocument.Parse(RepositoryLayout.ReadFile(relativePath));
}
