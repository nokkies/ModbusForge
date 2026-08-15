using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Serilog.Events;
using static ModbusForge.Headless.Program;

namespace ModbusForge.Headless.Tests
{
    public class ProgramArgumentTests
    {
        private static IConfiguration BuildConfig(IDictionary<string, string?>? values = null)
            => new ConfigurationBuilder()
                .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
                .Build();

        [Fact]
        public void TryValidateAndNormalizeArgs_ValidArgs_PassesThrough()
        {
            var ok = TryValidateAndNormalizeArgs(
                new[] { "--host", "10.0.0.5", "--port", "1502", "--unit-id", "7" },
                out var hostArgs,
                out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(6, hostArgs.Count);
        }

        [Fact]
        public void TryValidateAndNormalizeArgs_UnknownOption_IsRejected()
        {
            var ok = TryValidateAndNormalizeArgs(new[] { "--höst", "x" }, out _, out var error);

            Assert.False(ok);
            Assert.Contains("Unknown option", error);
        }

        [Fact]
        public void TryValidateAndNormalizeArgs_MissingValue_IsRejected()
        {
            var ok = TryValidateAndNormalizeArgs(new[] { "--port" }, out _, out var error);

            Assert.False(ok);
            Assert.Contains("requires a value", error);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("70000")]
        [InlineData("abc")]
        public void TryValidateAndNormalizeArgs_InvalidPort_IsRejected(string port)
        {
            Assert.False(TryValidateAndNormalizeArgs(new[] { "--port", port }, out _, out var error));
            Assert.Contains("port", error);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("65535")]
        public void TryValidateAndNormalizeArgs_BoundaryPorts_Accepted(string port)
        {
            Assert.True(TryValidateAndNormalizeArgs(new[] { "--port", port }, out _, out _));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("256")]
        [InlineData("abc")]
        public void TryValidateAndNormalizeArgs_InvalidUnitId_IsRejected(string unitId)
        {
            Assert.False(TryValidateAndNormalizeArgs(new[] { "--unit-id", unitId }, out _, out var error));
            Assert.Contains("unit id", error);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("255")]
        public void TryValidateAndNormalizeArgs_BoundaryUnitIds_Accepted(string unitId)
        {
            Assert.True(TryValidateAndNormalizeArgs(new[] { "--unit-id", unitId }, out _, out _));
        }

        [Fact]
        public void TryValidateAndNormalizeArgs_EnvironmentOption_IsRecognized()
        {
            Assert.True(TryValidateAndNormalizeArgs(new[] { "--environment", "Development" }, out _, out _));
        }

        [Theory]
        [InlineData("Debug", LogEventLevel.Debug)]
        [InlineData("debug", LogEventLevel.Debug)]
        [InlineData("WARNING", LogEventLevel.Warning)]
        [InlineData("warn", LogEventLevel.Warning)]
        [InlineData("Trace", LogEventLevel.Verbose)]
        [InlineData("info", LogEventLevel.Information)]
        [InlineData("Information", LogEventLevel.Information)]
        [InlineData("Critical", LogEventLevel.Fatal)]
        [InlineData("fatal", LogEventLevel.Fatal)]
        public void ParseLogLevel_MatchesCaseInsensitively(string raw, LogEventLevel expected)
        {
            Assert.Equal(expected, ParseLogLevel(raw, LogEventLevel.Information));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("bogus")]
        public void ParseLogLevel_UnknownValue_FallsBack(string? raw)
        {
            Assert.Equal(LogEventLevel.Warning, ParseLogLevel(raw, LogEventLevel.Warning));
        }

        [Fact]
        public void ValidateRuntimeConfiguration_InvalidPollingCount_IsRejected()
        {
            var config = BuildConfig(new Dictionary<string, string?> { ["Polling:Count"] = "0" });

            Assert.False(ValidateRuntimeConfiguration(config, out var error));
            Assert.Contains("Polling:Count", error);
        }

        [Fact]
        public void ValidateRuntimeConfiguration_NonPositiveInterval_IsRejected()
        {
            var config = BuildConfig(new Dictionary<string, string?> { ["Polling:IntervalMs"] = "0" });

            Assert.False(ValidateRuntimeConfiguration(config, out var error));
            Assert.Contains("Polling:IntervalMs", error);
        }

        [Fact]
        public void ValidateRuntimeConfiguration_NegativeCustomTick_IsRejected()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Custom:Path"] = "watch.json",
                ["Custom:TickMs"] = "-5",
            });

            Assert.False(ValidateRuntimeConfiguration(config, out var error));
            Assert.Contains("Custom:TickMs", error);
        }

        [Fact]
        public void ValidateRuntimeConfiguration_ValidPollingConfig_Passes()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Polling:Count"] = "10",
                ["Polling:IntervalMs"] = "500",
                ["Polling:StartAddress"] = "0",
            });

            Assert.True(ValidateRuntimeConfiguration(config, out _));
        }

        [Fact]
        public void ValidateRuntimeConfiguration_CustomMode_IgnoresPollingValues()
        {
            // In custom-watch mode Polling:* is irrelevant; only Custom:TickMs matters.
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Custom:Path"] = "watch.json",
                ["Polling:Count"] = "0",
            });

            Assert.True(ValidateRuntimeConfiguration(config, out _));
        }
    }
}
