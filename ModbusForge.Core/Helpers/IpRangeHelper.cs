using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ModbusForge.Helpers
{
    /// <summary>
    /// Expands inclusive IPv4 ranges into the individual addresses a scan should probe.
    /// </summary>
    public static class IpRangeHelper
    {
        /// <summary>Upper bound on the number of hosts a single scan may target.</summary>
        public const int MaxAddresses = 4096;

        public static bool TryParseIPv4(string? address, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            if (!IPAddress.TryParse(address.Trim(), out var parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            var bytes = parsed.GetAddressBytes();
            value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            return true;
        }

        public static string ToIPv4String(uint value)
        {
            return new IPAddress(new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            }).ToString();
        }

        /// <summary>
        /// Returns every address from <paramref name="startAddress"/> to <paramref name="endAddress"/> inclusive.
        /// </summary>
        /// <exception cref="ArgumentException">The addresses are not valid IPv4, are reversed, or span more than <see cref="MaxAddresses"/> hosts.</exception>
        public static IReadOnlyList<string> Expand(string startAddress, string endAddress)
        {
            if (!TryParseIPv4(startAddress, out var start))
            {
                throw new ArgumentException($"'{startAddress}' is not a valid IPv4 address.", nameof(startAddress));
            }

            if (!TryParseIPv4(endAddress, out var end))
            {
                throw new ArgumentException($"'{endAddress}' is not a valid IPv4 address.", nameof(endAddress));
            }

            if (end < start)
            {
                throw new ArgumentException("The end address must not be lower than the start address.", nameof(endAddress));
            }

            var count = end - start + 1;
            if (count > MaxAddresses)
            {
                throw new ArgumentException($"The range covers {count} addresses; at most {MaxAddresses} are allowed.", nameof(endAddress));
            }

            var addresses = new List<string>((int)count);
            for (var current = start; ; current++)
            {
                addresses.Add(ToIPv4String(current));
                if (current == end)
                {
                    break;
                }
            }

            return addresses;
        }

        /// <summary>
        /// Returns the primary non-loopback IPv4 address, or <c>null</c> if one cannot be found.
        /// </summary>
        public static string? GetPrimaryLocalIPv4()
        {
            try
            {
                var active = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderBy(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0
                        : ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1
                        : 2)
                    .FirstOrDefault();

                if (active != null)
                {
                    var address = active.GetIPProperties().UnicastAddresses
                        .Select(u => u.Address)
                        .FirstOrDefault(a =>
                            a.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(a) &&
                            !a.ToString().StartsWith("169.254.", StringComparison.OrdinalIgnoreCase));

                    if (address != null)
                    {
                        return address.ToString();
                    }
                }

                var host = Dns.GetHostEntry(Dns.GetHostName());
                return host.AddressList
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    .Where(ip => !IPAddress.IsLoopback(ip))
                    .Where(ip => !ip.ToString().StartsWith("169.254.", StringComparison.OrdinalIgnoreCase))
                    .Select(ip => ip.ToString())
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns all non-loopback, non-link-local IPv4 addresses assigned to local network adapters.
        /// </summary>
        public static IReadOnlyList<string> GetAllLocalIPv4()
        {
            try
            {
                var fromNics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                    .Select(u => u.Address)
                    .Where(a =>
                        a.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(a) &&
                        !a.ToString().StartsWith("169.254.", StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                var fromDns = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    .Where(ip => !IPAddress.IsLoopback(ip))
                    .Where(ip => !ip.ToString().StartsWith("169.254.", StringComparison.OrdinalIgnoreCase))
                    .Select(ip => ip.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                return fromNics.Concat(fromDns).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns a sensible default IPv4 scan range for the local network based on the
        /// primary adapter's subnet, falling back to the primary IP's /24 if the subnet
        /// is too large to scan.
        /// </summary>
        public static (string StartAddress, string EndAddress) GetLocalNetworkRange()
        {
            try
            {
                var adapter = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderBy(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0
                        : ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1
                        : 2)
                    .FirstOrDefault();

                var unicast = adapter?.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(u =>
                        u.Address?.AddressFamily == AddressFamily.InterNetwork &&
                        u.IPv4Mask != null);

                if (unicast?.Address is not null && unicast.IPv4Mask is not null)
                {
                    var ipBytes = unicast.Address.GetAddressBytes();
                    var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                    var network = new byte[4];
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        network[i] = (byte)(ipBytes[i] & maskBytes[i]);
                        broadcast[i] = (byte)(network[i] | ~maskBytes[i]);
                    }

                    var end = ((uint)((broadcast[0] << 24) | (broadcast[1] << 16) | (broadcast[2] << 8) | broadcast[3]) - 1);
                    var start = ((uint)((network[0] << 24) | (network[1] << 16) | (network[2] << 8) | network[3]) + 1);

                    if (end >= start && end - start + 1 <= MaxAddresses)
                    {
                        return (ToIPv4String(start), ToIPv4String(end));
                    }
                }

                var primary = GetPrimaryLocalIPv4();
                if (!string.IsNullOrWhiteSpace(primary) && TryParseIPv4(primary, out var ipValue))
                {
                    var bytes = ToIPv4String(ipValue).Split('.');
                    return ($"{bytes[0]}.{bytes[1]}.{bytes[2]}.1", $"{bytes[0]}.{bytes[1]}.{bytes[2]}.254");
                }
            }
            catch
            {
                // Fall through to loopback fallback.
            }

            return ("127.0.0.1", "127.0.0.1");
        }
    }
}
