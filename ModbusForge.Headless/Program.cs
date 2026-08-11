using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ModbusForge.Headless
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("-h", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("-?", StringComparer.OrdinalIgnoreCase))
            {
                PrintHelp();
                return;
            }

            if (!TryValidateAndNormalizeArgs(args, out var hostArgs, out var error))
            {
                Console.WriteLine(error);
                Console.WriteLine();
                PrintHelp();
                Environment.ExitCode = 1;
                return;
            }

            var envName = "Production";
            for (int i = 0; i < hostArgs.Count; i++)
            {
                if (i + 1 < hostArgs.Count &&
                    (hostArgs[i] == "--environment" || hostArgs[i] == "-e"))
                {
                    envName = hostArgs[i + 1];
                    hostArgs.RemoveRange(i, 2);
                    break;
                }
            }

            var host = new HostBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .UseEnvironment(envName)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables("MODBUSFORGE_");
                    config.AddCommandLine(hostArgs.ToArray(), _switchMappings);
                })
                .UseSerilog((context, config) => ConfigureSerilog(context, config))
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IModbusAddressValidator, ModbusAddressValidator>();
                    services.AddSingleton<IValidationService, ValidationService>();
                    services.AddSingleton<MqttGatewayService>();
                    services.AddSingleton<IModbusService>(sp => CreateModbusService(sp));

                    if (!string.IsNullOrWhiteSpace(context.Configuration["Custom:Path"]))
                    {
                        services.AddHostedService<HeadlessCustomService>();
                    }
                    else
                    {
                        services.AddHostedService<HeadlessPollingService>();
                    }
                })
                .UseConsoleLifetime()
                .Build();

            using (host)
            {
                var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                var logger = host.Services.GetRequiredService<ILogger<Program>>();

                lifetime.ApplicationStarted.Register(() =>
                    logger.LogInformation("ModbusForge.Headless started. Press Ctrl+C to stop."));
                lifetime.ApplicationStopping.Register(() =>
                    logger.LogInformation("ModbusForge.Headless is stopping..."));

                await host.RunAsync();
            }
        }

        private static IModbusService CreateModbusService(IServiceProvider sp)
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var transport = HeadlessProfileFactory.CreateConnectionProfile(config).Transport;
            var addressValidator = sp.GetService<IModbusAddressValidator>();
            var validation = sp.GetService<IValidationService>();
            var frameLogger = new ModbusFrameLogger();

            if (transport == TransportType.Tcp)
            {
                var logger = sp.GetRequiredService<ILogger<ModbusTcpService>>();
                return new ModbusTcpService(logger, null, frameLogger, addressValidator);
            }

            var serialLogger = sp.GetRequiredService<ILogger<ModbusSerialService>>();
            return new ModbusSerialService(serialLogger, null, validation, frameLogger, addressValidator, transport);
        }

        private static void ConfigureSerilog(HostBuilderContext context, LoggerConfiguration config)
        {
            var configuration = context.Configuration;

            var defaultLevel = configuration["Logging:LogLevel:Default"] switch
            {
                "Debug" => LogEventLevel.Debug,
                "Information" or null or "" => LogEventLevel.Information,
                "Warning" => LogEventLevel.Warning,
                "Error" => LogEventLevel.Error,
                "Fatal" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information,
            };

            var microsoftLevel = configuration["Logging:LogLevel:Microsoft"] switch
            {
                "Debug" => LogEventLevel.Debug,
                "Information" => LogEventLevel.Information,
                "Warning" or null or "" => LogEventLevel.Warning,
                "Error" => LogEventLevel.Error,
                "Fatal" => LogEventLevel.Fatal,
                _ => LogEventLevel.Warning,
            };

            config.MinimumLevel.Is(defaultLevel)
                .MinimumLevel.Override("Microsoft", microsoftLevel)
                .MinimumLevel.Override("System", microsoftLevel)
                .Enrich.FromLogContext();

            var useJson = configuration.GetValue<bool>("Logging:Console:UseJson");

            if (useJson)
            {
                config.WriteTo.Console(new CompactJsonFormatter());
            }
            else
            {
                config.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
            }

            var filePath = configuration["Logging:File:Path"];
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var retainedFileCountLimit = configuration.GetValue<int?>("Logging:File:RetainedFileCountLimit") ?? 7;
                var fileSizeLimitBytes = configuration.GetValue<int?>("Logging:File:FileSizeLimitBytes") ?? 10485760;
                var rollOnFileSizeLimit = configuration.GetValue<bool?>("Logging:File:RollOnFileSizeLimit") ?? true;
                var rollingInterval = configuration.GetValue<RollingInterval?>("Logging:File:RollingInterval") ?? RollingInterval.Day;

                if (useJson)
                {
                    config.WriteTo.File(
                        new CompactJsonFormatter(),
                        filePath,
                        rollingInterval: rollingInterval,
                        fileSizeLimitBytes: fileSizeLimitBytes,
                        rollOnFileSizeLimit: rollOnFileSizeLimit,
                        retainedFileCountLimit: retainedFileCountLimit);
                }
                else
                {
                    config.WriteTo.File(
                        filePath,
                        rollingInterval: rollingInterval,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                        fileSizeLimitBytes: fileSizeLimitBytes,
                        rollOnFileSizeLimit: rollOnFileSizeLimit,
                        retainedFileCountLimit: retainedFileCountLimit);
                }
            }
        }

        private static readonly Dictionary<string, string> _switchMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["--host"] = "Connection:Host",
            ["--port"] = "Connection:Port",
            ["--unit-id"] = "Connection:UnitId",
            ["--transport"] = "Connection:Transport",
            ["--com-port"] = "Connection:ComPort",
            ["--baud-rate"] = "Connection:BaudRate",
            ["--parity"] = "Connection:Parity",
            ["--data-bits"] = "Connection:DataBits",
            ["--stop-bits"] = "Connection:StopBits",
            ["--rts-enable"] = "Connection:RtsEnable",
            ["--pre-tx-delay"] = "Connection:PreTxDelayMs",
            ["--post-tx-delay"] = "Connection:PostTxDelayMs",
            ["--start"] = "Polling:StartAddress",
            ["--count"] = "Polling:Count",
            ["--interval"] = "Polling:IntervalMs",
            ["--area"] = "Polling:Area",
            ["--custom"] = "Custom:Path",
            ["--custom-tick"] = "Custom:TickMs",
            ["--mqtt-enabled"] = "Mqtt:Enabled",
            ["--mqtt-broker-host"] = "Mqtt:BrokerHost",
            ["--mqtt-broker-port"] = "Mqtt:BrokerPort",
            ["--mqtt-client-id"] = "Mqtt:ClientId",
            ["--mqtt-username"] = "Mqtt:Username",
            ["--mqtt-password"] = "Mqtt:Password",
            ["--mqtt-topic-template"] = "Mqtt:TopicTemplate",
            ["--mqtt-qos"] = "Mqtt:QualityOfService",
            ["--mqtt-retain"] = "Mqtt:RetainMessages",
            ["--mqtt-publish-period"] = "Mqtt:PublishPeriodMs",
        };

        private static bool TryValidateAndNormalizeArgs(IReadOnlyList<string> args, out List<string> hostArgs, out string? error)
        {
            hostArgs = new List<string>(args);
            error = null;

            for (int i = 0; i < hostArgs.Count; i++)
            {
                var arg = hostArgs[i];

                if (i + 1 < hostArgs.Count)
                {
                    if (string.Equals(arg, "--port", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(hostArgs[i + 1], out var port) || port < 1 || port > 65535)
                        {
                            error = "Invalid port. Must be between 1 and 65535.";
                            return false;
                        }
                    }

                    if (string.Equals(arg, "--unit-id", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!byte.TryParse(hostArgs[i + 1], out var unitId) || unitId == 0)
                        {
                            error = "Invalid unit id. Must be between 1 and 255.";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
ModbusForge.Headless - cross-platform Modbus TCP/RTU poller

Usage:
  ModbusForge.Headless [options]

Connection options:
  --host <ip>           Modbus TCP host (default: 127.0.0.1)
  --port <port>         Modbus TCP port (default: 502)
  --unit-id <id>        Modbus unit/slave id (default: 1)
  --transport <type>    Transport: Tcp, Rtu, Ascii, Serial (default: Tcp)
  --com-port <name>     Serial port name (default: COM1)
  --baud-rate <rate>    Serial baud rate (default: 9600)
  --parity <type>       Serial parity: None, Odd, Even (default: None)
  --data-bits <bits>    Serial data bits (default: 8)
  --stop-bits <bits>    Serial stop bits: None, One, Two, OnePointFive (default: One)
  --rts-enable <bool>   Enable RTS for serial (default: false)
  --pre-tx-delay <ms>   Serial pre-transmit delay (default: 0)
  --post-tx-delay <ms>  Serial post-transmit delay (default: 0)

Polling options:
  --start <addr>        Start address (default: 0)
  --count <n>           Number of points to poll (default: 10)
  --interval <ms>       Poll interval in milliseconds (default: 1000)
  --area <area>         PlcArea: HoldingRegister, InputRegister, Coil, DiscreteInput (default: HoldingRegister)

Custom watch options:
  --custom <path>       Path to a custom watch JSON file
  --custom-tick <ms>    Custom watch tick in ms (default: 100)

MQTT options:
  --mqtt-enabled <true|false>         Enable MQTT publishing (default: false)
  --mqtt-broker-host <host>           MQTT broker host (default: localhost)
  --mqtt-broker-port <port>           MQTT broker port (default: 1883)
  --mqtt-client-id <id>               MQTT client id (default: ModbusForge)
  --mqtt-username <user>              MQTT username
  --mqtt-password <pass>              MQTT password
  --mqtt-topic-template <tpl>         Topic template (default: modbusforge/{UnitId}/{Tag})
  --mqtt-qos <0|1|2>                  MQTT QoS (default: 0)
  --mqtt-retain <true|false>          Retain MQTT messages (default: false)
  --mqtt-publish-period <ms>          MQTT publish period (default: 1000)

Other options:
  --environment <env>   Hosting environment: Development/Production (default: Production)
  -h, --help            Show this help

Configuration can also be supplied in appsettings.json and appsettings.<environment>.json.
");
        }
    }
}
