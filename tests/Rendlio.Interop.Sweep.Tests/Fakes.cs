using System.Net;
using Rendlio.Interop.Sweep;

namespace Rendlio.Interop.Sweep.Tests;

/// <summary>
/// A transport that answers from a script instead of the network, and remembers what it was
/// asked for. The suite never reaches a registry: a fixture that did would be measuring
/// somebody else uptime and would report a rate limit as a defect in this repository.
/// </summary>
internal sealed class RecordingTransport(params string?[] responses) : ISweepTransport
{
    private int served;

    /// <summary>Every address the code under test asked for, in order.</summary>
    public List<Uri> Requested { get; } = [];

    public Task<string?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        Requested.Add(uri);

        string? response = responses.Length == 0
            ? null
            : responses[Math.Min(served, responses.Length - 1)];

        served++;

        return Task.FromResult(response);
    }
}

/// <summary>A transport that fails the test if anything asks it for anything.</summary>
internal sealed class UnusedTransport : ISweepTransport
{
    public Task<string?> GetAsync(Uri uri, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"The run reached {uri} when it should not have.");
}

/// <summary>Answers HTTP without a socket, and keeps the request it was handed.</summary>
internal sealed class StubHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    : HttpMessageHandler
{
    public HttpRequestMessage? Sent { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent = request;

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        });
    }
}

/// <summary>Fails the way a network does, without one.</summary>
internal sealed class ThrowingHandler(Exception failure) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) => throw failure;
}

/// <summary>Builds observations for the diff and ledger fixtures.</summary>
internal static class Sighting
{
    public static Observation Of(
        string name,
        SweepSource source = SweepSource.CratesIo,
        string? version = "1.0.0",
        long? downloads = null,
        long? stars = null,
        IReadOnlyList<string>? claims = null) => new()
        {
            Id = Observation.Identify(source, name),
            Source = source,
            Name = name,
            Url = "https://example.invalid/" + name,
            Version = version,
            Description = name + " does something",
            Downloads = downloads,
            Stars = stars,
            Queries = ["a-query"],
            Claims = claims ?? [],
        };
}
