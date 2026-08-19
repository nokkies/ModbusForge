using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusFrameLoggerTests
    {
        [Fact]
        public void Log_AddsFrames()
        {
            var logger = new ModbusFrameLogger();

            logger.Log(FrameDirection.Tx, new byte[] { 1, 2, 3 }, unitId: 1, functionCode: 6);

            var frame = Assert.Single(logger.Frames);
            Assert.Equal(FrameDirection.Tx, frame.Direction);
            Assert.Equal(1, frame.UnitId);
            Assert.Equal(6, frame.FunctionCode);
        }

        [Fact]
        public void Capacity_TrimsOldestFrames()
        {
            var logger = new ModbusFrameLogger(3);

            for (var i = 0; i < 5; i++)
            {
                logger.Log(FrameDirection.Rx, new byte[] { (byte)i }, unitId: 1, functionCode: (byte)(i + 1));
            }

            Assert.Equal(3, logger.Frames.Count);
            Assert.Equal(3, logger.Frames[0].FunctionCode); // oldest kept: the 4th frame
            Assert.Equal(5, logger.Frames[2].FunctionCode);
        }

        [Fact]
        public void Log_IgnoresNullBytes()
        {
            var logger = new ModbusFrameLogger();

            logger.Log(FrameDirection.Tx, null!);

            Assert.Empty(logger.Frames);
        }

        [Fact]
        public void DeltaMs_IsZeroForFirstFrameAndPositiveAfter()
        {
            var logger = new ModbusFrameLogger();

            logger.Log(FrameDirection.Rx, new byte[] { 1 });
            Thread.Sleep(20);
            logger.Log(FrameDirection.Tx, new byte[] { 2 });

            Assert.Equal(0.0, logger.Frames[0].DeltaMs);
            Assert.True(logger.Frames[1].DeltaMs > 0, "second frame should carry a positive delta");
        }

        [Fact]
        public void Clear_RemovesAllFramesAndResetsDelta()
        {
            var logger = new ModbusFrameLogger();
            logger.Log(FrameDirection.Rx, new byte[] { 1 });
            Thread.Sleep(10);
            logger.Clear();

            Assert.Empty(logger.Frames);

            logger.Log(FrameDirection.Tx, new byte[] { 2 });
            Assert.Equal(0.0, logger.Frames[0].DeltaMs);
        }

        [Fact]
        public void WithInlineDispatcher_CollectionIsUpdatedSynchronously()
        {
            var logger = new ModbusFrameLogger(100, new InlineDispatcher());

            logger.Log(FrameDirection.Rx, new byte[] { 1 });

            Assert.Single(logger.Frames);
        }

        [Fact]
        public void WithDispatcher_MutationRunsOnTheDispatcherThread()
        {
            // A stand-in UI dispatcher: Post enqueues onto a queue drained by a
            // dedicated background thread. The drain loop parks on a gate until
            // the test releases it, so the "not applied yet" assertion below is
            // deterministic instead of racing the dispatcher thread.
            var queue = new BlockingCollection<Action>();
            var drainGate = new ManualResetEventSlim(false);
            var dispatcherThread = new Thread(() =>
            {
                foreach (var action in queue.GetConsumingEnumerable())
                {
                    drainGate.Wait();
                    action();
                }
            })
            {
                IsBackground = true,
                Name = "fake-ui-dispatcher",
            };
            dispatcherThread.Start();

            int mutationThread = 0;
            var logger = new ModbusFrameLogger(100, new QueueDispatcher(queue));
            logger.Frames.CollectionChanged += (_, _) => mutationThread = Environment.CurrentManagedThreadId;

            logger.Log(FrameDirection.Rx, new byte[] { 1 });

            // Not applied synchronously on the capturing thread (the dispatcher
            // thread is parked on the gate, so nothing can have run yet)...
            Assert.Empty(logger.Frames);

            // ...and applied by the dispatcher thread once released.
            drainGate.Set();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (logger.Frames.Count == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }

            Assert.Single(logger.Frames);
            Assert.Equal(dispatcherThread.ManagedThreadId, mutationThread);
            Assert.NotEqual(Environment.CurrentManagedThreadId, mutationThread);

            queue.CompleteAdding();
        }
    }

    /// <summary>
    /// Dispatcher that runs actions inline (like a UI dispatcher when already on the UI thread).
    /// </summary>
    internal sealed class InlineDispatcher : IDispatcher
    {
        public bool CheckAccess => true;
        public void Invoke(Action action) => action();
        public T Invoke<T>(Func<T> func) => func();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public void Post(Action action) => action();
    }

    /// <summary>
    /// Dispatcher that posts actions onto a queue drained by another thread.
    /// </summary>
    internal sealed class QueueDispatcher : IDispatcher
    {
        private readonly BlockingCollection<Action> _queue;

        public QueueDispatcher(BlockingCollection<Action> queue)
        {
            _queue = queue;
        }

        public bool CheckAccess => false;
        public void Invoke(Action action) => throw new NotSupportedException();
        public T Invoke<T>(Func<T> func) => throw new NotSupportedException();
        public Task InvokeAsync(Action action) => Task.FromException(new NotSupportedException());
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromException<T>(new NotSupportedException());
        public void Post(Action action) => _queue.Add(action);
    }
}
