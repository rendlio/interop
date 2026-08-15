using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Holds <c>global.json</c> to pinning one SDK feature band, holds every workflow to
/// installing the band it names, and holds the page a contributor reads before their first
/// build to stating it.
/// </summary>
/// <remarks>
/// The three are one claim taken at each end. A pin that names a band is worth nothing if CI
/// installs its SDK from somewhere else, and both are worth nothing to somebody who cannot
/// find out what to install — so the page that states the band is checked against the pin
/// rather than left to go stale beside it.
/// <para>
/// The two ends read the same file differently, which is what makes this worth pinning at all:
/// <c>setup-dotnet</c> takes the <c>version</c> field and installs exactly that, never
/// applying the roll-forward policy, while the SDK on a contributor's machine applies that
/// policy to whatever is already installed. A repository that lets those answers drift builds
/// perfectly well on every machine, right up to the day two of them disagree about the same
/// commit — and because the analyzers that decide whether this compiles ship inside the SDK,
/// and warnings are errors here, the disagreement arrives as a red build on somebody's desk
/// against a green one in CI with nothing in the diff to explain it.
/// </para>
/// <para>
/// None of that is observable from a build, which is the whole difficulty: every one of them
/// passes until the day one does not. Directly: with the band left free, a pin naming
/// <c>10.0.100</c> resolves <c>10.0.400</c> on a machine holding only the newer band, while CI
/// installs <c>10.0.100</c> exactly — two analyzer sets, one commit. Held inside the band, the
/// same pin refuses to start and names the version it wanted. These rules are the only place
/// that difference is visible before it costs somebody an afternoon.
/// </para>
/// </remarks>
public sealed partial class SdkPinTests
{
    /// <summary>The file every build in this repository resolves its SDK against.</summary>
    private const string PinFileName = "global.json";

    /// <summary>The page a contributor reads before their first build.</summary>
    private const string ContributorPage = "README.md";

    /// <summary>
    /// The extension of a page somebody reads. The rule that scans for a band the pin no
    /// longer names reads these and not source, because a fixture's own cases name bands on
    /// purpose and a rule that reddened on its own test data would be one nobody could keep.
    /// </summary>
    private const string PageExtension = ".md";

    /// <summary>
    /// The roll-forward policies that cannot leave the feature band the version names. A patch
    /// level inside a band is a servicing release carrying that band's analyzers; crossing a
    /// band is the move that changes what the compiler says about unchanged code, so these
    /// three are the only ones under which the pin still decides anything. <c>disable</c> takes
    /// the named version alone, <c>patch</c> and <c>latestPatch</c> take a patch level of it —
    /// every other policy the SDK offers is free to answer with a different band.
    /// </summary>
    private static readonly string[] BandBoundPolicies = ["latestPatch", "patch", "disable"];

    [Fact]
    public void The_repository_pins_one_SDK_feature_band() =>
        Assert.Empty(Inspect(RepositoryLayout.ReadFile(PinFileName)));

