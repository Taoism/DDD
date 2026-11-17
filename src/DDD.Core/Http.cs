using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace DDD.Core;

/// <summary>The one HttpClient DDD uses: honest user agent, decompression, IPv4 first.</summary>
public static class Http
{
    public static string AppVersion { get; } =
        typeof(Http).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static string UserAgent { get; } = $"DDD/{AppVersion} (driver update checker)";

    public static HttpClient Shared { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            MaxConnectionsPerServer = 6,
            ConnectCallback = ConnectAsync,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// Connects over IPv4 when the host has an IPv4 address. Some vendor CDNs answer the default
    /// lookup with IPv6 addresses only, which fails outright on PCs where IPv6 is turned off.
    /// </summary>
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        string host = context.DnsEndPoint.Host;
        int port = context.DnsEndPoint.Port;
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                addresses = [];
            }
            if (addresses.Length == 0)
                addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        }

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    public static Task<string> GetStringAsync(string url, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), r => r.Content.ReadAsStringAsync(ct), ct);

    public static Task<byte[]> GetBytesAsync(string url, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), r => r.Content.ReadAsByteArrayAsync(ct), ct);

    public static Task<string> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        var pairs = form.ToList();
        return SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(pairs) },
            r => r.Content.ReadAsStringAsync(ct), ct);
    }

    /// <summary>True when the URL answers 2xx to a HEAD request.</summary>
    public static async Task<bool> ExistsAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Shared.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Sends with one retry on a 5xx answer, a timeout or a dropped connection.</summary>
    private static async Task<T> SendAsync<T>(Func<HttpRequestMessage> build, Func<HttpResponseMessage, Task<T>> read, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = build();
                using var response = await Shared.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if ((int)response.StatusCode >= 500 && attempt == 1)
                {
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return await read(response).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt == 1 && !ct.IsCancellationRequested
                && (ex is TaskCanceledException || (ex is HttpRequestException h && h.StatusCode == null)))
            {
                await Task.Delay(1500, ct).ConfigureAwait(false);
            }
        }
    }
}
