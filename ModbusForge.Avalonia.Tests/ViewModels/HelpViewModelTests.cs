using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    public class HelpViewModelTests
    {
        private static HelpViewModel CreateViewModel()
        {
            var helpService = new HelpContentService(NullLogger<HelpContentService>.Instance);
            var viewModel = new HelpViewModel(helpService, NullLogger<HelpViewModel>.Instance);
            return viewModel;
        }

        [Fact]
        public void Search_FindsTheMcpServerTopic()
        {
            // Regression: the search filter once kept its own topic list without
            // mcp-server, so "API & MCP Server" could never be found by searching.
            var viewModel = CreateViewModel();

            viewModel.SearchText = "MCP";

            var ids = viewModel.HelpTopics.Select(t => t.TopicId).ToList();
            Assert.Contains("mcp-server", ids);
        }

        [Fact]
        public void Search_FindsEveryTopicInTheNavigationList()
        {
            // Every topic the user can click must also be reachable by search.
            var viewModel = CreateViewModel();
            var allIds = viewModel.HelpTopics.Select(t => t.TopicId).ToList();
            Assert.NotEmpty(allIds);

            foreach (var id in allIds)
            {
                viewModel.SearchText = id;
                // Every topic in the navigation list must be findable by search.
                Assert.Contains(id, viewModel.HelpTopics.Select(t => t.TopicId));
            }

            viewModel.SearchText = string.Empty;
        }

        [Fact]
        public void Search_ClearingTheSearchRestoresAllTopics()
        {
            var viewModel = CreateViewModel();
            var fullCount = viewModel.HelpTopics.Count;

            viewModel.SearchText = "troubleshooting";
            Assert.Single(viewModel.HelpTopics);

            viewModel.SearchText = string.Empty;
            Assert.Equal(fullCount, viewModel.HelpTopics.Count);
        }

        [Fact]
        public void Navigate_UnknownTopic_ShowsNotFoundContent()
        {
            var viewModel = CreateViewModel();

            viewModel.NavigateCommand.Execute("does-not-exist");

            Assert.Contains("Not Found", viewModel.HelpContent);
        }

        [Fact]
        public void DefaultTopic_IsGettingStarted()
        {
            var viewModel = CreateViewModel();

            Assert.Contains("Getting Started", viewModel.HelpContent);
        }
    }
}