    [Fact]
    public void The_file_the_rule_reads_is_the_one_every_build_resolves_against()
    {
        // Guards the rule above, which is the only thing here that reads the committed file.
        // An SDK looks for global.json from the current directory upwards, so the one that
        // decides a build is the one at the root — and ReadFile resolves from the root and
        // fails naming the path, which is the assertion that it is there at all.
        Pin pin = ReadPin(RepositoryLayout.ReadFile(PinFileName));

        Assert.True(
            pin.HasSdkSection,
            $"{PinFileName} has no \"sdk\" section, so every field the rule above reads is "
            + "absent and it has nothing to report.");

        // A file whose version were missing would satisfy the page rule below by looking for
        // a band derived from nothing, and would report only one violation above rather than
        // the two a file that pins nothing usable deserves.
        string version = Assert.IsType<string>(pin.Version);

        // Reducible to a band, which is what the page states and what a contributor installs.
        Assert.EndsWith("xx", FeatureBand(version), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_workflow_installs_the_SDK_the_pin_names()
    {
        (string Name, string Text)[] workflows = [.. RepositoryLayout.EnumerateWorkflows()];

        // Guard the assertion below. A repository with no workflow, or none that installs an
        // SDK, satisfies "none of them installs the wrong one" while enforcing nothing.
        Assert.NotEmpty(workflows);
        Assert.Contains(workflows, workflow => SetupStepPattern().IsMatch(workflow.Text));

        List<string> violations = [];

        foreach ((string name, string text) in workflows)
        {
            int installs = SetupStepPattern().Count(text);
            int fromPin = PinFileInputPattern().Count(text);

            if (installs != fromPin)
            {
                violations.Add(
                    $"{name} installs an SDK {installs} time(s) but reads {PinFileName} "
                    + $"{fromPin} time(s)");
            }

            // A version on the step is a second copy of the pin, and a copy drifts: the two
            // would then have to be changed together by whoever moves the band, and nothing
            // would say so. Unlike the sibling analyzer repository this one has no job that
            // needs a second SDK beside the pinned one, so the rule here is absolute — if
            // that changes, the exception belongs in this comment and in the pattern, spelled
            // rather than left to a reader to infer from a step that looks like a mistake.
            if (OwnVersionPattern().IsMatch(text))
            {
                violations.Add($"{name} names an SDK version of its own");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{string.Join("; ", violations)}. Every step that installs an SDK has to take its "
            + $"version from {PinFileName} and name none of its own, or CI builds a commit on a "
            + "different SDK than the pin says and the pin stops meaning anything.");
    }

    [Fact]
    public void The_page_a_contributor_reads_states_the_band_the_pin_names()
    {
        // Derived from the pin rather than written down twice, so moving the pin without
        // updating the page fails here instead of leaving a contributor installing whatever
        // the page used to say.
        string version = Assert.IsType<string>(ReadPin(RepositoryLayout.ReadFile(PinFileName)).Version);
        string page = RepositoryLayout.ReadFile(ContributorPage);

        Assert.Contains(FeatureBand(version), page, StringComparison.Ordinal);

        // The band is what has to be installed; the exact version is what CI installs and
        // what somebody comparing their machine against this repository needs to see.
        Assert.Contains(version, page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("latestFeature")]
    [InlineData("latestMinor")]
    [InlineData("latestMajor")]
    [InlineData("feature")]
    [InlineData("minor")]
    [InlineData("major")]
    public void A_policy_that_can_leave_the_feature_band_is_reported(string rollForward)
    {
        string violation = Assert.Single(Inspect(PinText("10.0.400", rollForward, allowPrerelease: false)));

        Assert.Contains("outside the feature band", violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("latestPatch")]
    [InlineData("patch")]
    [InlineData("disable")]
    public void A_policy_that_cannot_leave_the_feature_band_is_clean(string rollForward)
    {
        string pinText = PinText("10.0.400", rollForward, allowPrerelease: false);

        // Guards the assertion below: a reader that had stopped finding the fields would
        // report nothing about anything, and every clean case would pass for that reason.
        Assert.Equal(rollForward, ReadPin(pinText).RollForward);

        Assert.Empty(Inspect(pinText));
    }

    [Theory]
    [InlineData("10.0")]
    [InlineData("10.0.4")]
    [InlineData("10.0.x")]
    [InlineData("10.0.4xx")]
    [InlineData("10.0.400-preview.1.25000.1")]
    public void A_version_that_names_no_feature_band_is_reported(string version)
    {
        // A roll-forward policy is relative to the version it starts from, so a band-bound
        // policy on a version that names no band holds nothing. The last case matters most:
        // allowPrerelease excludes a preview from being *resolved*, and says nothing about
        // one being *named* — a pin can ask for a version it also forbids.
        string violation = Assert.Single(Inspect(PinText(version, "latestPatch", allowPrerelease: false)));

        Assert.Contains("not an exact SDK version", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_that_leaves_the_roll_forward_policy_unstated_is_reported()
    {
        // Left out, the policy comes from whatever the SDK defaults to. That is strictness
        // held by something outside this repository, which can move without anyone here
        // committing anything, and which the next person to read the file cannot tell apart
        // from a decision.
        string violation = Assert.Single(Inspect(
            """
            {
              "sdk": {
                "version": "10.0.400",
                "allowPrerelease": false
              }
            }
            """));

        Assert.Contains("\"rollForward\" unstated", violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pin_that_lets_a_preview_win_is_reported(bool stated)
    {
        // Both spellings of the same hole, because the SDK's default here is permissive:
        // saying nothing is a preview being allowed to answer, not nothing happening. A
        // preview bundles analyzers that no released band does, so it is a third answer to
        // the question these rules exist to give one answer to.
        string pinText = stated
            ? PinText("10.0.400", "latestPatch", allowPrerelease: true)
            : """
              {
                "sdk": {
                  "version": "10.0.400",
                  "rollForward": "latestPatch"
                }
              }
              """;

        Assert.Contains(
            "allowPrerelease",
            Assert.Single(Inspect(pinText)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_no_sdk_section_is_reported()
    {
        // Valid JSON, and a pin of nothing: every machine resolves whatever it likes. Reported
        // once rather than as one violation per absent field, which would bury it.
        string violation = Assert.Single(Inspect("""{ "msbuild-sdks": {} }"""));

        Assert.Contains("no \"sdk\" section", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_that_is_wrong_twice_is_reported_twice()
    {
        // The rule returns every violation rather than the first, so a file with two problems
        // does not have to be fixed, re-run and fixed again.
        List<string> violations = Inspect(PinText("10.0", "latestFeature", allowPrerelease: false));

        Assert.True(
            violations.Count == 2,
            "A version that names no band and a policy that can leave one are two problems, "
            + $"and this reported {violations.Count}: {string.Join("; ", violations)}");
    }

    [Theory]
    [InlineData("10.0.400", "10.0.4xx")]
    [InlineData("10.0.100", "10.0.1xx")]
    [InlineData("9.0.203", "9.0.2xx")]
    [InlineData("10.0.900", "10.0.9xx")]
    public void A_version_names_the_band_the_way_the_SDK_documents_one(string version, string band) =>
        Assert.Equal(band, FeatureBand(version));

    [Fact]
    public void A_version_that_names_no_band_cannot_be_reduced_to_one() =>
        // Rather than returning something band-shaped that no SDK would recognise, which the
        // page rule would then look for and find missing for the wrong reason.
        Assert.Throws<FormatException>(() => FeatureBand("10.0.x"));

    [Theory]
    [InlineData("      - uses: actions/setup-dotnet@v4\n        with:\n          global-json-file: global.json\n")]
    [InlineData("      - uses: actions/setup-dotnet@v4\n        with:\n          global-json-file: ./global.json\n")]
    public void A_step_that_takes_its_SDK_from_the_pin_is_clean(string step)
    {
        int installs = SetupStepPattern().Count(step);
        int fromPin = PinFileInputPattern().Count(step);

        Assert.True(installs == 1, $"Read {installs} SDK installs in one step.");
        Assert.True(fromPin == 1, $"Read {fromPin} readings of the pin in a step that takes it.");
        Assert.DoesNotMatch(OwnVersionPattern(), step);
    }

    [Theory]
    // A version of its own, whether or not the pin is read as well.
    [InlineData("      - uses: actions/setup-dotnet@v4\n        with:\n          dotnet-version: 10.0.x\n")]
    [InlineData(
        "      - uses: actions/setup-dotnet@v4\n        with:\n          global-json-file: global.json\n"
        + "          dotnet-version: 10.0.x\n")]
    public void A_step_that_decides_its_SDK_somewhere_else_is_reported(string step) =>
        Assert.Matches(OwnVersionPattern(), step);

    [Fact]
    public void A_second_pin_file_is_not_read_as_this_one()
    {
        // The rule counts steps against readings of *this* file, so a step pointed at another
        // pin has to come out as a step that does not read it rather than as one that does.
        const string step =
            "      - uses: actions/setup-dotnet@v4\n        with:\n"
            + "          global-json-file: eng/global.json\n";

        int installs = SetupStepPattern().Count(step);
        int fromPin = PinFileInputPattern().Count(step);

        Assert.True(installs == 1, $"Read {installs} SDK installs in one step.");
        Assert.True(fromPin == 0, $"Read a pin at another path as this one {fromPin} time(s).");
    }

    [Fact]
    public void The_reader_finds_every_install_in_a_file_rather_than_the_first()
    {
        // A workflow with two jobs has two install steps, and a rule that stopped at the
        // first would accept a second job installing anything it liked.
        const string workflow =
            "      - uses: actions/setup-dotnet@v4\n        with:\n"
            + "          global-json-file: global.json\n"
            + "      - uses: actions/setup-dotnet@v4\n        with:\n"
            + "          dotnet-version: 9.0.x\n";

        int installs = SetupStepPattern().Count(workflow);
        int fromPin = PinFileInputPattern().Count(workflow);

        Assert.True(installs == 2, $"Read {installs} of the two SDK installs in the file.");
        Assert.True(fromPin == 1, $"Read {fromPin} readings of the pin where one is written.");
    }

    [Fact]
    public void No_page_names_a_feature_band_other_than_the_pinned_one()
    {
        // The rule above asks that the band appears somewhere, and cannot see a second band
        // that also appears. Both failures have one cause: moving the pin and updating one
        // sentence out of two. What is left behind still reads as an instruction, so a
        // contributor who finds the stale sentence installs a band this repository refuses and
        // gets a build that stops naming a version nothing they read mentioned. Every shipped
        // page is scanned rather than only the one, because the sentence that goes stale is
        // whichever page somebody was not looking at.
        string band = FeatureBand(
            Assert.IsType<string>(ReadPin(RepositoryLayout.ReadFile(PinFileName)).Version));

        List<string> mentions = [];
        List<string> violations = [];

        foreach (string path in RepositoryLayout.EnumerateShippedFiles())
        {
            if (!PageExtension.Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match mention in BandPattern().Matches(File.ReadAllText(path)))
            {
                string named = $"{RepositoryLayout.Describe(path)} names \"{mention.Value}\"";
                mentions.Add(named);

                if (!string.Equals(mention.Value, band, StringComparison.Ordinal))
                {
                    violations.Add(named);
                }
            }
        }

        // Guards the assertion below, which a scan that had stopped finding bands at all — a
        // pattern that no longer matches how one is spelled, an enumeration that no longer
        // reaches the pages — would satisfy while reading nothing.
        Assert.NotEmpty(mentions);

        Assert.True(
            violations.Count == 0,
            $"{string.Join("; ", violations)}, but {PinFileName} pins \"{band}\". A page left "
            + "naming the band the pin used to hold still reads as the one to install.");
    }

    [Fact]
    public void The_SDK_that_builds_this_repository_resolves_inside_the_pinned_band()
    {
        // Every rule above reads the pin. This one reads what the pin produces, which is a
        // different question and the only one that can catch the file having stopped being the
        // one that decides: moved out of the root, shadowed by a second global.json nearer the
        // projects, or answered by a preview that allowPrerelease was opened up to admit. Each
        // of those leaves text these rules pass on while a build resolves something else — the
        // exact shape of the split this file exists to close.
        string pinned = Assert.IsType<string>(ReadPin(RepositoryLayout.ReadFile(PinFileName)).Version);
        (int ExitCode, string Output) resolved = AskTheMuxerItsVersion();

        // A muxer that could not satisfy the pin says so and names the version it wanted,
        // which is the message worth reporting — of far more use than a band comparison
        // against whatever an unsuccessful run left on the pipe.
        Assert.True(
            resolved.ExitCode == 0,
            $"Asking the SDK its version from the repository root exited with "
            + $"{resolved.ExitCode}, so nothing here resolved {PinFileName}: {resolved.Output}");

        // Reported rather than thrown out of the middle of FeatureBand below, because the way
        // this actually arrives is a prerelease label — the one answer no released band gives.
        Assert.True(
            ExactVersionPattern().IsMatch(resolved.Output),
            $"The SDK building this repository reports \"{resolved.Output}\", which is not an "
            + $"exact SDK version and so belongs to no released feature band, where "
            + $"{PinFileName} names \"{pinned}\".");

        Assert.Equal(FeatureBand(pinned), FeatureBand(resolved.Output));
    }

    /// <summary>
    /// What the pin says, as the three fields that decide which SDK a build gets. A null field
    /// is one the file leaves out, which is never the same as one it sets: the SDK supplies its
    /// own answer for a field nobody wrote, and for <c>allowPrerelease</c> that answer is
    /// permissive.
    /// </summary>
    private readonly record struct Pin(
        bool HasSdkSection, string? Version, string? RollForward, bool? AllowPrerelease);

    /// <summary>
    /// A pin file with the three fields set, for a fixture that breaks one on purpose. Built
    /// rather than kept as files, so what a case is testing is visible where it is asserted.
    /// </summary>
    private static string PinText(string version, string rollForward, bool allowPrerelease) =>
        $$"""
        {
          "sdk": {
            "version": "{{version}}",
            "rollForward": "{{rollForward}}",
            "allowPrerelease": {{(allowPrerelease ? "true" : "false")}}
          }
        }
        """;

    /// <summary>
    /// The three fields, or the absence of each. A wrongly-typed field reads as absent so that
    /// it is reported as the hole it is rather than thrown out of the middle of a rule.
    /// </summary>
    private static Pin ReadPin(string pinText)
    {
        JsonDocumentOptions options = new()
        {
            // Read it the way the SDK does, which tolerates both. A file the SDK accepts must
            // not fail here as malformed — that would be this fixture reporting on its own
            // parser rather than on the pin.
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        using JsonDocument document = JsonDocument.Parse(pinText, options);

        if (!document.RootElement.TryGetProperty("sdk", out JsonElement sdk))
        {
            return new Pin(HasSdkSection: false, null, null, null);
        }

        return new Pin(
            HasSdkSection: true,
            Text(sdk, "version"),
            Text(sdk, "rollForward"),
            sdk.TryGetProperty("allowPrerelease", out JsonElement allowPrerelease)
                ? allowPrerelease.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                }
                : null);

        static string? Text(JsonElement sdk, string name) =>
            sdk.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Everything wrong with a pin, as messages naming the field. A list rather than the first
    /// failure, so a file that is wrong twice does not have to be fixed twice.
    /// </summary>
    private static List<string> Inspect(string pinText)
    {
        Pin pin = ReadPin(pinText);
        List<string> violations = [];

        if (!pin.HasSdkSection)
        {
            // Everything below reads a field of that section. Reporting each of them absent
            // would be four messages for one problem.
            violations.Add(
                $"{PinFileName} has no \"sdk\" section, so it pins nothing and every machine "
                + "resolves whichever SDK it happens to have.");

            return violations;
        }

        if (pin.Version is null || !ExactVersionPattern().IsMatch(pin.Version))
        {
            violations.Add(
                $"{PinFileName} names \"{pin.Version ?? "nothing"}\", which is not an exact SDK "
                + "version. A roll-forward policy is relative to the version it starts from, so "
                + "one that names no feature band cannot be held inside one.");
        }

        if (pin.RollForward is null)
        {
            violations.Add(
                $"{PinFileName} leaves \"rollForward\" unstated, so how far a machine may roll "
                + "is decided by the SDK's own default rather than by this repository — held by "
                + "something that can move without anyone here committing anything, and that "
                + "the next person to read the file cannot tell apart from a decision.");
        }
        else if (!BandBoundPolicies.Contains(pin.RollForward, StringComparer.Ordinal))
        {
            violations.Add(
                $"{PinFileName} rolls forward on \"{pin.RollForward}\", which can resolve "
                + $"outside the feature band the version names. Use one of "
                + $"{string.Join(", ", BandBoundPolicies)}, or the SDK that builds a commit is "
                + "whichever band a machine happens to hold.");
        }

        if (pin.AllowPrerelease is not false)
        {
            violations.Add(
                $"{PinFileName} does not set \"allowPrerelease\" to false, so a preview SDK can "
                + "answer the pin. A preview bundles analyzers no released band does, which is "
                + "a third answer to the question this file exists to give one answer to.");
        }

        return violations;
    }

    /// <summary>
    /// The feature band a version belongs to, spelled the way the SDK's own documentation
    /// spells one: in the third component, the leading digit is the band and the two after it
    /// are the patch level within it. <c>10.0.400</c> is a version, <c>10.0.4xx</c> is the
    /// band — and the band is what a contributor has to install, since the exact patch level
    /// they end up with is not something the page can promise.
    /// </summary>
    private static string FeatureBand(string version)
    {
        if (!ExactVersionPattern().IsMatch(version))
        {
            throw new FormatException(
                $"'{version}' is not an exact SDK version, so it names no feature band.");
        }

        string[] parts = version.Split('.');

        return $"{parts[0]}.{parts[1]}.{parts[2][0]}xx";
    }

    /// <summary>
    /// An exact SDK version: three components, the third of which is the three-digit
    /// band-and-patch the SDK numbers a release with. Anchored, so a prerelease label is not
    /// an exact version — <c>allowPrerelease</c> keeps a preview from being resolved and has
    /// nothing to say about one being named.
    /// </summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d{3}$")]
    private static partial Regex ExactVersionPattern();

    /// <summary>A step that installs an SDK.</summary>
    [GeneratedRegex(@"uses:\s*actions/setup-dotnet(?=[@\s])")]
    private static partial Regex SetupStepPattern();

    /// <summary>
    /// A step input that takes the version from the pin. The file name is spelled here as well
    /// as in <c>PinFileName</c> because a compile-time pattern cannot read a constant; the two
    /// are checked against each other by the rules above, which count matches of this against
    /// a file read by that name. A leading <c>./</c> is accepted as the same path.
    /// </summary>
    [GeneratedRegex(@"(?m)^\s*global-json-file:\s*(?:\./)?global\.json\s*$")]
    private static partial Regex PinFileInputPattern();

    /// <summary>A step input that decides the SDK version somewhere other than the pin.</summary>
    [GeneratedRegex(@"(?m)^\s*dotnet-version:")]
    private static partial Regex OwnVersionPattern();

    /// <summary>
    /// What the SDK answers when asked its version from the repository root, and how the
    /// invocation ended. The working directory is the whole point: an SDK looks for
    /// <c>global.json</c> from there upwards, so asking from anywhere else answers a question
    /// about the machine rather than about this repository — the same trap that fails a build
    /// started outside the checkout with errors in files nobody touched.
    /// </summary>
    private static (int ExitCode, string Output) AskTheMuxerItsVersion()
    {
        ProcessStartInfo start = new()
        {
            FileName = Toolchain.DotnetMuxer(),
            WorkingDirectory = RepositoryLayout.Root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("--version");

        // So the answer is the version and nothing else. Both are set in the workflows for the
        // same reason; here a banner would arrive as a version that matches no band.
        start.Environment["DOTNET_NOLOGO"] = "true";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "true";

        using Process muxer = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the SDK muxer.");

        // Drain both pipes before waiting, for the reason the other fixtures that run a
        // process do: one filled while nobody reads it blocks for ever rather than failing.
        Task<string> output = muxer.StandardOutput.ReadToEndAsync();
        Task<string> error = muxer.StandardError.ReadToEndAsync();
        muxer.WaitForExit();

        return (muxer.ExitCode, (output.Result + error.Result).Trim());
    }

    /// <summary>
    /// A feature band as a page spells one: an exact version with the two patch digits written
    /// <c>xx</c>, which is the form the SDK's own documentation uses and the form nothing else
    /// in a page is numbered with — a package version is never written this way, so a match
    /// here is always somebody telling a reader which SDK to install.
    /// </summary>
    [GeneratedRegex(@"\d+\.\d+\.\dxx")]
    private static partial Regex BandPattern();
}
