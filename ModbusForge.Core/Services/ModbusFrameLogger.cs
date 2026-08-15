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

        /// <summary>
        /// Raised on the thread that logged the frame (a Modbus I/O thread), after the frame
        /// has been added to <see cref="Frames"/>. UI subscribers must marshal to their own
        /// thread before touching Avalonia state.
        /// </summary>
        public event Action<ModbusFrameLog>? FrameLogged;

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
            var log = new ModbusFrameLog
            {
                Timestamp = DateTime.Now,
                Direction = direction,
                RawBytes = rawBytes,
                IsValidCrc = isValidCrc,
                UnitId = unitId,
                FunctionCode = functionCode,
            };

            Append(log);
        }

        public void Log(ModbusFrameLog log)
        {
            if (log is null)
                return;

            Append(log);
        }

        /// <summary>
        /// Adds a frame to the ring buffer. The delta timestamp and the buffer update happen
        /// under a single lock (the previous code used two separate lock sections, so a
        /// concurrent log could interleave and corrupt both deltas).
        /// </summary>
        private void Append(ModbusFrameLog log)
        {
            lock (_sync)
            {
                var nowTicks = _stopwatch.Elapsed.Ticks;
                var last = _lastTimestampTicks;
                _lastTimestampTicks = nowTicks;

                log.DeltaMs = last == 0
                    ? 0.0
                    : (nowTicks - last) * 1000.0 / Stopwatch.Frequency;

                Frames.Add(log);

                while (Frames.Count > Capacity)
                    Frames.RemoveAt(0);
            }

            // Outside the lock: subscribers (e.g. the frame inspector) may marshal to the UI.
            FrameLogged?.Invoke(log);
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
