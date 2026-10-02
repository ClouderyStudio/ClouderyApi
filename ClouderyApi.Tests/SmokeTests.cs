using System.Net;

namespace ClouderyApi.Tests;

public sealed class SmokeTests : IntegrationTestBase
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var response = await Client.GetAsync("/mhop/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
    }
}
