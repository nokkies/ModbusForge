using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;

namespace ModbusForge.Tests.Services
{
    public class UnhandledExceptionReporterTests : IDisposable
    {
        private readonly string _crashLogDirectory = Path.Combine(Path.GetTempPath(), "ModbusForgeTests", Guid.NewGuid().ToString("N"));
        private readonly CapturingLogger _logger = new();
        private readonly RecordingMessageBox _messageBoxes = new();

        public UnhandledExceptionReporterTests()
        {
            Directory.CreateDirectory(_crashLogDirectory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_crashLogDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private UnhandledExceptionReporter CreateReporter()
            => new(
                new ImmediateDispatcher(),
                _logger,
                _messageBoxes,
                _crashLogDirectory);

        [Fact]
        public void OnDispatcherException_OrdinaryException_ReportsAndAllowsContinuation()
        {
            var reporter = CreateReporter();

            var handled = reporter.OnDispatcherException(new InvalidOperationException("boom"));

            Assert.True(handled);
            Assert.Single(_messageBoxes.Messages);
            Assert.Contains("InvalidOperationException", _messageBoxes.Messages[0]);
            Assert.Contains("boom", _messageBoxes.Messages[0]);
            Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
        }

        [Fact]
        public void OnDispatcherException_OutOfMemory_DoesNotContinueAndShowsNoDialog()
        {
            var reporter = CreateReporter();

            var handled = reporter.OnDispatcherException(new OutOfMemoryException());

            Assert.False(handled);
            Assert.Empty(_messageBoxes.Messages);
            Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Critical);
        }

        [Fact]
        public void OnDispatcherException_SameExceptionTwice_ShowsDialogOnce()
        {
            var reporter = CreateReporter();

            reporter.OnDispatcherException(new InvalidOperationException("boom"));
            reporter.OnDispatcherException(new InvalidOperationException("boom"));

            Assert.Single(_messageBoxes.Messages);
            Assert.Equal(2, _logger.Entries.Count(e => e.Level == LogLevel.Error));
        }

        [Fact]
        public void OnDispatcherException_DifferentExceptionWithinCooldown_StillShowsDialog()
        {
            var reporter = CreateReporter();

            reporter.OnDispatcherException(new InvalidOperationException("first"));
            reporter.OnDispatcherException(new ArgumentException("second"));

            Assert.Equal(2, _messageBoxes.Messages.Count);
        }

        [Fact]
        public void OnDispatcherException_Null_IsTreatedAsHandled()
        {
            var reporter = CreateReporter();

            Assert.True(reporter.OnDispatcherException(null!));
            Assert.Empty(_messageBoxes.Messages);
        }

        [Fact]
        public void HandleUnobservedTaskException_LogsTheFaultWithoutDialog()
        {
            var reporter = CreateReporter();
            // Mirrors what the runtime supplies: a single-layer aggregate wrapping
            // the task's fault (the same shape Task.Exception produces).
            var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new TimeoutException("slow")));

            reporter.HandleUnobservedTaskException(null, args);

            Assert.Empty(_messageBoxes.Messages);
            Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception is TimeoutException);
        }

        [Fact]
        public void HandleUnobservedTaskException_FatalFault_IsIgnored()
        {
            var reporter = CreateReporter();
            var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new OutOfMemoryException()));

            reporter.HandleUnobservedTaskException(null, args);

            Assert.Empty(_messageBoxes.Messages);
            Assert.Empty(_logger.Entries);
        }

        [Fact]
        public void HandleDomainUnhandledException_NonTerminating_ShowsDialog()
        {
            var reporter = CreateReporter();

            reporter.HandleDomainUnhandledException(null, new UnhandledExceptionEventArgs(new NullReferenceException(), isTerminating: false));

            Assert.Single(_messageBoxes.Messages);
            Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception is NullReferenceException);
        }

        [Fact]
        public void HandleDomainUnhandledException_Terminating_LogsCriticalWithoutDialog()
        {
            var reporter = CreateReporter();

            reporter.HandleDomainUnhandledException(null, new UnhandledExceptionEventArgs(new TypeLoadException(), isTerminating: true));

            Assert.Empty(_messageBoxes.Messages);
            Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Critical && e.Exception is TypeLoadException);
        }

        [Fact]
        public void HandleDomainUnhandledException_NonExceptionPayload_IsIgnored()
        {
            var reporter = CreateReporter();

            reporter.HandleDomainUnhandledException(null, new UnhandledExceptionEventArgs("not an exception", isTerminating: false));

            Assert.Empty(_messageBoxes.Messages);
            Assert.Empty(_logger.Entries);
        }

        [Fact]
        public void CrashLog_AppendsEntryWithSourceAndStack()
        {
            var reporter = CreateReporter();

            reporter.OnDispatcherException(new InvalidOperationException("boom"));

            var content = File.ReadAllText(Path.Combine(_crashLogDirectory, "crash.log"));
            Assert.Contains("Unhandled exception on the UI dispatcher", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("boom", content);
        }

        [Fact]
        public void CrashLog_TruncatesToTailWhenOverLimit()
        {
            var path = Path.Combine(_crashLogDirectory, "crash.log");
            var bigLine = "X";
            var filler = new string('A', 1100 * 1024);
            File.WriteAllText(path, filler + "\n" + bigLine + "\n");

            var reporter = CreateReporter();
            reporter.OnDispatcherException(new InvalidOperationException("boom"));

            var content = File.ReadAllText(path);
            Assert.DoesNotContain(filler, content);
            Assert.Contains(bigLine, content);
            Assert.Contains("boom", content);
        }

        [Fact]
        public void WithoutMessageBox_NoDialogIsAttempted()
        {
            var reporter = new UnhandledExceptionReporter(new ImmediateDispatcher(), _logger, messageBoxes: null, _crashLogDirectory);

            var handled = reporter.OnDispatcherException(new InvalidOperationException("boom"));

            Assert.True(handled);
            Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
        }

        private sealed class RecordingMessageBox : IMessageBoxService
        {
            private readonly object _gate = new();

            public List<string> Messages { get; } = new();

            public Task<DialogResult> ShowAsync(string message, string title, DialogButton button, DialogIcon icon)
            {
                lock (_gate)
                {
                    Messages.Add(message);
                }

                return Task.FromResult(DialogResult.Ok);
            }
        }

        private sealed class CapturingLogger : ILogger<UnhandledExceptionReporter>
        {
            private readonly object _gate = new();

            public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_gate)
                {
                    Entries.Add((logLevel, formatter(state, exception), exception));
                }
            }
        }
    }
}
