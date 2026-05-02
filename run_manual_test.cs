using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Services;
using System.Reflection;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting manual test...");
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<ModbusServerService>();

        using var service = new ModbusServerService(logger);

        // Use reflection to call private GetLocalNetworkIp
        MethodInfo method = typeof(ModbusServerService).GetMethod("GetLocalNetworkIp", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method != null)
        {
            var result = method.Invoke(service, null);
            Console.WriteLine($"GetLocalNetworkIp returned: {result}");
        }
        else
        {
            Console.WriteLine("GetLocalNetworkIp method not found!");
        }

        Console.WriteLine("Manual test finished successfully!");
    }
}
