using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
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
        public void ParseAssetDownloadUrl_VersionMustMatchAtVersionBoundary()
        {
            // "2026.8.2" is a prefix of "2026.8.27" - a plain substring match
            // would pick the stale installer.
            var json = @"{
                ""assets"": [
                    { ""name"": ""ModbusForge-2026.8.27-setup.exe"", ""browser_download_url"": ""https://stale.example/setup.exe"" },
                    { ""name"": ""ModbusForge-2026.8.2-setup.exe"", ""browser_download_url"": ""https://good.example/setup.exe"" }
                ]
            }";

            var url = UpdateService.ParseAssetDownloadUrl(json, new Version(2026, 8, 2));

            Assert.Equal("https://good.example/setup.exe", url);
        }

        [Fact]
        public void ParseAssetDownloadUrl_ShorterCalVerPrefixDoesNotMatchMoreSpecificAsset()
        {
            // With CalVer tags, every release in a month shares the "YYYY.M"
            // prefix. "2026.8" must not be satisfied by "2026.8.27".
            var json = @"{
                ""assets"": [
                    { ""name"": ""ModbusForge-2026.8.27-setup.exe"", ""browser_download_url"": ""https://specific.example/setup.exe"" },
                    { ""name"": ""ModbusForge-2026.8-setup.exe"", ""browser_download_url"": ""https://exact.example/setup.exe"" }
                ]
            }";

            var url = UpdateService.ParseAssetDownloadUrl(json, new Version(2026, 8));

            Assert.Equal("https://exact.example/setup.exe", url);
        }

        [Fact]
        public async Task DownloadInstallerAsync_Success_WritesCompleteFileAndLeavesNoPartFile()
        {
            var payload = new byte[50000];
            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i % 251);

            using var server = new LocalHttpServer(payload, stallAfterPartial: false);
            var destination = Path.Combine(Path.GetTempPath(), $"mf-dl-{Guid.NewGuid():N}.exe");

            try
            {
                var service = new UpdateService(NullLogger<UpdateService>.Instance);

                var ok = await service.DownloadInstallerAsync($"http://127.0.0.1:{server.Port}/setup.exe", destination);

                Assert.True(ok);
                Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
                Assert.False(File.Exists(destination + ".part"));
            }
            finally
            {
                TryDeleteFile(destination);
                TryDeleteFile(destination + ".part");
            }
        }

        [Fact]
        public async Task DownloadInstallerAsync_Cancellation_RethrowsAndLeavesNoFilesBehind()
        {
            // The server sends a partial body and then stalls; the caller
            // cancels mid-download. The cancellation must propagate and no
            // (partial) file may remain at the destination.
            using var server = new LocalHttpServer(new byte[100000], stallAfterPartial: true);
            var destination = Path.Combine(Path.GetTempPath(), $"mf-dl-{Guid.NewGuid():N}.exe");

            try
            {
                var service = new UpdateService(NullLogger<UpdateService>.Instance);
                using var cts = new CancellationTokenSource();
                var download = service.DownloadInstallerAsync(
                    $"http://127.0.0.1:{server.Port}/setup.exe", destination, null, cts.Token);

                await Task.Delay(1000); // let the partial body arrive and the read block
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
                Assert.False(File.Exists(destination));
                Assert.False(File.Exists(destination + ".part"));
            }
            finally
            {
                TryDeleteFile(destination);
                TryDeleteFile(destination + ".part");
            }
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

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort test cleanup.
            }
        }

        /// <summary>
        /// Minimal loopback HTTP server: serves a fixed body, or sends half of
        /// it and stalls (to simulate a slow/stuck download).
        /// </summary>
        private sealed class LocalHttpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly byte[] _body;
            private readonly bool _stallAfterPartial;

            public LocalHttpServer(byte[] body, bool stallAfterPartial)
            {
                _body = body;
                _stallAfterPartial = stallAfterPartial;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Task.Run(AcceptLoop);
            }

            public int Port { get; }

            private async Task AcceptLoop()
            {
                while (true)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync();
                    }
                    catch
                    {
                        return; // listener stopped
                    }

                    _ = Task.Run(() => HandleAsync(client));
                }
            }

            private async Task HandleAsync(TcpClient client)
            {
                try
                {
                    using (client)
                    {
                        var stream = client.GetStream();

                        // Drain the request headers.
                        var buffer = new byte[4096];
                        var received = string.Empty;
                        while (!received.Contains("\r\n\r\n"))
                        {
                            var count = await stream.ReadAsync(buffer);
                            if (count <= 0) return;
                            received += Encoding.ASCII.GetString(buffer, 0, count);
                        }

                        var header = Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {_body.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header);
                        await stream.FlushAsync();

                        if (_stallAfterPartial)
                        {
                            await stream.WriteAsync(_body.AsMemory(0, _body.Length / 2));
                            await stream.FlushAsync();
                            // Never send the rest; bounded stall so this task
                            // ends even if the client never disconnects.
                            await Task.Delay(TimeSpan.FromSeconds(30));
                            return;
                        }

                        await stream.WriteAsync(_body);
                    }
                }
                catch
                {
                    // Client went away; nothing to do.
                }
            }

            public void Dispose()
            {
                _listener.Stop();
            }
        }
    }
}
