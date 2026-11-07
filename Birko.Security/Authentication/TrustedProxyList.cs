using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Birko.Security.Authentication
{
    /// <summary>
    /// A parsed list of trusted proxies: addresses (<c>10.0.0.5</c>) and CIDR networks (<c>172.18.0.0/16</c>), with
    /// <c>${ENV_VAR}</c> entries expanded. The one parser behind <see cref="AuthenticationConfiguration.TrustedProxies"/>
    /// and <see cref="TrustedProxySettings.TrustedProxies"/> (TASK-829), so the two cannot read an entry differently.
    /// </summary>
    /// <remarks>
    /// Two modes. <see cref="Parse"/> is strict: an entry that is not an address or a network, a <c>${VAR}</c> naming
    /// a variable that is not set, or an entry that is blank throws <see cref="FormatException"/> naming it. It is what
    /// the forwarded-headers registration uses, at startup. <see cref="ParseLenient"/> skips such an entry and reports
    /// it, which is what <see cref="AuthenticationService"/> has always done for the transport configurations.
    /// </remarks>
    public sealed class TrustedProxyList
    {
        private readonly IReadOnlyList<IPAddress> _addresses;
        private readonly IReadOnlyList<IPNetwork> _networks;

        private TrustedProxyList(IReadOnlyList<IPAddress> addresses, IReadOnlyList<IPNetwork> networks)
        {
            _addresses = addresses;
            _networks = networks;
        }

        /// <summary>A list that trusts nobody.</summary>
        public static TrustedProxyList Empty { get; } = new(Array.Empty<IPAddress>(), Array.Empty<IPNetwork>());

        /// <summary>The proxy addresses, IPv4-mapped ones reduced to IPv4.</summary>
        public IReadOnlyList<IPAddress> Addresses => _addresses;

        /// <summary>The proxy networks.</summary>
        public IReadOnlyList<IPNetwork> Networks => _networks;

        /// <summary>True when nobody is trusted.</summary>
        public bool IsEmpty => _addresses.Count == 0 && _networks.Count == 0;

        /// <summary>
        /// Parses strictly. Throws <see cref="FormatException"/> naming the first entry that is blank, names an unset
        /// <c>${VAR}</c>, or is neither an address nor a CIDR network.
        /// </summary>
        /// <param name="entries">The configured entries; null is an empty list.</param>
        public static TrustedProxyList Parse(IEnumerable<string>? entries)
            => Build(entries, lenient: false, (entry, reason) => throw new FormatException($"Trusted proxy entry '{entry}' {reason}."));

        /// <summary>
        /// Parses leniently: an entry <see cref="Parse"/> would refuse is skipped and passed to
        /// <paramref name="onSkipped"/> with the reason.
        /// </summary>
        public static TrustedProxyList ParseLenient(IEnumerable<string>? entries, Action<string, string>? onSkipped = null)
            => Build(entries, lenient: true, (entry, reason) => onSkipped?.Invoke(entry, reason));

        /// <summary>
        /// Parses in <paramref name="mode"/>: <see cref="Parse(IEnumerable{string}?)"/> for
        /// <see cref="TrustedProxyParsing.Strict"/>, <see cref="ParseLenient"/> (reporting to
        /// <paramref name="onSkipped"/>) for <see cref="TrustedProxyParsing.Lenient"/>.
        /// </summary>
        public static TrustedProxyList Parse(IEnumerable<string>? entries, TrustedProxyParsing mode, Action<string, string>? onSkipped = null)
            => mode == TrustedProxyParsing.Lenient ? ParseLenient(entries, onSkipped) : Parse(entries);

        private static TrustedProxyList Build(IEnumerable<string>? entries, bool lenient, Action<string, string> refuse)
        {
            if (entries == null)
            {
                return Empty;
            }

            var addresses = new List<IPAddress>();
            var networks = new List<IPNetwork>();
            foreach (var configured in entries)
            {
                var expanded = AuthenticationService.ExpandEnvironmentVariable(configured)?.Trim();
                if (string.IsNullOrEmpty(expanded))
                {
                    refuse(configured ?? string.Empty, "is blank");
                    continue;
                }
                if (expanded.StartsWith("${", StringComparison.Ordinal) && expanded.EndsWith("}", StringComparison.Ordinal))
                {
                    refuse(configured, "names an environment variable that is not set");
                    continue;
                }
                if (expanded.Contains('/'))
                {
                    if (!IPNetwork.TryParse(expanded, out var network))
                    {
                        refuse(configured, "is not a CIDR network (address/prefix)");
                    }
                    else if (!lenient && network.PrefixLength == 0)
                    {
                        refuse(configured, "trusts every sender; list the proxy's own address or network");
                    }
                    else if (network.PrefixLength == (network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128))
                    {
                        // One address written as a network: the same entry as the bare address.
                        addresses.Add(Normalize(network.BaseAddress));
                    }
                    else
                    {
                        networks.Add(network);
                    }
                    continue;
                }
                if (!lenient && !IsPlainAddress(expanded))
                {
                    refuse(configured, "is not an IP address");
                    continue;
                }
                if (IPAddress.TryParse(expanded, out var address))
                {
                    addresses.Add(Normalize(address));
                }
                else if (lenient && IPEndPoint.TryParse(expanded, out var endPoint))
                {
                    // AuthenticationService always accepted an address with a port ("10.0.0.5:80") and ignored the
                    // port; the lenient mode keeps that, the strict one refuses it.
                    addresses.Add(Normalize(endPoint.Address));
                }
                else
                {
                    refuse(configured, "is not an IP address");
                }
            }
            return addresses.Count == 0 && networks.Count == 0 ? Empty : new TrustedProxyList(addresses, networks);
        }

        /// <summary>True when <paramref name="address"/> is a listed proxy or inside a listed network.</summary>
        public bool Contains(IPAddress? address)
        {
            if (address == null)
            {
                return false;
            }
            var normalized = Normalize(address);
            foreach (var proxy in _addresses)
            {
                if (proxy.Equals(normalized))
                {
                    return true;
                }
            }
            foreach (var network in _networks)
            {
                if (network.Contains(address) || network.Contains(normalized))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when both lists trust exactly the same addresses and networks, in any order.</summary>
        public bool SameAs(TrustedProxyList other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Canonical().SetEquals(other.Canonical());
        }

        /// <inheritdoc />
        public override string ToString() => IsEmpty ? "(none)" : string.Join(", ", Canonical().OrderBy(s => s, StringComparer.Ordinal));

        private HashSet<string> Canonical()
            => new(_addresses.Select(a => a.ToString()).Concat(_networks.Select(n => n.ToString())), StringComparer.Ordinal);

        /// <summary>
        /// An address written in full, as the strict mode requires: IPv4 as four dotted decimal parts (<c>IPAddress</c>
        /// also reads <c>10.5</c> as 10.0.0.5), IPv6 without brackets (it also reads <c>[::1]:80</c>, dropping the port).
        /// </summary>
        private static bool IsPlainAddress(string text)
        {
            if (text.Contains('[') || text.Contains(']'))
            {
                return false;
            }
            if (text.Contains(':'))
            {
                return true;
            }
            var parts = text.Split('.');
            return parts.Length == 4 && parts.All(p => p.Length is > 0 and <= 3 && p.All(char.IsAsciiDigit));
        }

        private static IPAddress Normalize(IPAddress address)
            => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
