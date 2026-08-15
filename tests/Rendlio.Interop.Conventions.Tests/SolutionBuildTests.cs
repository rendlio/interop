using System.Diagnostics;
using Xunit;

namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// Asks MSBuild what it does with this solution when the shell that started the build has
/// already made up its mind about the platform. <see cref="BuildContractTests"/> reads
/// <c>Directory.Solution.props</c> and checks that it assigns one; that is the shape, and the
/// shape cannot answer this question, because an assignment guarded by
/// <c>Condition=" '$(Platform)' == '' "</c> reads as exactly the same intent and does nothing
/// at all in the only case where it matters.
///
/// The case is ordinary rather than hypothetical: <c>vcvarsall.bat x64</c> exports
/// <c>Platform</c>, and so does anything that inherits from a shell where it has been run.
/// MSBuild then demands a solution configuration for what it found, this solution declares
/// only the defaults, and restore, build, test, pack and format all stop before a project is
/// loaded — on a checkout with nothing wrong with it.
/// </summary>
public sealed class SolutionBuildTests
{
    private const string SolutionFileName = "Rendlio.Interop.slnx";

    /// <summary>What a shell that has run <c>vcvarsall.bat</c> leaves in the environment.</summary>
    private const string InheritedPlatform = "x64";

    /// <summary>MSBuild refusing a solution configuration the solution does not declare.</summary>
    private const string NoSuchSolutionConfiguration = "MSB4126";

    /// <summary>
    /// MSBuild refusing to run a target no project defines. Getting this far means the
    /// solution configuration resolved and the projects were loaded, which is the whole
    /// question here.
    /// </summary>
    private const string NoSuchTarget = "MSB4057";

    /// <summary>
    /// A target nothing defines. Whether MSBuild gets as far as looking for one is the
    /// question; actually building the solution from inside a test of that solution would be
    /// a second build writing to the output the first one is running from.
    /// </summary>
    private const string ProbeTarget = "RendlioSolutionConfigurationProbe";

    [Fact]
    public void A_solution_build_does_not_inherit_the_platform_from_the_shell()
    {
        string output = Probe(ambientPlatform: InheritedPlatform, requestedPlatform: null);

        Assert.DoesNotContain(NoSuchSolutionConfiguration, output, StringComparison.Ordinal);

        // Guards the assertion above, which an invocation that failed for some earlier reason
        // — no muxer, an unreadable solution — would satisfy while proving nothing. This
        // error can only be reported once the projects have been loaded.
        Assert.Contains(NoSuchTarget, output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_platform_asked_for_on_the_command_line_is_still_refused()
    {
        // The other half, and the reason the test above can be believed: the same probe run
        // against a platform this solution genuinely does not declare does fail, so the first
        // test is reporting a fixed problem rather than a probe that cannot see one.
        //
        // It is also the boundary worth keeping. A property set in a file outranks the
        // environment and loses to the command line, so an inherited platform is overruled
        // while an explicit -p:Platform=x64 gets the honest refusal it asked for.
        string output = Probe(ambientPlatform: null, requestedPlatform: InheritedPlatform);

        Assert.Contains(NoSuchSolutionConfiguration, output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the probe target against the solution and returns everything MSBuild said, with
    /// <paramref name="ambientPlatform"/> exported to the build and
    /// <paramref name="requestedPlatform"/> passed on the command line. Either may be null;
    /// the environment is cleared of <c>Platform</c> when it is, so that whatever shell is
    /// running the suite cannot decide the answer.
    /// </summary>
    private static string Probe(string? ambientPlatform, string? requestedPlatform)
    {
        ProcessStartInfo start = new()
        {
            FileName = Toolchain.DotnetMuxer(),
            WorkingDirectory = RepositoryLayout.Root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(Path.Combine(RepositoryLayout.Root.FullName, SolutionFileName));
        start.ArgumentList.Add($"-t:{ProbeTarget}");
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-v:q");

        // Nothing is built here, so a worker node left running afterwards would only be a
        // handle held on a directory somebody else has to be able to delete.
        start.ArgumentList.Add("-nodeReuse:false");

        if (requestedPlatform is not null)
        {
            start.ArgumentList.Add($"-p:Platform={requestedPlatform}");
        }

        start.Environment.Remove("Platform");
        if (ambientPlatform is not null)
        {
            start.Environment["Platform"] = ambientPlatform;
        }

        using Process msbuild = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the SDK muxer.");

        // Drain both pipes before waiting: a run that filled one while nobody read it would
        // block for ever rather than fail.
        Task<string> output = msbuild.StandardOutput.ReadToEndAsync();
        Task<string> error = msbuild.StandardError.ReadToEndAsync();
        msbuild.WaitForExit();

        return output.Result + error.Result;
    }
}
