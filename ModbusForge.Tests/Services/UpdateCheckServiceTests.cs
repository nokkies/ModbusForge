using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services;

public class UpdateCheckServiceTests
{
    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }

    [Theory]
    [InlineData("v5.9.0", "5.8.12.0", true)]
    [InlineData("5.9.0", "5.8.12.0", true)]
    [InlineData("v5.8.13", "5.8.12.0", true)]
    [InlineData("v5.8.12", "5.8.12.0", false)]
    [InlineData("v5.8.11", "5.8.12.0", false)]
    [InlineData("v5.8.12-beta", "5.8.12.0", false)]
    [InlineData("v5.9.0-beta", "5.8.12.0", true)]
    [InlineData("v5.8.12+0b0e429", "5.8.12.0", false)]
    public async Task CheckForUpdateAsync_ComparesVersionsCorrectly(string latestTag, string currentVersion, bool expectedUpdate)
    {
        // Arrange
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/nokkies/ModbusForge/releases/latest", request.RequestUri?.ToString());
            Assert.Contains("User-Agent", request.Headers.ToString());

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = latestTag,
                    html_url = "https://github.com/nokkies/ModbusForge/releases/tag/" + latestTag
                }))
            };
            return Task.FromResult(response);
        });

        var service = new UpdateCheckService(new HttpClient(handler), new NullLogger<UpdateCheckService>());

        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);

        // Assert
        Assert.Equal(currentVersion, result.CurrentVersion);
        Assert.Equal("https://github.com/nokkies/ModbusForge/releases/tag/" + latestTag, result.ReleaseUrl);
        Assert.Equal(expectedUpdate, result.IsUpdateAvailable);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsError_WhenApiFails()
    {
        // Arrange
        var handler = new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var service = new UpdateCheckService(new HttpClient(handler), new NullLogger<UpdateCheckService>());

        // Act
        var result = await service.CheckForUpdateAsync("5.8.12");

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsError_WhenCurrentVersionIsEmpty()
    {
        // Arrange
        var service = new UpdateCheckService(new HttpClient(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { tag_name = "v5.9.0", html_url = "url" }))
            }))), new NullLogger<UpdateCheckService>());

        // Act
        var result = await service.CheckForUpdateAsync(string.Empty);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData("v5.8.12", "5.8.12")]
    [InlineData("V5.9.0", "5.9.0")]
    [InlineData("5.8.12-beta", "5.8.12")]
    [InlineData("5.8.12+0b0e429", "5.8.12")]
    [InlineData("v5.9.0-alpha.1+build", "5.9.0")]
    public void NormalizeVersion_StripsPrefixMetadataAndPreRelease(string input, string expected)
    {
        // Act
        var actual = UpdateCheckService.NormalizeVersion(input);

        // Assert
        Assert.Equal(expected, actual);
    }
}
