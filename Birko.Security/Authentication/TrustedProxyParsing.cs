namespace Birko.Security.Authentication
{
    /// <summary>
    /// How a trusted-proxy list treats an entry that is not an address or a CIDR network, names an unset
    /// <c>${VAR}</c>, or is blank (TASK-830).
    /// </summary>
    public enum TrustedProxyParsing
    {
        /// <summary>Refuse: <see cref="TrustedProxyList.Parse(System.Collections.Generic.IEnumerable{string}?)"/> throws naming the entry.</summary>
        Strict,

        /// <summary>
        /// Skip the entry and report it (<see cref="TrustedProxyList.ParseLenient"/>). The proxy it meant stays untrusted,
        /// so its requests read as coming from the proxy.
        /// </summary>
        Lenient,
    }
}
