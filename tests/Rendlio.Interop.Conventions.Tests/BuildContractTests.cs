using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Pins the build settings the README promises a consumer. Each one is a published claim —
/// that warnings are errors, that a package can only ship from <c>src/</c>, that an
/// undocumented public member fails the build, that an upstream is always the published
/// package, that a range resolves to the graph its lock records, that a package page is
/// written rather than defaulted. Turning one off is a change to what this repository
/// promises, so it should show up as a failing test rather than as a quiet edit to a props
/// file.
///
/// Everything here reads a setting. Whether a setting still produces the behaviour it was
/// written for is a different question and cannot be answered by reading: that is what
/// <see cref="PackagingContractTests"/> and <see cref="SolutionBuildTests"/> are for, and
/// where the claims about an undocumented member and an inherited platform are actually run.
/// </summary>
public sealed partial class BuildContractTests
{
    private const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    private const string LockFileName = "packages.lock.json";

    private const string SolutionFileName = "Rendlio.Interop.slnx";

    private const string PackagingProps = "src/Directory.Build.props";

    private const string SolutionProps = "Directory.Solution.props";

    /// <summary>The only platform this solution declares, spelled the way a solution spells it.</summary>
    private const string DefaultPlatform = "Any CPU";

    /// <summary>How a workflow drops the audit exemption for the length of its own run.</summary>
    private const string AuditPromotion = "-p:WarningsNotAsErrors=";

    private const string RepositoryHome = "https://github.com/rendlio/rendlio-interop";

    /// <summary>Shared tags; a project appends its upstream rather than replacing these.</summary>
    private const string SharedPackageTags = "rendlio;rendlio-sheets;spreadsheet;interop;adapter";

    /// <summary>
    /// What the NuGet audit reports against the advisory database. These four are the only
    /// warnings this repository declines to fail on, and only because a job of their own asks
    /// the question on a schedule instead. Sorted, because the assertion compares against a
    /// sorted list rather than against the order somebody typed them in.
    /// </summary>
    private static readonly string[] AuditWarnings = ["NU1901", "NU1902", "NU1903", "NU1904"];

    /// <summary>
    /// What the SDK puts in the <c>description</c> field of a package that sets none. It packs
    /// without complaint, so this exact string is what a consumer would read on the page.
    /// </summary>
    private const string SdkPlaceholderDescription = "Package Description";

    /// <summary>
    /// The property pack actually reads for the <c>description</c> field. The SDK defaults it
    /// from <c>Description</c>, so a guard on this one covers a project that set either.
    /// </summary>
    private const string DescriptionProperty = "$(PackageDescription)";

    /// <summary>
    /// The opt-out a project under <c>src/</c> uses to decline to ship. The description guard
    /// has to test it, because a <c>BeforeTargets</c> hook runs even when the target it
    /// precedes is skipped by its own condition.
    /// </summary>
    private const string PackableProperty = "$(IsPackable)";

    /// <summary>
    /// The analyzer that reads a project's two <c>PublicAPI</c> files and fails the build over
    /// a public member neither of them records.
    /// </summary>
    private const string SurfaceAnalyzer = "Microsoft.CodeAnalysis.PublicApiAnalyzers";

    [Theory]
    [InlineData("Nullable", "enable")]
    [InlineData("TreatWarningsAsErrors", "true")]
    [InlineData("EnableNETAnalyzers", "true")]
    public void The_repository_wide_build_settings_hold(string property, string expected)
    {
        Assert.Equal(expected, Property("Directory.Build.props", property));
    }

    [Fact]
    public void A_solution_build_does_not_take_its_platform_from_the_environment()
    {
        // MSBuild reads Platform from the environment, and a solution build then demands a
        // solution configuration for whatever it found there. Only the defaults are declared,
        // so a shell that has run vcvarsall.bat stops restore, build, test, pack and format
        // before a project is loaded. An assignment in a file settles it, because a file beats
        // the environment and loses to a command-line -p: — which is the order this wants.
        // What that assignment then does is run in SolutionBuildTests.
        Assert.Equal(DefaultPlatform, Property(SolutionProps, "Platform"));
    }

