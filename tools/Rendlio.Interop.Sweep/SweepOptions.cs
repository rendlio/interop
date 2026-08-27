using System.Globalization;

namespace Rendlio.Interop.Sweep;

/// <summary>What one invocation was told to do.</summary>
/// <param name="RecipePath">The recipe to run.</param>
/// <param name="LedgerPath">The append-only file to read the previous run from and add to.</param>
/// <param name="Run">
/// The run identifier stamped onto every record. Supplied rather than generated when a caller
/// needs two invocations to be comparable — a replay of a recorded run, or a re-run of a job
/// that failed partway.
/// </param>
public sealed record SweepOptions(string RecipePath, string LedgerPath, string Run)
{
    /// <summary>How to invoke the tool.</summary>
    public const string Usage =
        "usage: Rendlio.Interop.Sweep --recipe <path> --ledger <path> [--run <id>]";

    /// <summary>
    /// Reads the command line.
    /// </summary>
    /// <param name="arguments">The arguments, without the executable.</param>
    /// <param name="utcNow">Now, for the default run identifier.</param>
    /// <returns>The options.</returns>
    /// <exception cref="SweepException">The command line is not usable.</exception>
    public static SweepOptions Parse(IReadOnlyList<string> arguments, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? recipe = null;
        string? ledger = null;
        string? run = null;

        for (int index = 0; index < arguments.Count; index += 2)
        {
            string name = arguments[index];

            if (index + 1 >= arguments.Count)
            {
                throw new SweepException($"{name} was given no value. {Usage}");
            }

            string value = arguments[index + 1];

            switch (name)
            {
                case "--recipe":
                    recipe = value;
                    break;
                case "--ledger":
                    ledger = value;
                    break;
                case "--run":
                    run = value;
                    break;
                default:
                    throw new SweepException($"{name} is not an option. {Usage}");
            }
        }

        if (string.IsNullOrWhiteSpace(recipe))
        {
            // Named rather than defaulted on purpose. The recipe says what a run watches, and
            // that is not this repository to hold: a run is handed one.
            throw new SweepException($"No recipe was given. {Usage}");
        }

        if (string.IsNullOrWhiteSpace(ledger))
        {
            throw new SweepException($"No ledger was given. {Usage}");
        }

        return new SweepOptions(
            recipe,
            ledger,
            string.IsNullOrWhiteSpace(run) ? Stamp(utcNow) : run);
    }

    /// <summary>A run identifier that sorts and reads as the moment the run started.</summary>
    public static string Stamp(DateTimeOffset utcNow) =>
        utcNow.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}
