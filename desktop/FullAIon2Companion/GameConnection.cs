using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace FullAIon2Companion;

/// <param name="ViaProxy">A gaming VPN's local proxy (ExitLag, NoPing...): the game talks to 127.0.0.1 on another port.</param>
public sealed record GameConnection(int Pid, string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, bool ViaProxy = false)
{
    /// <summary>Every connection to the game server's port: a relog opens a new one, whose first second (inventory, counters)
    /// must not be lost while the app notices it.</summary>
    public string PortFilter => $"tcp and port {RemotePort}";
    public string Key => $"{LocalAddress}:{LocalPort}>{RemoteAddress}:{RemotePort}";
    public string Filter => $"tcp and ((src host {RemoteAddress} and src port {RemotePort} and dst host {LocalAddress} and dst port {LocalPort}) or (src host {LocalAddress} and src port {LocalPort} and dst host {RemoteAddress} and dst port {RemotePort}))";
    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    public static IReadOnlyList<GameConnection> Find()
    {
        var pids = Process.GetProcessesByName("AION2").Select(p => { using(p) return p.Id; }).ToHashSet();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
        if (size <= 0) return [];
        // The table can grow between asking its size and reading it (then the read fails with 122 and the game
        // looked closed for a few seconds at start): read with room to spare, and retry with the new size.
        IntPtr buffer = IntPtr.Zero;
        for (int attempt = 0; ; attempt++)
        {
            size += 4096;
            buffer = Marshal.AllocHGlobal(size);
            if (GetExtendedTcpTable(buffer, ref size, false, 2, 5, 0) == 0) break;
            Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero;
            if (attempt == 3) return [];
        }
        try
        {
            var result = new List<GameConnection>();
            var proxied = new List<GameConnection>();
            int count = Marshal.ReadInt32(buffer);
            for (int i = 0; i < count; i++)
            {
                IntPtr row = buffer + 4 + i * 24;
                int pid = Marshal.ReadInt32(row, 20), state = Marshal.ReadInt32(row);
                int remotePort = Port(Marshal.ReadInt32(row, 16));
                // Only gameplay ports observed in this protocol. Never fall back to capturing all TCP.
                if (!pids.Contains(pid) || state != 5) continue;
                var local = new IPAddress(unchecked((uint)Marshal.ReadInt32(row, 4))).ToString();
                var remote = new IPAddress(unchecked((uint)Marshal.ReadInt32(row, 12))).ToString();
                int localPort = Port(Marshal.ReadInt32(row, 8));
                if (remote.StartsWith("127.", StringComparison.Ordinal))
                {
                    // Gaming VPNs (ExitLag, NoPing, WTFast in proxy mode) redirect the game to a proxy on this PC: its
                    // connection goes to 127.0.0.1 on the proxy's port. Used only when there is no direct game connection.
                    proxied.Add(new(pid, local, localPort, remote, remotePort, ViaProxy: true));
                    continue;
                }
                if (remotePort is < 13300 or > 13400) continue;
                result.Add(new(pid, local, localPort, remote, remotePort));
            }
            if (result.Count > 0) return result;
            // The game also talks to itself on the loopback (2026-10-13: 127.0.0.1:19006 <-> 19007, both ends AION2): a proxy
            // is a loopback connection whose other end belongs to another program.
            var gameOwned = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                IntPtr row = buffer + 4 + i * 24;
                if (pids.Contains(Marshal.ReadInt32(row, 20))) gameOwned.Add(Port(Marshal.ReadInt32(row, 8)));
            }
            return proxied.Where(c => !gameOwned.Contains(c.RemotePort)).ToList();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    static int Port(int value) => ((value & 255) << 8) | ((value >> 8) & 255);
}
