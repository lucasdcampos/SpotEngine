using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Spot.Net;

namespace Spot.Net.Tests;

/// <summary>Shared helpers for the loopback integration tests.</summary>
internal static class NetTest
{
    /// <summary>Pumps the given managers until the condition holds or the timeout elapses.</summary>
    public static bool PumpUntil(Func<bool> condition, TimeSpan timeout, params NetworkManager[] managers)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            foreach (NetworkManager m in managers)
            {
                m.Update(1f / 60f);
            }

            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    /// <summary>Pumps the given managers a fixed number of frames.</summary>
    public static void Pump(int frames, params NetworkManager[] managers)
    {
        for (int i = 0; i < frames; i++)
        {
            foreach (NetworkManager m in managers)
            {
                m.Update(1f / 60f);
            }

            Thread.Sleep(5);
        }
    }

    /// <summary>Finds a free loopback TCP port.</summary>
    public static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
