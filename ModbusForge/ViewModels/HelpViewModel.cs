using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    public partial class HelpTopic
    {
        public string TopicId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    public partial class HelpViewModel : ViewModelBase
    {
        private readonly IHelpContentService _helpContentService;
        private readonly ILogger<HelpViewModel> _logger;
        private FlowDocument? _helpContent;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<HelpTopic> _helpTopics = new();

        public FlowDocument? HelpContent
        {
            get => _helpContent;
            private set => SetProperty(ref _helpContent, value);
        }

        public HelpViewModel(IHelpContentService helpContentService, ILogger<HelpViewModel> logger)
        {
            _helpContentService = helpContentService;
            _logger = logger;
            LoadHelpTopics();
            LoadDefaultTopic();
        }

        private void LoadHelpTopics()
        {
            HelpTopics = new ObservableCollection<HelpTopic>
            {
                new HelpTopic { TopicId = "getting-started", Title = "Getting Started" },
                new HelpTopic { TopicId = "connection-manager", Title = "Connection Manager" },
                new HelpTopic { TopicId = "script-editor", Title = "Script Editor" },
                new HelpTopic { TopicId = "custom-data", Title = "Custom Data Tab" },
                new HelpTopic { TopicId = "trends", Title = "Trend & Logging" },
                new HelpTopic { TopicId = "visual-editor", Title = "Visual Node Editor" },
                new HelpTopic { TopicId = "preferences", Title = "Preferences" },
                new HelpTopic { TopicId = "keyboard-shortcuts", Title = "Keyboard Shortcuts" },
                new HelpTopic { TopicId = "troubleshooting", Title = "Troubleshooting" }
            };
        }

        private void LoadDefaultTopic()
        {
            LoadTopic("getting-started");
        }

        [RelayCommand]
        private void Navigate(string topicId)
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
                // Load default topic on error
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
                // Show error message
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

                var allTopics = new List<HelpTopic>
                {
                    new HelpTopic { TopicId = "getting-started", Title = "Getting Started" },
                    new HelpTopic { TopicId = "connection-manager", Title = "Connection Manager" },
                    new HelpTopic { TopicId = "script-editor", Title = "Script Editor" },
                    new HelpTopic { TopicId = "custom-data", Title = "Custom Data Tab" },
                    new HelpTopic { TopicId = "trends", Title = "Trend & Logging" },
                    new HelpTopic { TopicId = "visual-editor", Title = "Visual Node Editor" },
                    new HelpTopic { TopicId = "preferences", Title = "Preferences" },
                    new HelpTopic { TopicId = "keyboard-shortcuts", Title = "Keyboard Shortcuts" },
                    new HelpTopic { TopicId = "troubleshooting", Title = "Troubleshooting" }
                };

                var filtered = allTopics.Where(t =>
                    t.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    t.TopicId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                HelpTopics = new ObservableCollection<HelpTopic>(filtered);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                // Log error and reset to all topics
                _logger.LogError(ex, "Search error: {Message}", ex.Message);
                LoadHelpTopics();
            }
        }
    }
}
