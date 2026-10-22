using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TeachWell.Application;
using TeachWell.Infrastructure.Canvas;
using Xunit;

public class CanvasSecurityTests
{
    private static CanvasIntegrationOptions Options() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Features:CanvasPAT"] = "true", ["Canvas:AllowedHosts:0"] = "canvas.example.test", ["Canvas:EncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }).Build());

    [Fact] public void Encryption_is_randomized_and_bound_to_tenant_owner_and_connection()
    {
        var p = new CanvasTokenProtector(Options()); var token = Guid.NewGuid().ToString("N"); var tenant = Guid.NewGuid(); var owner = Guid.NewGuid(); var id = Guid.NewGuid();
        var encrypted = p.Protect(token, tenant, owner, id);
        Assert.NotEqual(encrypted, p.Protect(token, tenant, owner, id));
        Assert.Equal(token, p.Unprotect(encrypted, tenant, owner, id));
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(encrypted, Guid.NewGuid(), owner, id));
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(encrypted, tenant, Guid.NewGuid(), id));
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(encrypted, tenant, owner, Guid.NewGuid()));
        Assert.ThrowsAny<CryptographicException>(() => new CanvasTokenProtector(Options()).Unprotect(encrypted, tenant, owner, id));
        var bytes = Convert.FromBase64String(encrypted); bytes[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(bytes), tenant, owner, id));
    }
    [Theory]
    [InlineData("http://canvas.example.test")]
    [InlineData("https://canvas.example.test:444")]
    [InlineData("https://user:pass@canvas.example.test")]
    [InlineData("https://canvas.example.test/path")]
    [InlineData("https://canvas.example.test?access_token=example")]
    [InlineData("https://canvas.example.test/#fragment")]
    [InlineData("https://canvas.example.test.evil.test")]
    [InlineData("https://127.0.0.1")]
    public void Only_exact_approved_https_origins_are_accepted(string value) => Assert.Null(new CanvasHostPolicy(Options()).ParseOrigin(value));

    [Theory]
    [InlineData("127.0.0.1")][InlineData("10.0.0.1")][InlineData("169.254.169.254")][InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")][InlineData("100.64.0.1")][InlineData("0.0.0.0")][InlineData("224.0.0.1")]
    [InlineData("192.0.2.1")][InlineData("198.18.0.1")][InlineData("203.0.113.1")][InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")][InlineData("fd00::1")][InlineData("fe80::1")][InlineData("2001:db8::1")][InlineData("2002::1")][InlineData("3fff::1")]
    public void Private_metadata_reserved_and_transition_addresses_are_blocked(string value) => Assert.False(CanvasHostPolicy.IsPublicAddress(IPAddress.Parse(value)));
    [Theory][InlineData("8.8.8.8")][InlineData("1.1.1.1")][InlineData("2606:4700:4700::1111")]
    public void Public_addresses_are_accepted(string value) => Assert.True(CanvasHostPolicy.IsPublicAddress(IPAddress.Parse(value)));

    [Theory][InlineData(401, CanvasError.InvalidToken)][InlineData(403, CanvasError.PermissionDenied)][InlineData(429, CanvasError.RateLimited)]
    [InlineData(302, CanvasError.RedirectBlocked)][InlineData(500, CanvasError.Unavailable)]
    public async Task Adapter_maps_remote_errors_without_exposing_response_bodies(int status, CanvasError expected)
    {
        var token = Guid.NewGuid().ToString("N");
        using var http = new HttpClient(new ResponseHandler(request => {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("/api/v1/users/self/profile", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query); Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal(token, request.Headers.Authorization.Parameter);
            return new((HttpStatusCode)status) { Content = new StringContent(token) };
        }));
        var result = await new CanvasPatClient(http, new CanvasHostPolicy(Options())).ValidateConnectionAsync(new("https://canvas.example.test/"), token, default);
        Assert.Equal(expected, result.Error); Assert.DoesNotContain(token, JsonSerializer.Serialize(result));
    }
    [Theory][InlineData("{\"id\":42,\"primary_email\":\"ignored@example.test\"}", CanvasError.None)]
    [InlineData("{\"id\":null}", CanvasError.UnexpectedResponse)][InlineData("{\"id\":-1}", CanvasError.UnexpectedResponse)]
    [InlineData("<html>error</html>", CanvasError.UnexpectedResponse)]
    public async Task Adapter_retains_only_a_valid_external_id(string body, CanvasError expected)
    {
        using var http = new HttpClient(new ResponseHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var result = await new CanvasPatClient(http, new CanvasHostPolicy(Options())).ValidateConnectionAsync(new("https://canvas.example.test/"), "fictional-test-input", default);
        Assert.Equal(expected, result.Error); Assert.DoesNotContain("ignored@example.test", JsonSerializer.Serialize(result));
    }
    [Fact] public void Network_handler_blocks_redirects_proxies_and_cookie_reuse()
    {
        using var handler = new CanvasHostPolicy(Options()).CreateHandler();
        Assert.False(handler.AllowAutoRedirect); Assert.False(handler.UseProxy); Assert.False(handler.UseCookies); Assert.NotNull(handler.ConnectCallback);
    }
    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
