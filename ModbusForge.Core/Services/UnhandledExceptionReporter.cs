using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModbusForge.Services
{
    /// <summary>
    /// Central sink for exceptions that escape the application's own error handling.
    ///
    /// Policy: report, never disappear, and keep the user's session when it is safe to.
    /// <list type="bullet">
    /// <item>Every reported exception is logged (full stack) and appended to a crash
    /// log file next to the settings, so a packaged build leaves evidence even though
    /// it has no console.</item>
    /// <item>UI dispatcher exceptions surface a dialog and the application keeps
    /// running: the faults that reach this sink are typically binding or render
    /// handlers, and dropping the user's live session (connections, scripts, trends)
    /// over one of them costs more than the bug.</item>
    /// <item>Faults in unobserved tasks are logged without a dialog: they are usually
    /// garbage-collection-time noise from an abandoned task and interrupting the user
    /// is worse than the fault.</item>
    /// <item><see cref="OutOfMemoryException"/> and <see cref="StackOverflowException"/>
    /// are never swallowed: the process is about to die anyway and masking that would
    /// hide a real resource problem.</item>
    /// <item>Repeating the same exception within a short window logs again but does
    /// not re-show the dialog, so a fault that re-triggers on every UI pass cannot
    /// turn into a dialog loop.</item>
    /// </list>
    /// </summary>
    public sealed class UnhandledExceptionReporter
    {
        private const int CrashLogMaxBytes = 1024 * 1024;
        private const int CrashLogKeepBytes = 512 * 1024;
        private static readonly TimeSpan DialogCooldown = TimeSpan.FromSeconds(5);

        private readonly ILogger<UnhandledExceptionReporter> _logger;
        private readonly IDispatcher? _dispatcher;
        private readonly IMessageBoxService? _messageBoxes;
        private readonly string _crashLogDirectory;

        private readonly object _dialogGate = new();
        private string? _lastDialogSignature;
        private DateTime _lastDialogAtUtc;

        /// <param name="dispatcher">
        /// Used to marshal the error dialog to the UI thread. Optional for
        /// headless hosts, which have no UI and log only.
        /// </param>
        public UnhandledExceptionReporter(
            IDispatcher? dispatcher = null,
            ILogger<UnhandledExceptionReporter>? logger = null,
            IMessageBoxService? messageBoxes = null,
            string? crashLogDirectory = null)
        {
            _dispatcher = dispatcher;
            _logger = logger ?? NullLogger<UnhandledExceptionReporter>.Instance;
            _messageBoxes = messageBoxes;
            _crashLogDirectory = crashLogDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ModbusForge");
        }

        /// <summary>
        /// Subscribes to the process-wide unhandled-exception sources.
        /// UI dispatcher exceptions must be wired to <see cref="OnDispatcherException"/>
        /// by the host (the desktop lifetime owns that event).
        /// </summary>
        public void Attach()
        {
            AppDomain.CurrentDomain.UnhandledException += HandleDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        }

        /// <summary>
        /// Unsubscribes from the process-wide unhandled-exception sources.
        /// </summary>
        public void Detach()
        {
            AppDomain.CurrentDomain.UnhandledException -= HandleDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
        }

        /// <summary>
        /// Handles an exception raised on the UI dispatcher.
        /// Returns <see langword="true"/> when the application can safely continue
        /// (the host should mark the dispatcher event handled in that case).
        /// </summary>
        public bool OnDispatcherException(Exception exception)
        {
            if (exception is null)
            {
                return true;
            }

            if (IsFatal(exception))
            {
                _logger.LogCritical(exception, "Fatal unhandled exception on the UI dispatcher");
                AppendCrashLog("Fatal unhandled exception on the UI dispatcher", exception);
                return false;
            }

            _logger.LogError(exception, "Unhandled exception on the UI dispatcher");
            AppendCrashLog("Unhandled exception on the UI dispatcher", exception);
            _ = ShowDialogAsync(exception);
            return true;
        }

        /// <summary>
        /// Handles a faulted task that was never observed. On .NET this event is
        /// informational only, so the fault is logged and crash-logged.
        /// </summary>
        public void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
        {
            var exception = eventArgs.Exception is null ? null : Unwrap(eventArgs.Exception);
            if (exception is null)
            {
                return;
            }

            if (IsFatal(exception))
            {
                return;
            }

            // On .NET this event is informational only (unobserved task faults never
            // terminate the process), so logging is the complete response.
            _logger.LogError(exception, "Unhandled exception in an unobserved task");
            AppendCrashLog("Unhandled exception in an unobserved task", exception);
        }

        /// <summary>
        /// Handles an unhandled AppDomain exception. Terminating faults are logged
        /// only; non-terminating ones are also reported to the user.
        /// </summary>
        public void HandleDomainUnhandledException(object? sender, UnhandledExceptionEventArgs eventArgs)
        {
            if (eventArgs.ExceptionObject is not Exception exception)
            {
                return;
            }

            if (eventArgs.IsTerminating)
            {
                _logger.LogCritical(exception, "Unhandled terminating exception in the application domain");
                AppendCrashLog("Unhandled terminating exception in the application domain", exception);
                return;
            }

            _logger.LogError(exception, "Unhandled exception in the application domain");
            AppendCrashLog("Unhandled exception in the application domain", exception);
            _ = ShowDialogAsync(exception);
        }

        private Task ShowDialogAsync(Exception exception)
        {
            if (_messageBoxes is null || _dispatcher is null)
            {
                return Task.CompletedTask;
            }

            var signature = SignatureOf(exception);
            var now = DateTime.UtcNow;
            bool show;
            lock (_dialogGate)
            {
                show = !string.Equals(signature, _lastDialogSignature, StringComparison.Ordinal)
                    || now - _lastDialogAtUtc >= DialogCooldown;
                if (!show)
                {
                    return Task.CompletedTask;
                }

                _lastDialogSignature = signature;
                _lastDialogAtUtc = now;
            }

            var message = $"An unexpected error occurred.\n\n" +
                          $"{exception.GetType().Name}: {exception.Message}\n\n" +
                          "You can keep using ModbusForge; the application has recovered.\n" +
                          "Full details were appended to the crash log.";

            try
            {
                return _dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await _messageBoxes.ShowAsync(message, "ModbusForge", DialogButton.Ok, DialogIcon.Error);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        _logger.LogError(ex, "Failed to show the error dialog");
                    }
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Failed to marshal the error dialog to the UI thread");
                return Task.CompletedTask;
            }
        }

        private static bool IsFatal(Exception exception)
            => exception is OutOfMemoryException or StackOverflowException;

        /// <summary>
        /// Reduces the aggregate the runtime supplies to the actual fault: a
        /// single-fault aggregate (the shape Task.Exception produces) is unwrapped
        /// to its inner exception; multi-fault aggregates are kept as-is.
        /// </summary>
        private static Exception Unwrap(Exception exception)
        {
            if (exception is not AggregateException aggregate)
            {
                return exception;
            }

            var flattened = aggregate.Flatten();
            if (flattened is AggregateException flattenedAggregate && flattenedAggregate.InnerExceptions.Count == 1)
            {
                return flattenedAggregate.InnerExceptions[0];
            }

            return flattened;
        }

        private static string SignatureOf(Exception exception)
        {
            var innermost = exception;
            while (innermost.InnerException is not null)
            {
                innermost = innermost.InnerException;
            }

            return innermost.GetType().FullName + ": " + innermost.Message;
        }

        private void AppendCrashLog(string source, Exception exception)
        {
            try
            {
                Directory.CreateDirectory(_crashLogDirectory);
                var path = Path.Combine(_crashLogDirectory, "crash.log");

                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    if (info.Length > CrashLogMaxBytes)
                    {
                        var all = File.ReadAllBytes(path);
                        var tail = new byte[CrashLogKeepBytes];
                        Buffer.BlockCopy(all, all.Length - CrashLogKeepBytes, tail, 0, CrashLogKeepBytes);
                        var text = Encoding.UTF8.GetString(tail);
                        var firstNewline = text.IndexOf('\n');
                        if (firstNewline >= 0)
                        {
                            text = text[(firstNewline + 1)..];
                        }

                        File.WriteAllText(path, text);
                    }
                }

                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}\n{exception}\n\n");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Failed to write the crash log");
            }
        }
    }
}
