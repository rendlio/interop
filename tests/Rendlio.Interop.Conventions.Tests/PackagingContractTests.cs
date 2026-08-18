using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Runs the packaging contract rather than reading it. <see cref="BuildContractTests"/> pins the
/// shape of <c>src/Directory.Build.props</c> — that the description guard names the properties it
/// should, that the settings shared by every package page are written once. What MSBuild then
/// does with that shape is a separate question, and not one the shape can answer: a condition can
/// name every property it ought to and still be inverted. This repository has already had one
/// that was, failing the pack of a project that had legitimately opted out of packing. Nothing
/// else catches that, because <c>src/</c> holds no project yet — until the first adapter lands,
/// these settings are never executed by a build.
///
/// The subject is a throwaway project in a temporary directory rather than a real one under
/// <c>src/</c>, for two reasons. The fixtures that scan what this repository publishes walk the
/// tree while these tests run, so a project materialising under <c>src/</c> mid-run would be a
/// race rather than a test. And a package that exists only to be packed does not belong in the
/// tree a contributor reads. It imports the same props by absolute path, and is pointed at the
/// same central versions and the same package sources — which between them are what position under
/// <c>src/</c> would otherwise have given it.
///
/// Every case here starts a restore and a build, which is why this fixture is measured in tens of
/// seconds while the rest of the suite finishes in milliseconds. That is the price of the
/// question: a placeholder on a public package page, a public member a consumer's IntelliSense has
/// nothing to say about, a public member nobody wrote down — none of them can be taken back by
/// editing it later, because the version that carried it stays published.
///
/// The warning policy is run here for the same reason. Warnings are errors, with the four audit
/// codes exempted, and that exemption is only defensible because a workflow drops it again with a
/// command-line <c>-p:WarningsNotAsErrors=</c>. Whether a command line really does outrank an
/// exemption written in a props file is a fact about MSBuild rather than about this repository,
/// and the whole arrangement rests on it, so it is run rather than assumed — with <c>CS1591</c>
/// standing in for an advisory code, which cannot be raised without a vulnerable package and a
/// network to learn about it from.
///
/// So is the public-surface gate, and it is the strongest case in the fixture for running rather
/// than reading. Everything it does is decided inside an analyzer package: whether a project with
/// neither baseline file is treated as one that has not opted in and left alone, whether a project
/// given one file of the two is told so, whether the reference stays out of the dependency list a
/// consumer installs. None of that can be read off <c>src/Directory.Build.props</c>, all of it can
/// change with the version pinned in <c>Directory.Packages.props</c>, and the failure mode of the
/// first — a gate that silently passes — is one nothing else in this repository would report.
/// </summary>
public sealed class PackagingContractTests
{
    /// <summary>The description the SDK writes for a package that supplies none.</summary>
    private const string SdkPlaceholder = "Package Description";

    private const string RealDescription =
        "Bridges the FooSheet upstream onto the Rendlio Sheets model.";

    /// <summary>
    /// The promoted warning that stands in for an advisory code. Any would do; this one is
    /// already raised by <see cref="UndocumentedPublicType"/> and needs no package to produce.
    /// </summary>
    private const string ExemptibleWarning = "CS1591";

    /// <summary>What a workflow passes to drop the exemption for the length of its own run.</summary>
    private const string DropExemption = "-p:WarningsNotAsErrors=";

    /// <summary>
    /// A public type with nothing said about it — what a contributor writes by accident, and
    /// what a consumer would then be shown in IntelliSense: a name and nothing else.
    /// </summary>
    private const string UndocumentedPublicType =
        "namespace Probe;\n"
        + "\n"
        + "public sealed class Bridge\n"
        + "{\n"
        + "    public static int Rows => 0;\n"
        + "}\n";

