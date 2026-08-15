namespace Rendlio.Interop.Conventions.Tests;

/// <summary>
/// How a fixture that has to run the SDK finds it, so the two that do agree on one answer.
/// </summary>
internal static class Toolchain
{
    /// <summary>
    /// The muxer running these tests, so a build started from here resolves the SDK that
    /// <c>global.json</c> pins rather than whichever one happens to come first on PATH. The
    /// difference is not academic: a newer SDK picked up by accident fails this codebase with
    /// errors in files nobody touched.
    /// </summary>
    public static string DotnetMuxer()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }
}
