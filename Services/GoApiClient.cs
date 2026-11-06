using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCodeWidget.Models;

namespace OpenCodeWidget.Services;

public enum GoApiErrorKind
{
    Unauthorized,
    RateLimited,
    Server,
    Network,
}

public sealed class GoApiException : Exception
{
    public GoApiErrorKind Kind { get; }

    public GoApiException(GoApiErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

public sealed class GoApiClient
{
    public const string BaseUrl = "https://opencode.ai/console/api";

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = GoJson.Options;

    private readonly string _apiKey;

    public GoApiClient(string apiKey)
    {
        _apiKey = apiKey.Trim();
    }

    public bool HasKey => _apiKey.Length > 0;

    public async Task<GoStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        return await GetAsync<GoStatusResponse>("/go/status", cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoActivityResponse?> GetActivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync<GoActivityResponse>("/activity/go", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (_apiKey.Length == 0)
            throw new GoApiException(GoApiErrorKind.Unauthorized, "No API key configured");

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", "OpenCodeWidget/0.1");

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new GoApiException(GoApiErrorKind.Network, "Network request failed", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new GoApiException(GoApiErrorKind.Unauthorized, "API key rejected");

            if ((int)response.StatusCode == 429)
                throw new GoApiException(GoApiErrorKind.RateLimited, "Rate limited");

            if (!response.IsSuccessStatusCode)
                throw new GoApiException(GoApiErrorKind.Server, $"HTTP {(int)response.StatusCode}");

            try
            {
                var result = await response.Content
                    .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                return result ?? throw new GoApiException(GoApiErrorKind.Server, "Empty response");
            }
            catch (JsonException ex)
            {
                throw new GoApiException(GoApiErrorKind.Server, "Invalid response", ex);
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
    }
}
