using System;
using System.Reflection;
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

        [Fact]
        public void ParseAssetDownloadUrl_MatchesCalVerInstaller()
        {
            var json = @"{
                ""assets"": [
                    { ""name"": ""ModbusForge-2026.7.1-setup.exe"", ""browser_download_url"": ""https://good.example/setup.exe"" },
                    { ""name"": ""ModbusForge-2026.7.1-win-x64.zip"", ""browser_download_url"": ""https://bad.example/win.zip"" },
                    { ""name"": ""ModbusForge.Headless-v2026.7.1-linux-x64.zip"", ""browser_download_url"": ""https://bad.example/headless.zip"" }
                ]
            }";

            var url = UpdateService.ParseAssetDownloadUrl(json, new Version(2026, 7, 1));

            Assert.Equal("https://good.example/setup.exe", url);
        }

        [Fact]
        public void CheckForUpdateAsync_DetectsCalVerUpdateFromFourPartVersion()
        {
            // The release workflow sets AssemblyVersion to <version>.0 (e.g. 2026.7.1.0),
            // while release tags are 3-part (e.g. 2026.7.1). The updater must compare
            // these consistently.
            var current = new Version(6, 0, 8, 0);
            var latest = new Version(2026, 7, 1);

            // Simulate normalization via reflection
            var method = typeof(UpdateService).GetMethod("NormalizeVersion", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var currentNorm = (Version?)method.Invoke(null, new object?[] { current });
            var latestNorm = (Version?)method.Invoke(null, new object?[] { latest });

            Assert.True(latestNorm > currentNorm);
            Assert.Equal(new Version(2026, 7, 1, 0), latestNorm);
        }

        [Fact]
        public void CheckForUpdateAsync_SameVersionWithAndWithoutTrailingRevision_IsUpToDate()
        {
            var current = new Version(2026, 7, 1, 0);
            var latest = new Version(2026, 7, 1);

            var method = typeof(UpdateService).GetMethod("NormalizeVersion", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var currentNorm = (Version?)method.Invoke(null, new object?[] { current });
            var latestNorm = (Version?)method.Invoke(null, new object?[] { latest });

            Assert.Equal(currentNorm, latestNorm);
        }
    }
}
