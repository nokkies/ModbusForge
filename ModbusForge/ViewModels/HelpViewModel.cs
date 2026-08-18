using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public class HelpTopic
    {
        public string TopicId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    public sealed partial class HelpViewModel : ObservableObject
    {
        private readonly IHelpContentService _helpContentService;
        private readonly ILogger<HelpViewModel> _logger;

        /// <summary>
        /// The complete help topic list - the single source of truth for both the
        /// navigation panel and the search filter. The filter previously kept its own
        /// copy of the list, which had drifted (mcp-server was missing from it, so
        /// "API &amp; MCP Server" could never be found by searching).
        /// </summary>
        private static IReadOnlyList<HelpTopic> AllTopics { get; } = new[]
        {
            new HelpTopic { TopicId = "getting-started", Title = "Getting Started" },
            new HelpTopic { TopicId = "connection-manager", Title = "Connection Manager" },
            new HelpTopic { TopicId = "device-scanner", Title = "Device Scanner" },
            new HelpTopic { TopicId = "script-editor", Title = "Script Editor" },
            new HelpTopic { TopicId = "custom-data", Title = "Custom Data Tab" },
            new HelpTopic { TopicId = "trends", Title = "Trend & Logging" },
            new HelpTopic { TopicId = "visual-editor", Title = "Visual Node Editor" },
            new HelpTopic { TopicId = "preferences", Title = "Preferences" },
            new HelpTopic { TopicId = "mcp-server", Title = "API & MCP Server" },
            new HelpTopic { TopicId = "mqtt", Title = "MQTT Gateway" },
            new HelpTopic { TopicId = "keyboard-shortcuts", Title = "Keyboard Shortcuts" },
            new HelpTopic { TopicId = "partial-reads", Title = "Partial or Chunked Reads" },
            new HelpTopic { TopicId = "troubleshooting", Title = "Troubleshooting" }
        };

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<HelpTopic> _helpTopics = new();

        [ObservableProperty]
        private string? _helpContent;

        public HelpViewModel(IHelpContentService helpContentService, ILogger<HelpViewModel> logger)
        {
            _helpContentService = helpContentService;
            _logger = logger;
            LoadHelpTopics();
            LoadDefaultTopic();
        }

        private void LoadHelpTopics()
        {
            HelpTopics = new ObservableCollection<HelpTopic>(AllTopics);
        }

        private void LoadDefaultTopic()
        {
            LoadTopic("getting-started");
        }

        [RelayCommand]
        private void Navigate(string? topicId)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(topicId))
                {
                    LoadTopic(topicId);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Navigation error: {Message}", ex.Message);
                LoadTopic("getting-started");
            }
        }

        private void LoadTopic(string topicId)
        {
            try
            {
                HelpContent = _helpContentService.GetHelpContent(topicId);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Load topic error: {Message}", ex.Message);
                HelpContent = _helpContentService.GetHelpContent("troubleshooting");
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            FilterHelpTopics(value);
        }

        private void FilterHelpTopics(string searchText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    LoadHelpTopics();
                    return;
                }

                var filtered = AllTopics.Where(t =>
                    t.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    t.TopicId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                HelpTopics = new ObservableCollection<HelpTopic>(filtered);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Search error: {Message}", ex.Message);
                LoadHelpTopics();
            }
        }
    }
}