    [Fact]
    public void The_audit_is_the_only_warning_this_repository_declines_to_fail_on()
    {
        // Every other warning is an error because it would otherwise reappear in a consumer's
        // build, where they cannot fix it. The audit is exempt for the opposite reason: it
        // reports on a database that changes with nobody committing anything, so it would
        // redden work that has nothing to do with it. That argument covers these four codes
        // and no others, so anything else added here is a promise being weakened.
        string exempted = Property("Directory.Build.props", "WarningsNotAsErrors") ?? string.Empty;

        string[] listed =
        [
            .. exempted
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                // The property's own prior value, which the assignment appends to rather than
                // replaces. It is a reference, not a code.
                .Where(entry => !entry.StartsWith("$(", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(AuditWarnings, listed);
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
        // build failure, which is the promise the README's Contributing section makes. Half
        // of that promise is this setting; that the two halves still add up to a failed build
        // is run rather than read, in PackagingContractTests.
        Assert.Equal("true", Property(PackagingProps, "GenerateDocumentationFile"));
    }

    [Fact]
    public void A_package_records_its_public_surface_in_a_file_the_build_reads()
    {
        // The other half of what src/ promises about a public member. Documentation makes the
        // member explicable; this makes it deliberate. A member is permanent from the version
        // that first carried it, and the only moment it can still be taken back is before
        // that version exists — so the surface is kept in PublicAPI.Shipped.txt and
        // PublicAPI.Unshipped.txt beside the project, and a member neither file records fails
        // the build. What the analyzer does with those files is run in PackagingContractTests;
        // this pins that it is still referenced, which is the part a props file can answer.
        List<XElement> referenced =
        [
            .. Document(PackagingProps)
                .Descendants("PackageReference")
                .Where(reference => reference.Attribute("Include")?.Value == SurfaceAnalyzer),
        ];

        Assert.True(
            referenced.Count == 1,
            $"{PackagingProps} should reference {SurfaceAnalyzer} exactly once, but references "
            + $"it {referenced.Count} times.");

        // The attribute that keeps it a build-time tool rather than something every consumer
        // installs alongside an adapter. Losing it would satisfy every other assertion here.
        Assert.Equal("all", referenced[0].Attribute("PrivateAssets")?.Value);

        // No version on the reference itself: versions are managed centrally, and one written
        // here would be an error under that rather than an override of it.
        Assert.Null(referenced[0].Attribute("Version"));

        // Said rather than left to restore. src/ holds no project yet, so nothing in this
        // repository's own build resolves this reference, and the central version could go
        // missing without any of it turning red.
        Assert.Contains(
            Document("Directory.Packages.props").Descendants("PackageVersion"),
            declared => declared.Attribute("Include")?.Value == SurfaceAnalyzer
                && declared.Attribute("Version")?.Value.Length > 0);
    }

    [Fact]
    public void Every_package_this_repository_produces_is_mit()
    {
        // Rule 1: funnel code is MIT. This property is what stamps that onto the .nupkg.
        Assert.Equal("MIT", Property(PackagingProps, "PackageLicenseExpression"));
    }

    [Theory]
    [InlineData("PackageProjectUrl", RepositoryHome)]
    [InlineData("PackageTags", SharedPackageTags)]
    public void The_part_of_a_package_page_that_is_the_same_everywhere_is_written_once(
        string property, string expected)
    {
        // A package page is the first thing a consumer reads, and the SDK writes one for a
        // package that says nothing. Whatever is identical across every adapter is set here
        // so no adapter can ship a page by accident.
        Assert.Equal(expected, Property(PackagingProps, property));
    }

    [Fact]
    public void Every_package_ships_a_page_of_its_own()
    {
        // PackageReadmeFile only names the file; the item is what puts it in the package.
        // Both point at the project's own README rather than this repository's, because a
        // page describes the one package a consumer is installing. Pack fails when it is
        // missing, which is the point.
        Assert.Equal("README.md", Property(PackagingProps, "PackageReadmeFile"));

        List<XElement> packed =
        [
            .. Document(PackagingProps)
                .Descendants("None")
                .Where(none =>
                    none.Attribute("Include")?.Value.EndsWith("README.md", StringComparison.Ordinal) == true),
        ];

        // Exactly one, named rather than counted implicitly: two would be the shape of a
        // second readme item added later, and pack would then have to choose between them.
        Assert.True(
            packed.Count == 1,
            $"{PackagingProps} should pack exactly one README, but packs {packed.Count}.");

        Assert.Equal("true", packed[0].Attribute("Pack")?.Value);
        Assert.Contains(
            "MSBuildProjectDirectory",
            packed[0].Attribute("Include")?.Value ?? string.Empty,
            StringComparison.Ordinal);

        // Coupled to PackageReadmeFile above, which names a path relative to the package
        // root: pack it anywhere else and the name no longer resolves.
        Assert.Equal("/", packed[0].Attribute("PackagePath")?.Value);
    }

    [Fact]
    public void No_package_ships_without_a_description_of_its_own()
    {
        // Description names one package, so unlike the rest of the page it cannot be set
        // centrally — and the SDK's default for it packs without complaint, which is how
        // placeholder text reaches a public package page. This is the only place that
        // omission can be caught, so it is caught as a failed pack.
        List<XElement> guards =
        [
            .. Document(PackagingProps)
                .Descendants("Target")
                .Where(target => target.Attribute("BeforeTargets")?.Value == "GenerateNuspec")
                .Elements("Error"),
        ];

        Assert.True(
            guards.Count == 1,
            $"{PackagingProps} should fail the pack of a package that sets no description in "
            + $"exactly one place, but {guards.Count} were found.");

        string condition = guards[0].Attribute("Condition")?.Value ?? string.Empty;

        // A project under src/ may decline to ship, and MSBuild runs a BeforeTargets hook
        // even when the target it precedes is skipped by its own condition. Without this the
        // guard demands a description from a project that produces no package.
        Assert.Contains(PackableProperty, condition, StringComparison.Ordinal);

        // On the property pack reads, not on Description. The two are not interchangeable:
        // the SDK defaults this one from Description, so guarding it accepts a project that
        // set either spelling, while guarding Description would reject one that set only
        // PackageDescription.
        Assert.Contains(DescriptionProperty, condition, StringComparison.Ordinal);

        // Checking only for an empty description would let the SDK's placeholder through,
        // and the placeholder is the case that actually reaches a consumer.
        Assert.Contains(SdkPlaceholderDescription, condition, StringComparison.Ordinal);
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

    [Fact]
    public void Nothing_but_the_configured_sources_decides_where_a_package_comes_from()
    {
        // Clearing packageSources is not the whole job: a package source mapping decides
        // which of the configured sources serves which package, and one is inherited from
        // whatever the machine happens to have. It cannot defeat rule 2 — a mapping only
        // narrows sources that are already defined, so a hostile one fails a restore loudly
        // rather than substituting an upstream quietly — but a mapping nobody here wrote is
        // a restore that reproduces differently on a contributor's box, and that has already
        // cost one false result.
        XElement mapping = Document("NuGet.config").Root?.Element("packageSourceMapping")
            ?? throw new InvalidOperationException(
                "NuGet.config declares no <packageSourceMapping>, so it inherits the machine's.");

        Assert.Equal("clear", mapping.Elements().First().Name.LocalName);

        // Nothing after the clear on purpose: with no pattern declared, mapping is off and
        // <packageSources> is the only answer. An entry here would be a second, quieter
        // place to change where a package comes from.
        Assert.Empty(mapping.Elements("packageSource"));
    }

    [Fact]
    public void Restore_writes_down_the_versions_a_range_resolved_to()
    {
        // Rule 3 constrains a range, and a range is satisfiable by more than one version.
        // The lock is what turns that constraint into a fact on disk that a reviewer can see.
        Assert.Equal("true", Property("Directory.Build.props", "RestorePackagesWithLockFile"));
    }

    [Fact]
    public void Every_project_in_the_solution_commits_its_lock()
    {
        string[] projects = [.. SolutionProjects()];

        // Guards the assertion below. An empty project list would satisfy it while checking
        // nothing, which is how this claim would go quiet without anyone noticing.
        Assert.NotEmpty(projects);

        string[] unlocked =
        [
            .. projects.Where(project => !File.Exists(Path.Combine(
                Path.GetDirectoryName(Path.Combine(RepositoryLayout.Root.FullName, project))
                    ?? RepositoryLayout.Root.FullName,
                LockFileName))),
        ];

        Assert.True(
            unlocked.Length == 0,
            $"These projects restore without a committed {LockFileName}, so nothing records "
            + $"what their ranges resolved to: {string.Join(", ", unlocked)}. Regenerate with "
            + "a force-evaluate restore and commit what it writes.");
    }

    [Fact]
    public void Ci_restores_against_the_committed_lock()
    {
        // Every workflow, not just the one that restores today. The lock is only binding
        // because CI refuses to resolve a range afresh, so a second workflow added later
        // that restored without the flag would be a way back out of rule 3 — and it would
        // be added by whoever needed a second workflow, not by whoever wrote this rule.
        (string Name, string Text)[] workflows = [.. Workflows()];

        // Guards the assertion below. No workflow at all, or none that restores, satisfies
        // "none of them restores unlocked" while enforcing nothing.
        Assert.NotEmpty(workflows);
        Assert.Contains(workflows, workflow =>
            workflow.Text.Contains("dotnet restore", StringComparison.Ordinal));

        string[] unlocked =
        [
            .. workflows
                .Where(workflow => UnlockedRestorePattern().IsMatch(workflow.Text))
                .Select(workflow => workflow.Name),
        ];

        Assert.True(
            unlocked.Length == 0,
            $"These workflows restore without locked mode: {string.Join(", ", unlocked)}. "
            + "Restore then resolves the ranges afresh, and an upstream that drifted inside "
            + "one enters the build unreviewed.");
    }

    [Fact]
    public void The_exempted_audit_warnings_are_asked_about_by_a_job_of_their_own()
    {
        // The exemption above is only defensible while the question is still being asked. A
        // workflow deleted, or rewritten by somebody who did not know what that flag was
        // paying for, leaves the exemption behind as a hole nothing reports: the advisory
        // database goes unread and no build says so.
        (string Name, string Text)[] workflows = [.. Workflows()];

        Assert.NotEmpty(workflows);

        bool asked = workflows.Any(workflow =>
            workflow.Text.Contains(AuditPromotion, StringComparison.Ordinal));

        Assert.True(
            asked,
            $"No workflow runs with '{AuditPromotion}', so nothing promotes the audit warnings "
            + "back to errors. Directory.Build.props exempts them from warnings-as-errors on "
            + "the understanding that a job of their own asks the question instead; without "
            + "that job the exemption is a warning nobody reads.");
    }

    [Fact]
    public void No_workflow_cancels_an_in_progress_run_on_a_push()
    {
        (string Name, string Text)[] workflows = [.. Workflows()];

        // Guards the assertion below. With no workflow triggered by a push there is nothing
        // here to get wrong, and the rule would hold by describing nothing.
        Assert.NotEmpty(workflows);
        Assert.Contains(workflows, workflow => PushTriggerPattern().IsMatch(workflow.Text));

        string[] cancelling =
        [
            .. workflows
                .Where(workflow =>
                    PushTriggerPattern().IsMatch(workflow.Text)
                    && UnconditionalCancelPattern().IsMatch(workflow.Text))
                .Select(workflow => workflow.Name),
        ];

        Assert.True(
            cancelling.Length == 0,
            $"These workflows cancel an in-progress run on a push: {string.Join(", ", cancelling)}. "
            + "Merges land here by fast-forward and can arrive seconds apart, so the run being "
            + "cancelled is the one recording the verdict for a commit that is already on the "
            + "branch, and that commit ends up with no verdict at all. Make the cancellation "
            + "conditional on the event being a pull request, where superseding a run is the "
            + "point of it.");
    }

    /// <summary>
    /// Every CI workflow, as a repository-relative path and its text. Enumerated by
    /// <see cref="RepositoryLayout"/> rather than here, because <see cref="SdkPinTests"/> holds
    /// rules about workflows too: two fixtures listing them separately would be two answers to
    /// "what is a workflow here", and the rules that use this only bind while they cover all
    /// of them.
    /// </summary>
    private static IEnumerable<(string Name, string Text)> Workflows() =>
        RepositoryLayout.EnumerateWorkflows();

    /// <summary>The projects the solution builds, as repository-relative paths.</summary>
    private static IEnumerable<string> SolutionProjects() =>
        XDocument.Parse(RepositoryLayout.ReadFile(SolutionFileName))
            .Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value ?? string.Empty)
            .Where(path => path.Length > 0);

    /// <summary>
    /// What a file sets a property to, and a failure if it sets it more than once. Reading
    /// only the first occurrence is how a setting goes half-verified: a second assignment
    /// further down is the one that would win, and a conditioned one means the value depends
    /// on something this never looked at. Either way the answer here would stop describing
    /// the build.
    /// </summary>
    private static string? Property(string relativePath, string name)
    {
        List<XElement> declarations = [.. Document(relativePath).Descendants(name)];

        Assert.True(
            declarations.Count <= 1,
            $"{relativePath} sets <{name}> {declarations.Count} times, so what it evaluates to "
            + "is decided by order or by a condition rather than by any one of them. Settle it "
            + "in one place, or assert on this property somewhere that can tell them apart.");

        return declarations.SingleOrDefault()?.Value.Trim();
    }

    private static XDocument Document(string relativePath) =>
        XDocument.Parse(RepositoryLayout.ReadFile(relativePath));

    /// <summary>
    /// A restore that is not held to the lock. The whitespace is loose on purpose: YAML is
    /// free to fold a command across lines, and a restore that lost the flag by being
    /// reformatted is the same defect as one that never had it.
    /// </summary>
    [GeneratedRegex(@"dotnet\s+restore(?!\s+--locked-mode\b)")]
    private static partial Regex UnlockedRestorePattern();

    /// <summary>
    /// A workflow triggered by a push. Anchored to the indentation a trigger has, so that the
    /// word occurring in a comment or a step name is not read as one.
    /// </summary>
    [GeneratedRegex(@"(?m)^\s{2}push:")]
    private static partial Regex PushTriggerPattern();

    /// <summary>Cancellation with nothing deciding when it applies.</summary>
    [GeneratedRegex(@"cancel-in-progress:\s*true\b")]
    private static partial Regex UnconditionalCancelPattern();
}
