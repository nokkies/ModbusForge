using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Avalonia.Views;
using ModbusForge.Avalonia.Services;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// View-agnostic abstraction for the lightweight Avalonia dock/float manager.
    /// </summary>
    public interface IDockingHost
    {
        void SetMainView(MainView mainView);
        void ShowTagBrowser();
        void ShowWatchWindow();
        void ShowConnectionManager();
    }

    /// <summary>
    /// Lightweight contract implemented by tool windows that can be docked into
    /// the main <see cref="TabControl"/> or floated back to a <see cref="Window"/>.
    /// </summary>
    public interface IDockableTool
    {
        Action? ToggleDockCallback { get; set; }
        void SetDocked(bool isDocked);
    }

    /// <summary>
    /// Avalonia implementation of a lightweight tear-off/dock manager.
    /// Opens Tag Browser, Watch, and Connection Manager as <see cref="Window"/>
    /// instances that can be re-docked into the main view's tab control.
    /// </summary>
    public sealed class AvaloniaDockingHost : IDockingHost
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private readonly TagService _tagService;
        private readonly IRegisterTemplateImportService _registerTemplateImportService;
        private readonly IRegisterTemplateStore _registerTemplateStore;
        private readonly IFileDialogService _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly IMessageBoxService _messageBoxService;
        private readonly ILogger<ConnectionManagerViewModel>? _connectionManagerLogger;
        private readonly ILogger<AvaloniaDockingHost>? _logger;

        private MainView? _mainView;
        private Window? _mainWindow;
        private TabControl? _mainTabControl;
        private readonly Dictionary<string, DockedTool> _tools = new(StringComparer.Ordinal);

        private sealed class DockedTool
        {
            public string Id = string.Empty;
            public string Title = string.Empty;
            public Window Window = null!;
            public Control Content = null!;
            public object ViewModel = null!;
            public TabItem? Tab;
            public bool IsDocked;
        }

        public AvaloniaDockingHost(
            IConnectionManager connectionManager,
            IDispatcher dispatcher,
            TagService tagService,
            IRegisterTemplateImportService registerTemplateImportService,
            IRegisterTemplateStore registerTemplateStore,
            IFileDialogService fileDialogService,
            IFileSystem fileSystem,
            IMessageBoxService messageBoxService,
            ILogger<ConnectionManagerViewModel>? connectionManagerLogger = null,
            ILogger<AvaloniaDockingHost>? logger = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _registerTemplateImportService = registerTemplateImportService ?? throw new ArgumentNullException(nameof(registerTemplateImportService));
            _registerTemplateStore = registerTemplateStore ?? throw new ArgumentNullException(nameof(registerTemplateStore));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _messageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
            _connectionManagerLogger = connectionManagerLogger;
            _logger = logger;
        }

        public void SetMainView(MainView mainView)
        {
            _mainView = mainView ?? throw new ArgumentNullException(nameof(mainView));
            _mainWindow = TopLevel.GetTopLevel(mainView) as Window;
            _mainTabControl = mainView.FindControl<TabControl>("MainTabControl");

            if (_mainTabControl == null)
            {
                _logger?.LogWarning("AvaloniaDockingHost could not locate the main TabControl.");
            }
        }

        public void ShowTagBrowser() => ShowTool("TagBrowser", CreateTagBrowser);

        public void ShowWatchWindow() => ShowTool("Watch", CreateWatch);

        public void ShowConnectionManager() => ShowTool("ConnectionManager", CreateConnectionManager);

        public void ToggleDock(string id)
        {
            if (!_tools.TryGetValue(id, out var tool))
            {
                return;
            }

            if (tool.IsDocked)
            {
                FloatTool(tool);
            }
            else
            {
                DockTool(tool);
            }
        }

        private void DockTool(DockedTool tool)
        {
            if (_mainTabControl == null)
            {
                return;
            }

            tool.Window.Hide();

            var content = tool.Window.Content as Control;
            tool.Window.Content = null;

            if (content != null)
            {
                content.DataContext = tool.ViewModel;

                var tab = new TabItem
                {
                    Header = CreateTabHeader(tool),
                    Content = content
                };

                tool.Tab = tab;
                _mainTabControl.Items.Add(tab);
                _mainTabControl.SelectedItem = tab;
            }

            tool.IsDocked = true;

            if (tool.Window is IDockableTool dockable)
            {
                dockable.SetDocked(true);
            }
        }

        private void FloatTool(DockedTool tool)
        {
            if (_mainTabControl == null)
            {
                return;
            }

            if (tool.Tab != null)
            {
                var content = tool.Tab.Content as Control;
                tool.Tab.Content = null;
                _mainTabControl.Items.Remove(tool.Tab);
                tool.Tab = null;

                if (content != null)
                {
                    content.DataContext = tool.ViewModel;
                    tool.Window.Content = content;
                }
            }

            tool.IsDocked = false;

            if (tool.Window is IDockableTool dockable)
            {
                dockable.SetDocked(false);
            }

            if (_mainWindow != null)
            {
                tool.Window.Show(_mainWindow);
            }
            else
            {
                tool.Window.Show();
            }

            tool.Window.Activate();
        }

        private void CloseTool(string id)
        {
            if (!_tools.TryGetValue(id, out var tool))
            {
                return;
            }

            if (tool.IsDocked && tool.Tab != null)
            {
                tool.Tab.Content = null;
                _mainTabControl?.Items.Remove(tool.Tab);
                tool.Tab = null;
            }

            _tools.Remove(id);

            try
            {
                tool.Window.Close();
            }
            catch (InvalidOperationException)
            {
                // Window may already be closing.
            }
        }

        private void OnToolWindowClosed(string id)
        {
            if (!_tools.TryGetValue(id, out var tool))
            {
                return;
            }

            if (tool.IsDocked && tool.Tab != null)
            {
                tool.Tab.Content = null;
                _mainTabControl?.Items.Remove(tool.Tab);
            }

            _tools.Remove(id);
        }

        private void ShowTool(string id, Func<DockedTool> factory)
        {
            if (_mainView == null)
            {
                _logger?.LogWarning("Docking host has no main view; cannot show tool {ToolId}.", id);
                return;
            }

            if (_tools.TryGetValue(id, out var existing))
            {
                if (existing.IsDocked && existing.Tab != null)
                {
                    _mainTabControl!.SelectedItem = existing.Tab;
                }
                else
                {
                    ShowWindow(existing.Window);
                    existing.Window.Activate();
                }

                return;
            }

            var tool = factory();

            if (tool.Window is IDockableTool dockable)
            {
                dockable.ToggleDockCallback = () => ToggleDock(tool.Id);
            }

            tool.Window.Closed += (_, _) => OnToolWindowClosed(tool.Id);

            // Preserve the tool's view model when the content is re-parented into a tab.
            tool.Content.DataContext = tool.ViewModel;

            _tools[id] = tool;
            ShowWindow(tool.Window);

            if (tool.Window is IDockableTool dockableForShow)
            {
                dockableForShow.SetDocked(false);
            }
        }

        private DockedTool CreateTagBrowser()
        {
            var viewModel = new TagBrowserViewModel(
                _tagService,
                _registerTemplateImportService,
                _registerTemplateStore,
                _fileDialogService,
                _fileSystem,
                _messageBoxService,
                logger: null,
                selectionMode: false);

            var window = new TagBrowserWindow(viewModel);
            var content = window.Content as Control
                ?? throw new InvalidOperationException("TagBrowserWindow content is not a Control.");

            _ = viewModel.InitializeAsync();

            return new DockedTool
            {
                Id = "TagBrowser",
                Title = window.Title ?? "Tag Browser",
                Window = window,
                Content = content,
                ViewModel = viewModel
            };
        }

        private DockedTool CreateWatch()
        {
            var viewModel = new WatchViewModel(
                _tagService,
                _messageBoxService,
                logger: null,
                _connectionManager,
                _dispatcher);

            var window = new WatchWindow(
                viewModel,
                _registerTemplateImportService,
                _registerTemplateStore,
                _fileDialogService,
                _fileSystem,
                _messageBoxService);

            var content = window.Content as Control
                ?? throw new InvalidOperationException("WatchWindow content is not a Control.");

            _ = viewModel.InitializeAsync();

            return new DockedTool
            {
                Id = "Watch",
                Title = window.Title ?? "Watch Window",
                Window = window,
                Content = content,
                ViewModel = viewModel
            };
        }

        private DockedTool CreateConnectionManager()
        {
            var viewModel = new ConnectionManagerViewModel(_connectionManager, _dispatcher, _messageBoxService, _connectionManagerLogger);
            var window = new ConnectionManagerWindow { DataContext = viewModel };

            var content = window.Content as Control
                ?? throw new InvalidOperationException("ConnectionManagerWindow content is not a Control.");

            return new DockedTool
            {
                Id = "ConnectionManager",
                Title = window.Title ?? "Connection Manager",
                Window = window,
                Content = content,
                ViewModel = viewModel
            };
        }

        private Control CreateTabHeader(DockedTool tool)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };

            panel.Children.Add(new TextBlock
            {
                Text = tool.Title,
                VerticalAlignment = VerticalAlignment.Center
            });

            var closeButton = new Button
            {
                Content = "\u2715",
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var toolId = tool.Id;
            closeButton.Click += (_, _) => CloseTool(toolId);

            panel.Children.Add(closeButton);

            return panel;
        }

        private void ShowWindow(Window window)
        {
            if (_mainWindow != null)
            {
                window.Show(_mainWindow);
            }
            else
            {
                window.Show();
            }
        }
    }
}
