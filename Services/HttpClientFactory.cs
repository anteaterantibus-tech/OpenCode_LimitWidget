using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace OpenCodeWidget.Services;

internal static class HttpClientFactory
{
    private static readonly Lazy<HttpClient> LazyClient = new(Create);

    public static HttpClient Shared => LazyClient.Value;

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = ConnectPreferIpv4Async,
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
    }

    // Some networks accept IPv6 routes but silently drop traffic to opencode.ai,
    // which makes .NET hang until the request timeout. Try IPv4 first, then IPv6.
    private static async ValueTask<Stream> ConnectPreferIpv4Async(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }

        var ordered = addresses
            .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToArray();

        if (ordered.Length == 0)
            throw new HttpRequestException($"No usable address for {host}");

        Exception? last = null;
        foreach (var address in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };

            try
            {
                await socket.ConnectAsync(
                        new IPEndPoint(address, context.DnsEndPoint.Port),
                        cancellationToken)
                    .ConfigureAwait(false);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                last = ex;
                socket.Dispose();

                if (ex is OperationCanceledException)
                    throw;
            }
        }

        throw new HttpRequestException($"Could not connect to {host}", last);
    }
}
