using System.Net;
using System.Net.Sockets;
using System.Text;
namespace MAGNOS.Server;

// Answers "MAGNOS?" broadcasts on UDP 5001 so clients can find the server without typing its IP.
public class DiscoveryService : BackgroundService
{
    readonly int _port;
    public DiscoveryService(IConfiguration cfg)
    {
        var first = (cfg["Urls"] ?? "http://0.0.0.0:5000").Split(';')[0].TrimEnd('/');
        _port = int.TryParse(first[(first.LastIndexOf(':') + 1)..], out var p) ? p : 5000;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        UdpClient udp;
        try { udp = new UdpClient(new IPEndPoint(IPAddress.Any, 5001)); }
        catch (Exception ex) { Console.Error.WriteLine("Discovery disabled: " + ex.Message); return; }
        using (udp)
        {
            var reply = Encoding.ASCII.GetBytes("MAGNOS|" + _port);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var r = await udp.ReceiveAsync(ct);
                    if (Encoding.ASCII.GetString(r.Buffer).StartsWith("MAGNOS?"))
                        await udp.SendAsync(reply, reply.Length, r.RemoteEndPoint);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); await Task.Delay(1000, ct); }
            }
        }
    }
}

