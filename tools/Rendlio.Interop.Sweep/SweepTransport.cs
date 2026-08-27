using System.Net;
using System.Net.Http.Headers;

namespace Rendlio.Interop.Sweep;

/// <summary>
/// How a run reaches a registry. The interface has one method and that method is a GET, which
/// is the point of it being an interface at all: a collector is an observer, and there is no
/// way to write one against this that posts, edits, or opens anything. Anything a run learns,
/// it learned by reading a page that was already public.
/// </summary>
public interface ISweepTransport
{
    /// <summary>Reads <paramref name="uri"/>.</summary>
    /// <param name="uri">The document to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The body, or null when the registry says there is nothing there.</returns>
    /// <exception cref="SweepException">The registry answered with a failure.</exception>
    Task<string?> GetAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>
/// The transport a real run uses. It sends a GET and nothing else, and it attaches the GitHub
/// credential — when there is one — to GitHub alone, so a token cannot travel to a host that
/// merely appeared in a redirect or a mistyped endpoint.
/// </summary>
public sealed class HttpSweepTransport : ISweepTransport
{
    private const string GitHubApiHost = "api.github.com";

    private readonly HttpClient client;
    private readonly string? gitHubToken;

    /// <summary>Creates the transport.</summary>
    /// <param name="client">The client to send on. Its lifetime stays with the caller.</param>
    /// <param name="gitHubToken">
    /// An optional GitHub token. Unauthenticated GitHub search allows only a few requests a
    /// minute, which a weekly recipe of any size will exceed; a token raises that and changes
    /// nothing else. Read from the environment rather than an argument, so it stays off the
    /// command line and out of a shell history.
    /// </param>
    public HttpSweepTransport(HttpClient client, string? gitHubToken = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        this.client = client;
        this.gitHubToken = string.IsNullOrWhiteSpace(gitHubToken) ? null : gitHubToken;
    }

    /// <summary>
    /// The identity a run presents. Every registry here asks for a real one, and two of them
    /// refuse the request without it.
    /// </summary>
    public static ProductInfoHeaderValue UserAgent { get; } = new("Rendlio.Interop.Sweep", "1.0");

    /// <inheritdoc />
    public async Task<string?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (gitHubToken is not null && uri.Host.Equals(GitHubApiHost, StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gitHubToken);
        }

        using HttpResponseMessage response = await SendAsync(request, uri, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SweepException(
                $"{uri.Host} answered {(int)response.StatusCode} {response.ReasonPhrase} for "
                + $"'{uri.AbsolutePath}'.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the request, turning the ways a network fails into the one failure a run reports.
    /// </summary>
    /// <remarks>
    /// This runs unattended and weekly, and the week it cannot reach a registry is a normal
    /// week. What the operator should find is one line naming the registry, not a stack trace
    /// from four frames inside the socket layer. A cancellation the caller asked for is left
    /// alone: that is not a failure and should not be dressed as one.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await client
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException error)
        {
            throw new SweepException($"{uri.Host} could not be reached: {error.Message}", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SweepException(
                $"{uri.Host} did not answer within {client.Timeout.TotalSeconds} seconds.", error);
        }
    }
}
