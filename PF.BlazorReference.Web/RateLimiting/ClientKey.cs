using System.Net;
using System.Net.Sockets;

namespace PF.BlazorReference.Web.RateLimiting;

/// <summary>
/// The identity a rate limit counts against: the client address, normalised so it is hard to dodge.
/// </summary>
public static class ClientKey
{
    public const string Unknown = "unknown";

    public static string For(IPAddress? address)
    {
        if (address is null)
            return Unknown;

        // "::ffff:203.0.113.7" and "203.0.113.7" are the same client.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        // One IPv6 client typically controls a whole /64, so counting single addresses would let it
        // rotate through 2^64 of them. Count the /64 instead.
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
