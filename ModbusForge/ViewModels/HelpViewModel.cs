using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        public HelpViewModel(IHelpContentService helpContentService)
        {
            _helpContentService = helpContentService;
            LoadHelpTopics();
            LoadDefaultTopic();
        }

        private void LoadHelpTopics()
        {
            HelpTopics = new ObservableCollection<HelpTopic>
            {
                new HelpTopic { TopicId = "getting-started", Title = "Getting Started", Icon = "🚀" },
                new HelpTopic { TopicId = "connection-manager", Title = "Connection Manager", Icon = "🔗" },
                new HelpTopic { TopicId = "script-editor", Title = "Script Editor", Icon = "📝" },
                new HelpTopic { TopicId = "custom-data", Title = "Custom Data Tab", Icon = "📊" },
                new HelpTopic { TopicId = "trends", Title = "Trend & Logging", Icon = "📈" },
                new HelpTopic { TopicId = "visual-editor", Title = "Visual Node Editor", Icon = "🎨" },
                new HelpTopic { TopicId = "preferences", Title = "Preferences", Icon = "⚙️" },
                new HelpTopic { TopicId = "keyboard-shortcuts", Title = "Keyboard Shortcuts", Icon = "⌨️" },
                new HelpTopic { TopicId = "troubleshooting", Title = "Troubleshooting", Icon = "🔧" }
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load topic error: {ex.Message}");
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
                    new HelpTopic { TopicId = "getting-started", Title = "Getting Started", Icon = "🚀" },
                    new HelpTopic { TopicId = "connection-manager", Title = "Connection Manager", Icon = "🔗" },
                    new HelpTopic { TopicId = "script-editor", Title = "Script Editor", Icon = "📝" },
                    new HelpTopic { TopicId = "custom-data", Title = "Custom Data Tab", Icon = "📊" },
                    new HelpTopic { TopicId = "trends", Title = "Trend & Logging", Icon = "📈" },
                    new HelpTopic { TopicId = "visual-editor", Title = "Visual Node Editor", Icon = "🎨" },
                    new HelpTopic { TopicId = "preferences", Title = "Preferences", Icon = "⚙️" },
                    new HelpTopic { TopicId = "keyboard-shortcuts", Title = "Keyboard Shortcuts", Icon = "⌨️" },
                    new HelpTopic { TopicId = "troubleshooting", Title = "Troubleshooting", Icon = "🔧" }
                };

                var filtered = allTopics.Where(t =>
                    t.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    t.TopicId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                HelpTopics = new ObservableCollection<HelpTopic>(filtered);
            }
            catch (Exception ex)
            {
                // Log error and reset to all topics
                System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
                LoadHelpTopics();
            }
        }
    }
}
