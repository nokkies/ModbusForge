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
    /// <remarks>
    /// Frames are captured on worker threads (the socket read/write loops), but the inspector's
    /// grid binds to <see cref="Frames"/> on the UI thread. An optional
    /// <see cref="IDispatcher"/> is used to marshal collection mutations onto the UI thread so
    /// the grid reliably updates; without a dispatcher (headless, tests) mutations happen inline.
    /// </remarks>
    public class ModbusFrameLogger
    {
        private readonly object _sync = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _lastTimestampTicks;
        private readonly IDispatcher? _uiDispatcher;

        public const int DefaultCapacity = 1000;

        public ObservableCollection<ModbusFrameLog> Frames { get; } = new();

        public int Capacity { get; }

        public ModbusFrameLogger()
            : this(DefaultCapacity, null)
        {
        }

        public ModbusFrameLogger(int capacity)
            : this(capacity, null)
        {
        }

        public ModbusFrameLogger(int capacity, IDispatcher? uiDispatcher)
        {
            Capacity = Math.Max(1, capacity);
            _uiDispatcher = uiDispatcher;
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

            Append(log);
        }

        public void Log(ModbusFrameLog log)
        {
            if (log is null)
                return;

            Append(log);
        }

        private void Append(ModbusFrameLog log)
        {
            if (_uiDispatcher is null)
            {
                AppendInline(log);
                return;
            }

            // Mutate the observable collection on the UI thread so bound views update;
            // posting (not invoking) keeps the capturing socket loop non-blocking.
            _uiDispatcher.Post(() => AppendInline(log));
        }

        private void AppendInline(ModbusFrameLog log)
        {
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
