using System.Globalization;
using System.Runtime.ExceptionServices;
using Rendlio.Interop.Sweep;
using Rendlio.Interop.Sweep.Sources;
using Xunit;

namespace Rendlio.Interop.Sweep.Tests;

/// <summary>
/// Pins the run against the machine it runs on. Every value this tool compares across weeks is
/// text it formatted or parsed itself — an identity, a run stamp, a timestamp, a count — and
/// each of those is a place where .NET reaches for the ambient culture unless it is told not
/// to. A watch that read differently on a differently configured machine would report a field
/// of changes that never happened, quietly, and for as long as nobody compared two machines.
/// </summary>
/// <remarks>
/// The three locales are chosen for what each one breaks: <c>tr-TR</c> lower-cases I to a
/// different letter than an invariant lower-casing does, <c>de-DE</c> groups and separates
/// numbers the other way round, and <c>ar-SA</c> dates by another calendar and refuses some
/// formats outright.
/// </remarks>
public sealed class LocaleTests
{
    private const string Turkish = "tr-TR";
    private const string German = "de-DE";
    private const string Arabic = "ar-SA";

    [Theory]
    [InlineData(Turkish)]
    [InlineData(German)]
    [InlineData(Arabic)]
    public void The_locales_this_fixture_uses_really_do_differ_from_invariant(string locale)
    {
        // This one is about the fixture rather than the tool. Globalization can be switched off
        // for a whole app, and where it is, every CultureInfo collapses to invariant and the
        // cases below would pass by testing nothing at all. So the premise is asserted rather
        // than assumed: if this fails, the ones after it have stopped meaning anything.
        In(locale, () =>
        {
            Assert.NotEqual(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
            Assert.NotEqual(
                1234.5.ToString("N1", CultureInfo.InvariantCulture),
                1234.5.ToString("N1", CultureInfo.CurrentCulture));
        });
    }

    [Theory]
    [InlineData(Turkish)]
    [InlineData(German)]
    [InlineData(Arabic)]
    public void An_identity_reads_the_same_in_every_locale(string locale)
    {
        // The identity is what a diff joins two runs on, and it is lower-cased on the way in. A
        // Turkish lower-casing turns the I in this name into a dotless one, so the same package
        // would carry two identities on two machines — and each run would then report the other
        // machine's candidates as entrants and its own as gone.
        In(locale, () =>
            Assert.Equal("nuget:miniexcelio", Observation.Identify(SweepSource.NuGet, "MiniExcelIO")));
    }

    [Theory]
    [InlineData(Turkish)]
    [InlineData(German)]
    [InlineData(Arabic)]
    public void A_run_identifier_reads_the_same_in_every_locale(string locale)
    {
        // The default run stamp is a formatted date, and it is what tells one append from the
        // next in the ledger. Formatted under a Hijri calendar it would carry a different year
        // entirely, so two machines writing one file would disagree about the order the runs
        // happened in.
        In(locale, () =>
            Assert.Equal(
                "20260827T060504Z",
                SweepOptions.Stamp(new DateTimeOffset(2026, 8, 27, 6, 5, 4, TimeSpan.Zero))));
    }

    [Theory]
    [InlineData(Turkish)]
    [InlineData(German)]
    [InlineData(Arabic)]
    public void A_report_reads_the_same_in_every_locale(string locale)
    {
        // Counts and timestamps are the two things a change line carries. Grouped the German
        // way a download count reads as a decimal, and the report is the artefact an operator
        // keeps — two weeks of it should be comparable line for line.
        Observation before = Sighting.Of("alpha", downloads: 1_234_567);
        Observation after = before with
        {
            Downloads = 2_000_000,
            Updated = new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero),
        };

        In(locale, () =>
            Assert.Equal(
                "changed (2):" + Environment.NewLine
                + "  crates.io:alpha  downloads: 1234567 -> 2000000" + Environment.NewLine
                + "  crates.io:alpha  updated: - -> 2026-08-27T06:00:00.0000000Z" + Environment.NewLine,
                SweepDiff.Between([before], [after]).Report()));
    }

    [Theory]
    [InlineData(Turkish)]
    [InlineData(German)]
    [InlineData(Arabic)]
    public void A_registry_timestamp_reads_as_the_same_instant_in_every_locale(string locale)
    {
        // The one culture-sensitive read rather than write, and the payload is deliberate. None
        // of these five registries publishes a slash-dated timestamp — they all send ISO 8601,
        // which every culture reads identically, so a fixture built on one could not tell a
        // fixed parse culture from an ambient one and would pass however this was written. A
        // slash date is the input that can: read as the machine's own, this one is 3 April in
        // Germany and Turkey and does not parse at all in Saudi Arabia, against 4 March
        // everywhere for a run that fixes its own culture. Since updated is a compared field,
        // an ambient culture would have an unchanged project moving every week on any registry
        // that ever sent a format like this one.
        In(locale, async () =>
        {
            IReadOnlyList<Observation> observed = await new GitHubSource(
                new RecordingTransport(
                    """{"items":[{"full_name":"a/b","pushed_at":"03/04/2026 06:00:00"}]}"""))
                .CollectAsync(new SweepQuery("q", SweepSource.GitHub, "term"), CancellationToken.None);

            Assert.Equal(
                new DateTimeOffset(2026, 3, 4, 6, 0, 0, TimeSpan.Zero),
                Assert.Single(observed).Updated);
        });
    }

    /// <summary>
    /// Runs <paramref name="body"/> as a machine configured for <paramref name="locale"/> would.
    /// </summary>
    /// <remarks>
    /// On a thread of its own rather than by setting and restoring the culture around the body.
    /// The culture is a property of a thread, xUnit hands its threads to the next fixture when
    /// this one is done with them, and a body that awaits may resume on a different thread than
    /// it started on — so a restore in a <c>finally</c> can put the culture back on the wrong
    /// thread and leave this locale behind on the right one. A thread that exists only for the
    /// duration of the body cannot leak a culture anywhere, because it is gone.
    /// </remarks>
    private static void In(string locale, Action body)
    {
        Exception? failure = null;

        Thread thread = new(() =>
        {
            CultureInfo.CurrentCulture = new CultureInfo(locale);

            try
            {
                body();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            // Rethrown rather than wrapped, so the assertion the body failed on is the one
            // xUnit reports.
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    /// <summary>
    /// The same, for a body that awaits. Blocking is safe and deliberate here: the thread is
    /// this method's own and carries no synchronization context to deadlock against, and
    /// waiting on it is what keeps the culture in place until the body is finished with it.
    /// </summary>
    private static void In(string locale, Func<Task> body) =>
        In(locale, () => body().GetAwaiter().GetResult());
}
