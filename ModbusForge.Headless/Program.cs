using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Headless
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true);
                    config.AddCommandLine(args, new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["--host"] = "Connection:Host",
                        ["--port"] = "Connection:Port",
                        ["--unit-id"] = "Connection:UnitId",
                        ["--start"] = "Polling:StartAddress",
                        ["--count"] = "Polling:Count",
                        ["--interval"] = "Polling:IntervalMs",
                        ["--area"] = "Polling:Area",
                        ["--custom"] = "Custom:Path",
                        ["--custom-tick"] = "Custom:TickMs",
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging(builder => builder.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss.fff ";
                    }));

                    services.AddSingleton<IConsoleLoggerService, ConsoleLoggerService>();
                    services.AddSingleton<IModbusService, ModbusTcpService>();

                    if (!string.IsNullOrWhiteSpace(context.Configuration["Custom:Path"]))
                    {
                        services.AddHostedService<HeadlessCustomService>();
                    }
                    else
                    {
                        services.AddHostedService<HeadlessPollingService>();
                    }
                })
                .Build();

            await host.RunAsync();
        }
    }
}
