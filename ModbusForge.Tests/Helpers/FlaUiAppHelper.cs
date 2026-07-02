using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace ModbusForge.Tests.Helpers;

public class FlaUiAppHelper : IDisposable
{
    private readonly Application? _app;
    private readonly UIA3Automation _automation;
    private readonly string _appPath;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(15);
    private bool _disposed;

    public FlaUiAppHelper(string appPath)
    {
        _appPath = appPath;
        _automation = new UIA3Automation();
        _app = Application.Launch(appPath);
    }

    public static FlaUiAppHelper LaunchFromProject()
    {
        var solutionDir = GetSolutionDirectory();
        var appPath = Path.Combine(solutionDir, "ModbusForge", "bin", "Debug", "net8.0-windows", "ModbusForge.exe");

        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException($"ModbusForge.exe not found at {appPath}. Build the project first.");
        }

        return new FlaUiAppHelper(appPath);
    }

    public Window? GetMainWindow()
    {
        return _app?.GetMainWindow(_automation, _defaultTimeout);
    }

    public Window? FindWindowByTitle(string title)
    {
        return _automation.GetDesktop().FindFirstDescendant(cf => cf.ByName(title))?.AsWindow();
    }

    public Window WaitForWindowByTitle(string title, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? _defaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            var window = FindWindowByTitle(title);
            if (window != null)
            {
                return window;
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException($"Window with title '{title}' did not appear within {effectiveTimeout.TotalSeconds} seconds.");
    }

    public Window GetMainWindowOrThrow()
    {
        return GetMainWindow()
            ?? throw new InvalidOperationException("Main window not found.");
    }

    public void ClickMenuItem(Window window, params string[] menuPath)
    {
        if (menuPath.Length == 0)
        {
            throw new ArgumentException("Menu path must contain at least one item.");
        }

        // Find the menu bar
        var menuBar = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuBar));
        if (menuBar == null)
        {
            // Try to find the top-level menu item anywhere in the window
            menuBar = window;
        }

        var currentMenu = menuBar.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem).And(cf.ByName(menuPath[0])))?.AsMenuItem();
        if (currentMenu == null)
        {
            throw new InvalidOperationException($"Menu item '{menuPath[0]}' not found.");
        }

        for (int i = 1; i < menuPath.Length; i++)
        {
            currentMenu.Click();
            Thread.Sleep(150);

            // Submenus appear in a popup under the desktop or main window
            var nextMenu = WaitForElement(() =>
            {
                var searchScope = new[] { _automation.GetDesktop(), window }.Distinct();
                foreach (var scope in searchScope)
                {
                    var candidates = scope.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem));
                    foreach (var candidate in candidates)
                    {
                        var menuItem = candidate.AsMenuItem();
                        if (menuItem != null && (menuItem.Name == menuPath[i] || menuItem.Name.Replace("_", "") == menuPath[i]))
                        {
                            return menuItem;
                        }
                    }
                }
                return null;
            }, TimeSpan.FromSeconds(3));

            currentMenu = nextMenu ?? throw new InvalidOperationException($"Menu item '{menuPath[i]}' not found under '{menuPath[i - 1]}'.");
        }

        currentMenu.Click();
    }

    private T? WaitForElement<T>(Func<T?> findFunc, TimeSpan timeout) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var result = findFunc();
            if (result != null)
                return result;
            Thread.Sleep(100);
        }
        return null;
    }

    public AutomationElement? FindElementByName(Window window, string name)
    {
        return window.FindFirstDescendant(cf => cf.ByName(name));
    }

    public AutomationElement WaitForElementByName(Window window, string name, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? _defaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            var element = FindElementByName(window, name);
            if (element != null)
            {
                return element;
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException($"Element with name '{name}' did not appear within {effectiveTimeout.TotalSeconds} seconds.");
    }

    public void CloseWindow(Window window)
    {
        try
        {
            var closeButton = window.FindFirstDescendant(cf => cf.ByAutomationId("Close"))?.AsButton();
            closeButton?.Click();
        }
        catch
        {
            // Fallback: try to close via pattern
            window.Patterns.Window.Pattern.Close();
        }
    }

    public static string GetSolutionDirectory()
    {
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;

        while (!Directory.GetFiles(currentDir, "*.sln").Any() && Directory.GetParent(currentDir) != null)
        {
            currentDir = Directory.GetParent(currentDir)!.FullName;
        }

        return currentDir;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_app != null)
            {
                _app.Close();
                _app.Dispose();
            }
        }
        catch
        {
            // Best effort cleanup
        }

        _automation.Dispose();
    }
}
