using System.Net;
using System.Net.Sockets;

namespace TeachWell.Infrastructure.Canvas;

public sealed class CanvasHostPolicy(CanvasIntegrationOptions options)
{
    public Uri? ParseOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.HostNameType != UriHostNameType.Dns || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !options.AllowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)) return null;
        return new Uri($"https://{uri.IdnHost.ToLowerInvariant()}/");
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 100 && b[1] is >= 64 and <= 127 ||
                b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 ||
                b[0] == 192 && (b[1] == 168 || b[1] == 0 && b[2] is 0 or 2 || b[1] == 88 && b[2] == 99) ||
                b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100) || b[0] == 203 && b[1] == 0 && b[2] == 113);
        }
        // Only globally routable unicast; exclude transition, protocol and documentation ranges.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0) &&
            !(b[0] == 0x20 && b[1] == 0x02) &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8));
    }

    public SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(8),
        ConnectCallback = async (context, ct) =>
        {
            var endpoint = context.DnsEndPoint;
            if (endpoint.Port != 443 || !options.AllowedHosts.Contains(endpoint.Host, StringComparer.OrdinalIgnoreCase))
                throw new HttpRequestException("Canvas destination is not approved.");
            var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a))) throw new HttpRequestException("Canvas destination is not publicly routable.");
            // Connect to the checked IP, avoiding a second DNS lookup/rebinding window.
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, 443), ct); return new NetworkStream(socket, ownsSocket: true); }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("Canvas destination could not be reached.");
        }
    };
}
