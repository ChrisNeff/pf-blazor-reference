using System.Net;
using PF.BlazorReference.Web.RateLimiting;

namespace PF.BlazorReference.Tests.Web;

/// <summary>
/// The rate limits count per client key. The test server has no remote address, so the keying rules are
/// tested here directly instead of through HTTP.
/// </summary>
public class ClientKeyTests
{
    [Fact]
    public void An_ipv4_address_is_its_own_key()
    {
        Assert.Equal("203.0.113.7", ClientKey.For(IPAddress.Parse("203.0.113.7")));
    }

    [Fact]
    public void An_ipv4_address_mapped_into_ipv6_is_the_same_client_as_the_plain_address()
    {
        Assert.Equal(
            ClientKey.For(IPAddress.Parse("203.0.113.7")),
            ClientKey.For(IPAddress.Parse("::ffff:203.0.113.7")));
    }

    [Fact]
    public void Addresses_in_the_same_ipv6_slash_64_share_a_key()
    {
        Assert.Equal(
            ClientKey.For(IPAddress.Parse("2001:db8:abcd:12::1")),
            ClientKey.For(IPAddress.Parse("2001:db8:abcd:12:ffff:ffff:ffff:ffff")));
    }

    [Fact]
    public void Addresses_in_different_ipv6_slash_64s_get_different_keys()
    {
        Assert.NotEqual(
            ClientKey.For(IPAddress.Parse("2001:db8:abcd:12::1")),
            ClientKey.For(IPAddress.Parse("2001:db8:abcd:13::1")));
    }

    [Fact]
    public void A_missing_address_shares_one_unknown_bucket()
    {
        Assert.Equal(ClientKey.Unknown, ClientKey.For(null));
    }
}
