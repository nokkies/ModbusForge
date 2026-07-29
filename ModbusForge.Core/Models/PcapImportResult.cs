using System;
using System.Collections.Generic;

namespace ModbusForge.Models
{
    /// <summary>
    /// Result of importing a .pcap capture file.
    /// </summary>
    public class PcapImportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<ModbusFrameLog> Frames { get; set; } = new();
        public TimeSpan Duration => Frames.Count < 2
            ? TimeSpan.Zero
            : Frames[^1].Timestamp - Frames[0].Timestamp;
    }
}
