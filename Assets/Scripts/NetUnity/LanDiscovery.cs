using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Craftwar.Sim;
using UnityEngine;

namespace Craftwar.Net.Unity
{
    /// <summary>One line in the game browser.</summary>
    public struct LanGameInfo
    {
        public string HostName;
        public string MapName;
        public byte PlayersPresent;
        public byte PlayersMax;
        public ushort Port;
        public uint MapHash;
        public ushort ProtocolVersion;
        public string Address;      // filled in from the packet's sender
        public float LastSeenTime;  // Time.realtimeSinceStartup
    }

    /// <summary>
    /// LAN game announcement over raw UDP broadcast.
    ///
    /// Unity Transport cannot broadcast — there is no such API in it — so
    /// discovery is a plain <see cref="UdpClient"/> alongside the UTP socket
    /// that carries the match itself.
    ///
    /// Beacons go to each interface's SUBNET-DIRECTED broadcast address rather
    /// than to 255.255.255.255. Limited broadcast picks a single interface, and
    /// on a development machine that is very often a virtual adapter (Hyper-V,
    /// WSL, VPN) instead of the real network — the announcement then goes
    /// nowhere the other player can hear it.
    /// </summary>
    public sealed class LanDiscovery : IDisposable
    {
        public const ushort BeaconPort = 27016;
        const uint BeaconMagic = 0x4E41_4C43; // "CLAN"

        readonly UdpClient _socket;
        readonly List<IPEndPoint> _broadcastTargets = new List<IPEndPoint>();
        readonly Dictionary<string, LanGameInfo> _seen = new Dictionary<string, LanGameInfo>();
        bool _disposed;

        public LanDiscovery(bool listen)
        {
            _socket = new UdpClient
            {
                EnableBroadcast = true,
            };
            // Two instances on one development box must both be able to bind, or
            // you cannot test host and client without a second machine.
            _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Client.Bind(new IPEndPoint(IPAddress.Any, listen ? BeaconPort : 0));
            _socket.Client.Blocking = false;

            CollectBroadcastTargets();
        }

        void CollectBroadcastTargets()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (IPAddress.IsLoopback(info.Address))
                        continue;
                    var mask = info.IPv4Mask;
                    if (mask == null)
                        continue;

                    byte[] addr = info.Address.GetAddressBytes();
                    byte[] m = mask.GetAddressBytes();
                    var directed = new byte[4];
                    for (int i = 0; i < 4; i++)
                        directed[i] = (byte)(addr[i] | ~m[i]);
                    _broadcastTargets.Add(new IPEndPoint(new IPAddress(directed), BeaconPort));
                }
            }
            // Last resort if nothing usable was found.
            if (_broadcastTargets.Count == 0)
                _broadcastTargets.Add(new IPEndPoint(IPAddress.Broadcast, BeaconPort));
        }

        /// <summary>Host side: shout once. Call about once a second.</summary>
        public void Announce(in LanGameInfo info)
        {
            if (_disposed)
                return;
            var w = new ByteWriter(96);
            w.WriteUInt(BeaconMagic);
            w.WriteUShort(BuildIdentity.CurrentProtocolVersion);
            w.WriteUShort(info.Port);
            w.WriteUInt(info.MapHash);
            w.WriteByte(info.PlayersPresent);
            w.WriteByte(info.PlayersMax);
            NetMessages.WriteString(ref w, info.HostName ?? "");
            NetMessages.WriteString(ref w, info.MapName ?? "");
            byte[] payload = w.ToArray();

            foreach (var target in _broadcastTargets)
            {
                try
                {
                    _socket.Send(payload, payload.Length, target);
                }
                catch (SocketException e)
                {
                    // A downed or firewalled interface must not take the others
                    // with it.
                    Debug.LogWarning($"[craftwar-net] beacon to {target} failed: {e.SocketErrorCode}");
                }
            }
        }

        /// <summary>Client side: drain arrivals into the game list.</summary>
        public void Poll(float now, float forgetAfterSeconds = 5f)
        {
            if (_disposed)
                return;

            while (true)
            {
                byte[] data;
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    if (_socket.Available <= 0)
                        break;
                    data = _socket.Receive(ref sender);
                }
                catch (SocketException)
                {
                    break;
                }

                if (!TryParse(data, sender, now, out var info))
                    continue;
                _seen[$"{info.Address}:{info.Port}"] = info;
            }

            // Drop games whose host stopped announcing.
            var stale = new List<string>();
            foreach (var pair in _seen)
                if (now - pair.Value.LastSeenTime > forgetAfterSeconds)
                    stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++)
                _seen.Remove(stale[i]);
        }

        static bool TryParse(byte[] data, IPEndPoint sender, float now, out LanGameInfo info)
        {
            info = default;
            try
            {
                var r = new ByteReader(data);
                if (r.ReadUInt() != BeaconMagic)
                    return false;
                info.ProtocolVersion = r.ReadUShort();
                info.Port = r.ReadUShort();
                info.MapHash = r.ReadUInt();
                info.PlayersPresent = r.ReadByte();
                info.PlayersMax = r.ReadByte();
                info.HostName = NetMessages.ReadString(ref r);
                info.MapName = NetMessages.ReadString(ref r);
                info.Address = sender.Address.ToString();
                info.LastSeenTime = now;
                return true;
            }
            catch (System.IO.EndOfStreamException)
            {
                return false; // truncated or foreign packet
            }
            catch (System.IO.InvalidDataException)
            {
                return false;
            }
        }

        /// <summary>Games heard from recently. Ordered by address so the list does
        /// not reshuffle under the player's cursor.</summary>
        public List<LanGameInfo> Games()
        {
            var result = new List<LanGameInfo>(_seen.Count);
            foreach (var pair in _seen)
                result.Add(pair.Value);
            result.Sort((a, b) => string.CompareOrdinal($"{a.Address}:{a.Port}", $"{b.Address}:{b.Port}"));
            return result;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _socket?.Dispose();
        }
    }
}
