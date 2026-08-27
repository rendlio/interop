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
/// tree a contributor reads. It imports the same props by absolute path, which is what position
/// under <c>src/</c> would otherwise have given it, and it references no package, so pack needs
/// no network.
///
/// These cost a few seconds each, against a suite that otherwise finishes in well under one. That
/// is the price of the question: a placeholder that reaches a public package page cannot be taken
/// back by editing it later, because the version that carried it stays published.
/// </summary>
public sealed class PackagingContractTests
{
    /// <summary>The description the SDK writes for a package that supplies none.</summary>
    private const string SdkPlaceholder = "Package Description";

    private const string RealDescription =
        "Bridges the FooSheet upstream onto the Rendlio Sheets model.";

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

        // Read the shipped artifact rather than the intermediate .nuspec beside it: what a
        // consumer installs is the only copy of this that matters.
        ZipArchiveEntry manifest = Assert.Single(
            archive.Entries,
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));

        using Stream content = manifest.Open();
        XElement metadata = XDocument.Load(content).Root?.Elements().First()
            ?? throw new InvalidOperationException("The packed .nuspec has no <metadata>.");

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

        public Probe(string? description, bool packable = true, bool withReadme = true)
        {
            Root = Path.Combine(Path.GetTempPath(), $"rendlio-pack-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);

            // What position under src/ gives a real adapter. Imported explicitly rather than left
            // to discovery, so that nothing above the temporary directory can join the build.
            string shared =
                Path.Combine(RepositoryLayout.Root.FullName, "src", "Directory.Build.props");
            File.WriteAllText(
                Path.Combine(Root, "Directory.Build.props"),
                "<Project>\n"
                + $"  <Import Project=\"{shared}\" />\n"
                + "</Project>\n");

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
        }

        private string Root { get; }

        private string PackageOutput => Path.Combine(Root, "packages");

        public PackResult Pack()
        {
            ProcessStartInfo start = new()
            {
                FileName = DotnetMuxer(),
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            start.ArgumentList.Add("pack");
            start.ArgumentList.Add(Path.Combine(Root, $"{ProjectName}.csproj"));
            start.ArgumentList.Add("--configuration");
            start.ArgumentList.Add("Release");
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(PackageOutput);
            start.ArgumentList.Add("--nologo");

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

        /// <summary>
        /// The muxer running this test, so the probe builds against the SDK <c>global.json</c>
        /// pins rather than whichever one happens to come first on PATH.
        /// </summary>
        private static string DotnetMuxer()
        {
            string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

            return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
        }
    }
}
