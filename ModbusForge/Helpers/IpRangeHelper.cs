using System;
using System.Collections.Generic;
using System.Net;
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
    }
}