    /// <summary>The same type, documented. Nothing else about it differs.</summary>
    private const string DocumentedPublicType =
        "namespace Probe;\n"
        + "\n"
        + "/// <summary>Bridges an upstream workbook onto the model.</summary>\n"
        + "public sealed class Bridge\n"
        + "{\n"
        + "    /// <summary>How many rows the bridged sheet has.</summary>\n"
        + "    public static int Rows => 0;\n"
        + "}\n";

    /// <summary>The analyzer that reads the two baseline files, by the id a .nuspec would use.</summary>
    private const string SurfaceAnalyzer = "Microsoft.CodeAnalysis.PublicApiAnalyzers";

    /// <summary>
    /// A declared surface with nothing in it. The header is not decoration: without it the
    /// recorded surface carries no nullability, and a reference-typed member is rejected for
    /// that alone. What a project looks like once it has opted in and before it is public.
    /// </summary>
    private const string NothingDeclared = "#nullable enable\n";

    /// <summary>
    /// The surface of the two types above, as the analyzer spells it. Both declare the same
    /// members — they differ only in comments — so one declaration covers both. The implicit
    /// constructor is a line of its own, which is the sort of member the file exists to make
    /// visible: nobody writes it, everybody ships it.
    /// </summary>
    private const string BridgeDeclared =
        "#nullable enable\n"
        + "Probe.Bridge\n"
        + "Probe.Bridge.Bridge() -> void\n"
        + "static Probe.Bridge.Rows.get -> int\n";

    [Fact]
    public void A_package_that_supplies_no_description_does_not_pack()
    {
        using Probe probe = new(description: null);

        PackResult result = probe.Pack();

        Assert.False(result.Succeeded, $"Pack should have failed but succeeded:\n{result.Output}");

        // This repository's own error, not a NuGet one. Whoever hits it needs to be told what to
        // write and where, which no generic pack failure does.
        Assert.Contains(
            "packs without a description of its own", result.Output, StringComparison.Ordinal);

        // And it has to fail before the package is written. A guard that ran afterwards would
        // leave a publishable .nupkg on disk carrying the placeholder it exists to prevent.
        Assert.Empty(probe.ProducedPackages());
    }

    [Fact]
    public void A_project_that_declines_to_ship_is_never_asked_for_a_description()
    {
        // MSBuild runs a BeforeTargets hook even when the target it precedes is skipped by its own
        // condition, so the guard sees every project under src/ — including one that produces no
        // package and so has no page to describe. This is a regression, not a hypothesis: the
        // guard existed once without its IsPackable clause and failed exactly this build.
        using Probe probe = new(description: null, packable: false, withReadme: false);

        PackResult result = probe.Pack();

        Assert.True(
            result.Succeeded,
            $"A project under src/ that sets IsPackable=false should pack cleanly:\n{result.Output}");
        Assert.Empty(probe.ProducedPackages());
    }

