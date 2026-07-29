using System;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class UpdateServiceTests
    {
        [Fact]
        public void ParseAssetDownloadUrl_PrefersVersionMatchingInstaller()
        {
            var json = @"{
                ""assets"": [
                    { ""name"": ""ModbusForge-2.0.2-setup.exe"", ""browser_download_url"": ""https://bad.example/2.0.2.exe"" },
                    { ""name"": ""ModbusForge-6.0.8-setup.exe"", ""browser_download_url"": ""https://good.example/6.0.8.exe"" },
                    { ""name"": ""ModbusForge-6.0.8-win-x64-sc.zip"", ""browser_download_url"": ""https://good.example/6.0.8.zip"" }
                ]
            }";

            var url = UpdateService.ParseAssetDownloadUrl(json, new Version(6, 0, 8));

            Assert.Equal("https://good.example/6.0.8.exe", url);
        }

        [Fact]
        public void ParseAssetDownloadUrl_FallsBackToFirstExeWhenVersionMissing()
        {
            var json = @"{
                ""assets"": [
                    { ""name"": ""ModbusForge-2.0.2-setup.exe"", ""browser_download_url"": ""https://bad.example/2.0.2.exe"" },
                    { ""name"": ""ModbusForge-6.0.8-setup.exe"", ""browser_download_url"": ""https://good.example/6.0.8.exe"" }
                ]
            }";

            var url = UpdateService.ParseAssetDownloadUrl(json, new Version(7, 0, 0));

            Assert.Equal("https://bad.example/2.0.2.exe", url);
        }

        [Fact]
        public void ParseAssetDownloadUrl_IgnoresNonExeAssets()
        {
            var json = @"{
                ""assets"": [
                    { ""name"": ""ModbusForge-6.0.8-win-x64-sc.zip"", ""browser_download_url"": ""https://example/6.0.8.zip"" }
                ]
            }";

            var url = UpdateService.ParseAssetDownloadUrl(json, new Version(6, 0, 8));

            Assert.Null(url);
        }
    }
}
