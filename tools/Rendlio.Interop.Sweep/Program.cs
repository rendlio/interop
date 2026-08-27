using System.Globalization;
using Rendlio.Interop.Sweep;
using Rendlio.Interop.Sweep.Sources;

// One run of one recipe: read every registry the recipe names, write what was read to the
// append-only ledger, and print what moved since the run before it.
try
{
    SweepOptions options = SweepOptions.Parse(args, DateTimeOffset.UtcNow);
    SweepRecipe recipe = SweepRecipe.Parse(await ReadRecipeAsync(options.RecipePath).ConfigureAwait(false));

    using HttpClient client = new()
    {
        // A registry that has stopped answering should end the run rather than hold a
        // scheduled job open until something else times it out.
        Timeout = TimeSpan.FromSeconds(30),
    };

    // Read from the environment rather than the command line so it stays out of a shell
    // history and out of a process listing.
    HttpSweepTransport transport = new(client, Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

    SweepRunner runner = new(
    [
        new CratesIoSource(transport),
        new NuGetSource(transport),
        new NpmSource(transport),
        new PyPiSource(transport),
        new GitHubSource(transport),
    ]);

    DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
    IReadOnlyList<Observation> observed = await runner.CollectAsync(recipe).ConfigureAwait(false);
    IReadOnlyList<Observation> previous = CandidateLedger.LatestRun(CandidateLedger.Read(options.LedgerPath));

    SweepDiff diff = SweepDiff.Between(previous, observed, recipe.Sensitivity);

    CandidateLedger.Append(options.LedgerPath, CandidateLedger.Stamp(options.Run, observedUtc, observed));

    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{recipe.Name} — run {options.Run} — {observed.Count} observed, {previous.Count} in the run before"));
    Console.WriteLine();
    Console.Write(diff.Report());

    return 0;
}
catch (SweepException error)
{
    Console.Error.WriteLine(error.Message);

    return 1;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException)
{
    // A run happens on a schedule with nobody watching. A file it could not read or write is
    // an ordinary way for that to go, and it should read as one line rather than a stack trace.
    Console.Error.WriteLine(error.Message);

    return 1;
}

static async Task<string> ReadRecipeAsync(string path)
{
    if (!File.Exists(path))
    {
        throw new SweepException($"There is no recipe at {path}. {SweepOptions.Usage}");
    }

    return await File.ReadAllTextAsync(path).ConfigureAwait(false);
}
