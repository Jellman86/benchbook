using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Hortiplanner.Api.Printing;

/// <summary>Sends a print job to a printer's raw port, as every network label printer accepts.</summary>
internal sealed class PrinterConnection(IConfiguration configuration)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public async Task<PrintOutcome> SendAsync(string address, int port, byte[] job, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Printing:AllowLocal") && !IsPrinterPort(port))
        {
            return PrintOutcome.NotAllowed;
        }
        IPAddress[] found;

        try
        {
            found = IPAddress.TryParse(address, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(address, cancellationToken);
        }
        catch (SocketException)
        {
            return PrintOutcome.NotFound;
        }

        // A printer is on the nursery's network, never the server itself or the cloud's own addresses.
        var allowLocal = configuration.GetValue<bool>("Printing:AllowLocal");
        var usable = found.Where(ip => allowLocal || IsPrinterAddress(ip)).ToList();

        if (usable.Count == 0)
        {
            return found.Length == 0 ? PrintOutcome.NotFound : PrintOutcome.NotAllowed;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync([.. usable], port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(job, timeout.Token);
            await stream.FlushAsync(timeout.Token);
            client.Client.Shutdown(SocketShutdown.Send);
            return PrintOutcome.Sent;
        }
        catch (Exception error) when (error is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return PrintOutcome.Unreachable;
        }
    }

    internal static bool IsPrinterPort(int port) => port is >= 9100 and <= 9102 or 515 or 631;

    internal static bool IsPrinterAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var local = NetworkInterface.GetAllNetworkInterfaces().SelectMany(network => network.GetIPProperties().UnicastAddresses).Select(address => address.Address);
        if (local.Any(address => address.Equals(ip)) || IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily != AddressFamily.InterNetwork
            || !(bytes[0] == 169 && bytes[1] == 254) && bytes[0] < 224 && bytes[0] != 0;
    }
}
