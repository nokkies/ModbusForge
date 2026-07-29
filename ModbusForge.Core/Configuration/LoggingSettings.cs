using System;

namespace ModbusForge.Configuration
{
    public class LoggingSettings
    {
        // Retention window in minutes (1..60)
        public int RetentionMinutes { get; set; } = 10;

        // Sampling interval in milliseconds
        public int SampleRateMs { get; set; } = 500;

        // Default export folder for PNG/CSV
        public string ExportFolder { get; set; } = "Exports";

        public void Clamp()
        {
            if (RetentionMinutes < 1) RetentionMinutes = 1;
            if (RetentionMinutes > 60) RetentionMinutes = 60;
            if (SampleRateMs < 50) SampleRateMs = 50; // sane minimum
            if (SampleRateMs > 60000) SampleRateMs = 60000; // avoid runaway memory growth
        }
    }
}