    [Fact]
    public void A_package_that_supplies_no_page_does_not_pack()
    {
        // The other half of the claim the README makes. PackageReadmeFile only names the file; if
        // the project supplies nothing under that name, the name resolves to nothing.
        using Probe probe = new(description: RealDescription, withReadme: false);

        PackResult result = probe.Pack();

        Assert.False(result.Succeeded, $"Pack should have failed but succeeded:\n{result.Output}");

        // Not pinned to a NuGet error code, which is NuGet's to renumber. What matters is that the
        // failure names the file the contributor has to add.
        Assert.Contains("README.md", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_page_is_written_from_the_shared_settings()
    {
        using Probe probe = new(description: RealDescription);

        PackResult result = probe.Pack();

        Assert.True(result.Succeeded, $"Pack should have succeeded:\n{result.Output}");

        string package = Assert.Single(probe.ProducedPackages());
        using ZipArchive archive = ZipFile.OpenRead(package);
        XElement metadata = Manifest(archive);

        Assert.Equal(RealDescription, Field(metadata, "description"));
        Assert.NotEqual(SdkPlaceholder, Field(metadata, "description"));
        Assert.Equal("https://github.com/rendlio/rendlio-interop", Field(metadata, "projectUrl"));
        Assert.Equal("MIT", Field(metadata, "license"));

        // Written semicolon-separated in the props; NuGet stores them separated by spaces.
        Assert.Equal("rendlio rendlio-sheets spreadsheet interop adapter", Field(metadata, "tags"));

        // The name in the metadata is a path relative to the package root, so it is only a page if
        // a file is actually there under that name. Naming it and packing it are two separate
        // settings, and either one alone ships a package whose page does not render.
        Assert.Equal("README.md", Field(metadata, "readme"));
        Assert.Contains(archive.Entries, entry => entry.FullName == "README.md");
    }

    [Fact]
    public void A_public_member_without_documentation_does_not_build()
    {
        // The README's Contributing section promises a contributor exactly this, and the
        // promise is made by two settings meeting: GenerateDocumentationFile under src/
        // produces the warning, warnings-as-errors repository-wide turns it into a failure.
        // Either one on its own is silent, and reading the props can only show that both are
        // written down — not that they still add up.
        //
        // The surface is declared so that the only thing wrong with this project is the missing
        // comments. Left undeclared it would fail for two reasons at once, and the assertion
        // below would no longer be able to tell which.
        using Probe probe = new(
            description: RealDescription,
            source: UndocumentedPublicType,
            declaredApi: BridgeDeclared);

        PackResult result = probe.Build();

        Assert.False(result.Succeeded, $"Build should have failed but succeeded:\n{result.Output}");

        // The diagnostic by number, rather than any failure at all: a project that could not
        // restore, or could not find its target framework, also fails a build while saying
        // nothing whatever about documentation.
        Assert.Contains("CS1591", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_whose_public_members_are_documented_builds()
    {
        // Guards the test above, which a probe that could not build anything at all would
        // satisfy while proving nothing. The only difference between the two projects is the
        // comments, so this is what makes the other one a statement about documentation.
        using Probe probe = new(
            description: RealDescription,
            source: DocumentedPublicType,
            declaredApi: BridgeDeclared);

        PackResult result = probe.Build();

        Assert.True(result.Succeeded, $"Build should have succeeded:\n{result.Output}");
    }

    [Fact]
    public void A_warning_a_props_file_exempts_does_not_fail_the_build()
    {
        // Half of the audit arrangement, and the half that makes the other half mean
        // something: an exemption written as an append to WarningsNotAsErrors keeps a
        // promoted warning from failing a build. Without this, the test below would be
        // satisfied by a build that never promoted the warning in the first place.
        using Probe probe = new(
            description: RealDescription,
            source: UndocumentedPublicType,
            exemptWarning: ExemptibleWarning,
            declaredApi: BridgeDeclared);

        PackResult result = probe.Build();

        Assert.True(result.Succeeded, $"Build should have succeeded:\n{result.Output}");
    }

    [Fact]
    public void An_exemption_is_dropped_by_emptying_it_on_the_command_line()
    {
        // What the audit workflow is built on. The exemption in Directory.Build.props is
        // written as an append to whatever the property already held, so whether emptying it
        // on the command line clears that or is merged into it decides whether the job can
        // fail at all. Were the file to outrank the command line, the audit would run green
        // for ever while reporting on nothing — which is the one failure a scheduled job
        // cannot be relied on to surface, because nobody reads a job that is passing.
        //
        // A second probe rather than a second build of the one above: the same project built
        // twice is incremental, so the compiler would not rerun and the warning would not be
        // reissued. The assertion would then hold for a reason that has nothing to do with
        // the flag.
        using Probe probe = new(
            description: RealDescription,
            source: UndocumentedPublicType,
            exemptWarning: ExemptibleWarning,
            declaredApi: BridgeDeclared);

        PackResult result = probe.Build(DropExemption);

        Assert.False(result.Succeeded, $"Build should have failed but succeeded:\n{result.Output}");

        // As an error specifically. The warning is still reported either way, so matching the
        // bare code would pass against the exempted build this is meant to differ from.
        Assert.Contains($"error {ExemptibleWarning}", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_public_member_the_declared_surface_does_not_record_does_not_build()
    {
        // The claim the whole arrangement rests on, and the one a reviewer cannot be asked to
        // make: this member is public, it is documented, it compiles, and the only thing wrong
        // with it is that nobody wrote it down. Published once, it is permanent — so the build
        // is the last place it can still be stopped.
        using Probe probe = new(
            description: RealDescription,
            source: DocumentedPublicType,
            declaredApi: NothingDeclared);

        PackResult result = probe.Build();

        Assert.False(result.Succeeded, $"Build should have failed but succeeded:\n{result.Output}");

        // By number, not by any failure at all: a project that could not restore also fails a
        // build while saying nothing whatever about a public surface.
        Assert.Contains("RS0016", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_public_member_the_declared_surface_records_builds()
    {
        // Guards the three tests around it, which a probe that could not build anything at all
        // would satisfy while proving nothing. The only difference from the one above is the
        // three lines in PublicAPI.Unshipped.txt, which is what makes the others statements
        // about those lines rather than about the probe.
        using Probe probe = new(
            description: RealDescription,
            source: DocumentedPublicType,
            declaredApi: BridgeDeclared);

        PackResult result = probe.Build();

        Assert.True(result.Succeeded, $"Build should have succeeded:\n{result.Output}");
    }

    [Fact]
    public void A_package_that_declares_no_surface_at_all_does_not_build()
    {
        // The case that decides whether any of this is worth having, and the one that cannot be
        // read off a props file. An analyzer of this kind could reasonably treat a project with
        // neither file as one that has not opted in, and say nothing — which is exactly how the
        // first adapter would ship an unpinned surface while every setting under src/ looked
        // correct and every other test here passed. It does not: with nothing recorded, nothing
        // is declared, and every public member is one the declared surface does not have.
        using Probe probe = new(description: RealDescription, source: DocumentedPublicType);

        PackResult result = probe.Build();

        Assert.False(result.Succeeded, $"Build should have failed but succeeded:\n{result.Output}");
        Assert.Contains("RS0016", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_surface_declared_in_only_one_of_its_two_files_does_not_build()
    {
        // Between "both files" and "neither" is a state where the analyzer has been handed half
        // of what it reads, and it is the state a project drifts into rather than one anybody
        // chooses: a file deleted with the code it recorded, a project copied without both
        // halves. It has to fail, because it is the one shape whose diff looks fine — the
        // reference is there, a baseline is there, and nothing in the props file has moved.
        using Probe probe = new(
            description: RealDescription,
            source: DocumentedPublicType,
            declaredApi: BridgeDeclared,
            withShippedApi: false);

        PackResult result = probe.Build();

        Assert.False(result.Succeeded, $"Build should have failed but succeeded:\n{result.Output}");

        // The missing-file diagnostic specifically. Matching any failure would also pass on the
        // reading that half a baseline leaves every member undeclared, which is a different
        // claim about a different code.
        Assert.Contains("RS0048", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_analyzer_that_pins_the_surface_is_not_shipped_to_a_consumer()
    {
        // Every project under src/ references it, and a package reference is a consumer's
        // dependency by default: one attribute stands between a build-time check and a Roslyn
        // analyzer installed by everyone who installs an adapter — which would break the rule
        // about not adding a dependency to a packable project with the thing added to keep the
        // rule above. Only the packed manifest can say which of the two it is.
        using Probe probe = new(
            description: RealDescription,
            source: DocumentedPublicType,
            declaredApi: BridgeDeclared);

        PackResult result = probe.Pack();

        Assert.True(result.Succeeded, $"Pack should have succeeded:\n{result.Output}");

        string package = Assert.Single(probe.ProducedPackages());
        using ZipArchive archive = ZipFile.OpenRead(package);

        string[] dependencies =
        [
            .. Manifest(archive)
                .Descendants()
                .Where(element => element.Name.LocalName == "dependency")
                .Select(element => element.Attribute("id")?.Value ?? string.Empty),
        ];

        Assert.DoesNotContain(SurfaceAnalyzer, dependencies, StringComparer.OrdinalIgnoreCase);

        // And none of it is carried inside the package either. The two are separate: an asset
        // can be packed without being declared a dependency, and a consumer downloading a
        // Roslyn analyzer with an adapter is the same surprise either way.
        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.Contains(SurfaceAnalyzer, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The <c>metadata</c> of the packed manifest. Read from the shipped artifact rather than
    /// from the intermediate .nuspec beside it: what a consumer installs is the only copy of
    /// this that matters.
    /// </summary>
    private static XElement Manifest(ZipArchive archive)
    {
        ZipArchiveEntry manifest = Assert.Single(
            archive.Entries,
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));

        using Stream content = manifest.Open();

        return XDocument.Load(content).Root?.Elements().First()
            ?? throw new InvalidOperationException("The packed .nuspec has no <metadata>.");
    }

    /// <summary>A nuspec metadata field, looked up by local name because the nuspec is namespaced.</summary>
    private static string? Field(XElement metadata, string name) =>
        metadata.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;

    private readonly record struct PackResult(bool Succeeded, string Output);

    /// <summary>
    /// A minimal package that exists for the length of one test: the settings shared by everything
    /// under <c>src/</c>, plus whichever of the per-project pieces the case under test should have.
    /// </summary>
    private sealed class Probe : IDisposable
    {
        private const string ProjectName = "PackagingProbe";

        /// <summary>
        /// <paramref name="declaredApi"/> is the whole of <c>PublicAPI.Unshipped.txt</c>, header
        /// included; null means the project has neither baseline file, which is a state worth
        /// probing rather than one to avoid. <paramref name="withShippedApi"/> exists only for the
        /// case about a half-written baseline — every real project has both files, and the shipped
        /// one is always empty here because nothing has been published from this repository.
        /// </summary>
        public Probe(
            string? description,
            bool packable = true,
            bool withReadme = true,
            string? source = null,
            string? exemptWarning = null,
            string? declaredApi = null,
            bool withShippedApi = true)
        {
            Root = Path.Combine(Path.GetTempPath(), $"rendlio-pack-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);

            // Written the way this repository writes its own — appended to whatever the property
            // already holds, after the import that supplies it — because how the exemption is
            // spelled is precisely what the command-line flag has to overrule.
            string exemption = exemptWarning is null
                ? string.Empty
                : "  <PropertyGroup>\n"
                    + $"    <WarningsNotAsErrors>$(WarningsNotAsErrors);{exemptWarning}</WarningsNotAsErrors>\n"
                    + "  </PropertyGroup>\n";

            // What position under src/ gives a real adapter. Imported explicitly rather than left
            // to discovery, so that nothing above the temporary directory can join the build.
            string shared =
                Path.Combine(RepositoryLayout.Root.FullName, "src", "Directory.Build.props");

            // The settings above leave the version of every package to central management, and
            // central management is found by walking up from the project — which from a temporary
            // directory finds nothing. Named here, before the import, because the SDK reads this
            // property after Directory.Build.props and before it goes looking on its own. Same
            // file a real adapter would find by position; only the way it is reached differs.
            string centralVersions =
                Path.Combine(RepositoryLayout.Root.FullName, "Directory.Packages.props");

            File.WriteAllText(
                Path.Combine(Root, "Directory.Build.props"),
                "<Project>\n"
                + "  <PropertyGroup>\n"
                + $"    <DirectoryPackagesPropsPath>{centralVersions}</DirectoryPackagesPropsPath>\n"
                + "  </PropertyGroup>\n"
                + $"  <Import Project=\"{shared}\" />\n"
                + exemption
                + "</Project>\n");

            // Where restore is allowed to look. A project under src/ gets this by position too,
            // and it is not incidental: the config clears every inherited source and leaves
            // nuget.org alone, which is how rule 2 is kept. Copied rather than skipped because
            // these probes do restore a package now — the analyzer that reads the files below —
            // and a probe resolving it through whatever feed a machine happens to have configured
            // would be answering a question this repository does not ask.
            File.Copy(
                Path.Combine(RepositoryLayout.Root.FullName, "NuGet.config"),
                Path.Combine(Root, "NuGet.config"));

            List<string> properties = ["    <TargetFramework>net10.0</TargetFramework>"];
            if (description is not null)
            {
                properties.Add("    <Description>" + description + "</Description>");
            }

            if (!packable)
            {
                properties.Add("    <IsPackable>false</IsPackable>");
            }

            File.WriteAllText(
                Path.Combine(Root, $"{ProjectName}.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + string.Join("\n", properties) + "\n"
                + "  </PropertyGroup>\n"
                + "</Project>\n");

            if (withReadme)
            {
                File.WriteAllText(Path.Combine(Root, "README.md"), $"# {ProjectName}\n");
            }

            if (source is not null)
            {
                File.WriteAllText(Path.Combine(Root, "Bridge.cs"), source);
            }

            // Nothing marks these as AdditionalFiles: the analyzer package does that itself, and
            // only for a file that exists — which is why "neither file" and "one file" are states
            // a project can actually be in, and so states worth a test.
            if (declaredApi is not null)
            {
                File.WriteAllText(Path.Combine(Root, "PublicAPI.Unshipped.txt"), declaredApi);

                if (withShippedApi)
                {
                    File.WriteAllText(
                        Path.Combine(Root, "PublicAPI.Shipped.txt"), NothingDeclared);
                }
            }
        }

        private string Root { get; }

        private string PackageOutput => Path.Combine(Root, "packages");

        public PackResult Pack() => Run("pack", "--output", PackageOutput);

        /// <summary>
        /// The same project without the packing step, for a claim about what the compiler does
        /// with the settings under <c>src/</c> rather than about what ends up in a package.
        /// <paramref name="arguments"/> reach MSBuild as they would from a workflow, which is
        /// the only way to state a claim about what a command line overrules.
        /// </summary>
        public PackResult Build(params string[] arguments) => Run("build", arguments);

        private PackResult Run(string verb, params string[] arguments)
        {
            ProcessStartInfo start = new()
            {
                FileName = Toolchain.DotnetMuxer(),
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            start.ArgumentList.Add(verb);
            start.ArgumentList.Add(Path.Combine(Root, $"{ProjectName}.csproj"));
            start.ArgumentList.Add("--configuration");
            start.ArgumentList.Add("Release");
            start.ArgumentList.Add("--nologo");

            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            // An exported Platform reaches MSBuild as a global property and moves the output, so
            // the package would land somewhere this test does not look. It is inherited from
            // whatever shell started the run, which is nothing this repository controls.
            start.Environment.Remove("Platform");

            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start dotnet.");

            // Drain both pipes before waiting: a build that filled one while nobody read it would
            // block for ever rather than fail.
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return new PackResult(process.ExitCode == 0, output.Result + error.Result);
        }

        /// <summary>The packages this probe actually produced, which for most cases is none.</summary>
        public IReadOnlyList<string> ProducedPackages() =>
            Directory.Exists(PackageOutput)
                ? [.. Directory.EnumerateFiles(PackageOutput, "*.nupkg")]
                : [];

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A build node still holding a handle on the output is not a reason to fail a test
                // that has already answered its question. The directory is temporary either way.
            }
        }
    }
}
