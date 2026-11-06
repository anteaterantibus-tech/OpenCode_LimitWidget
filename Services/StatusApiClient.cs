using System.Net.Http;
using System.Net.Http.Json;
using OpenCodeWidget.Models;

namespace OpenCodeWidget.Services;

public sealed class StatusApiClient
{
    public const string StatusUrl = "https://status.opencode.ai/api/status";

    private static readonly HttpClient Http = HttpClientFactory.Shared;

    public async Task<ServiceStatusResponse?> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http
                .GetAsync(StatusUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content
                .ReadFromJsonAsync<ServiceStatusResponse>(GoJson.Options, cancellationToken)
                .ConfigureAwait(false);
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
}
