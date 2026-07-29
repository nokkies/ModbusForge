using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// In-memory ring buffer that captures Modbus request/response frames for the inspector.
    /// </summary>
    public class ModbusFrameLogger
    {
        private readonly object _sync = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _lastTimestampTicks;

        public const int DefaultCapacity = 1000;

        public ObservableCollection<ModbusFrameLog> Frames { get; } = new();

        public int Capacity { get; }

        public ModbusFrameLogger() : this(DefaultCapacity)
        {
        }

        public ModbusFrameLogger(int capacity)
        {
            Capacity = Math.Max(1, capacity);
        }

        public void Log(FrameDirection direction, byte[] rawBytes, bool? isValidCrc = null, byte unitId = 0, byte functionCode = 0)
        {
            if (rawBytes is null)
                return;

            var now = _stopwatch.Elapsed;

            long last;
            lock (_sync)
            {
                last = _lastTimestampTicks;
                _lastTimestampTicks = now.Ticks;
            }

            var delta = last == 0
                ? 0.0
                : (now.Ticks - last) * 1000.0 / Stopwatch.Frequency;

            var log = new ModbusFrameLog
            {
                Timestamp = DateTime.Now,
                DeltaMs = delta,
                Direction = direction,
                RawBytes = rawBytes,
                IsValidCrc = isValidCrc,
                UnitId = unitId,
                FunctionCode = functionCode,
            };

            lock (_sync)
            {
                Frames.Add(log);

                while (Frames.Count > Capacity)
                    Frames.RemoveAt(0);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                Frames.Clear();
                _lastTimestampTicks = 0;
            }
        }
    }
}
