using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    public class ScriptRulesViewModelTests
    {
        private static (ScriptRulesViewModel vm, FakeRuleService service) CreateViewModel(
            FakeFileDialogService? dialogService = null,
            FakeFileSystem? fileSystem = null,
            FakeMessageBoxService? messageBoxService = null)
        {
            var service = new FakeRuleService();
            var vm = new ScriptRulesViewModel(
                service,
                new SyncDispatcher(),
                NullLogger<ScriptRulesViewModel>.Instance,
                dialogService,
                fileSystem,
                messageBoxService);
            return (vm, service);
        }

        [Fact]
        public void AddRuleCommand_AddsRuleWithUniqueName_AndSelectsIt()
        {
            var (vm, service) = CreateViewModel();

            vm.AddRuleCommand.Execute(null);
            vm.AddRuleCommand.Execute(null);

            Assert.Equal(2, service.Rules.Count);
            Assert.Equal("Rule 1", service.Rules[0].Name);
            Assert.Equal("Rule 2", service.Rules[1].Name);
            Assert.Same(service.Rules[1], vm.SelectedRule);
            Assert.True(vm.HasSelectedRule);
            Assert.Equal("Added rule Rule 2", vm.StatusText);
        }

        [Fact]
        public void AddRuleCommand_CreatesUniqueName_WhenCandidateCollides()
        {
            var (vm, service) = CreateViewModel();

            // Two rules, and the second one was renamed to "Rule 3", which is
            // the name the next Add would naturally choose.
            service.Rules.Add(new ScriptRule { Name = "Rule 1" });
            service.Rules.Add(new ScriptRule { Name = "Rule 3" });

            vm.AddRuleCommand.Execute(null);

            var added = service.Rules.Last();
            Assert.Equal("Rule 3 2", added.Name);
        }

        [Fact]
        public void RemoveSelectedCommand_RemovesRule_AndSelectsRemaining()
        {
            var (vm, service) = CreateViewModel();
            vm.AddRuleCommand.Execute(null);
            var second = new ScriptRule { Name = "Second" };
            service.Rules.Add(second);
            vm.SelectedRule = service.Rules[0];

            Assert.True(vm.RemoveSelectedCommand.CanExecute(null));
            vm.RemoveSelectedCommand.Execute(null);

            Assert.Single(service.Rules);
            Assert.Same(second, vm.SelectedRule);
            Assert.Equal("Removed rule Rule 1", vm.StatusText);

            vm.RemoveSelectedCommand.Execute(null);
            Assert.Empty(service.Rules);
            Assert.Null(vm.SelectedRule);
            Assert.False(vm.HasSelectedRule);
            Assert.False(vm.RemoveSelectedCommand.CanExecute(null));
        }

        [Fact]
        public async Task ClearAllCommand_ClearsService_WhenUserConfirms()
        {
            var (vm, service) = CreateViewModel(messageBoxService: new FakeMessageBoxService { Result = DialogResult.Yes });
            vm.AddRuleCommand.Execute(null);
            vm.AddRuleCommand.Execute(null);

            await vm.ClearAllCommand.ExecuteAsync(null);

            Assert.Empty(service.Rules);
            Assert.Null(vm.SelectedRule);
            Assert.Equal("All rules removed", vm.StatusText);
        }

        [Fact]
        public async Task ClearAllCommand_KeepsRules_WhenUserDeclines()
        {
            var (vm, service) = CreateViewModel(messageBoxService: new FakeMessageBoxService { Result = DialogResult.No });
            vm.AddRuleCommand.Execute(null);

            await vm.ClearAllCommand.ExecuteAsync(null);

            Assert.Single(service.Rules);
            Assert.Equal("Clear canceled", vm.StatusText);
        }

        [Fact]
        public async Task SaveThenLoad_RoundTripsRules()
        {
            var fileSystem = new FakeFileSystem();
            var (vm, service) = CreateViewModel(
                dialogService: new FakeFileDialogService
                {
                    SavePath = "rules-save.json",
                    OpenPath = "rules-load.json"
                },
                fileSystem: fileSystem,
                messageBoxService: new FakeMessageBoxService { Result = DialogResult.Yes });

            vm.AddRuleCommand.Execute(null);
            var rule = service.Rules[0];
            rule.TriggerValue = "42";
            rule.OneTime = true;

            await vm.SaveRulesCommand.ExecuteAsync(null);
            Assert.False(string.IsNullOrEmpty(fileSystem.Files["rules-save.json"]));
            Assert.Contains("\"Name\"", fileSystem.Files["rules-save.json"]);

            // Replace the current rules with different ones from the loaded file.
            fileSystem.Files["rules-load.json"] = "[{\"Name\":\"Loaded rule\",\"TriggerValue\":\"7\"}]";
            await vm.LoadRulesCommand.ExecuteAsync(null);

            Assert.Single(service.Rules);
            Assert.Equal("Loaded rule", service.Rules[0].Name);
            Assert.Equal("7", service.Rules[0].TriggerValue);
            Assert.Same(service.Rules[0], vm.SelectedRule);
            Assert.Equal("Loaded 1 rule(s) from rules-load.json", vm.StatusText);
        }

        [Fact]
        public async Task LoadRulesCommand_KeepsRules_WhenDialogIsCanceled()
        {
            var (vm, service) = CreateViewModel(dialogService: new FakeFileDialogService { OpenPath = null });
            vm.AddRuleCommand.Execute(null);

            await vm.LoadRulesCommand.ExecuteAsync(null);

            Assert.Single(service.Rules);
        }

        [Fact]
        public async Task SaveRulesCommand_SetsStatus_WhenWriteFails()
        {
            var (vm, _) = CreateViewModel(
                dialogService: new FakeFileDialogService { SavePath = "rules.json" },
                fileSystem: new FakeFileSystem { WriteThrows = new IOException("disk full") });
            vm.AddRuleCommand.Execute(null);

            await vm.SaveRulesCommand.ExecuteAsync(null);

            Assert.StartsWith("Save failed:", vm.StatusText);
        }

        [Fact]
        public async Task LoadRulesCommand_SetsStatus_WhenFileIsNotValidRulesJson()
        {
            var fileSystem = new FakeFileSystem();
            fileSystem.Files["bad.json"] = "this is not json";
            var (vm, service) = CreateViewModel(
                dialogService: new FakeFileDialogService { OpenPath = "bad.json" },
                fileSystem: fileSystem);

            await vm.LoadRulesCommand.ExecuteAsync(null);

            Assert.StartsWith("Load failed:", vm.StatusText);
            Assert.Empty(service.Rules);
        }

        [Fact]
        public void DescriptionText_FollowsEdits_OfTheSelectedRule()
        {
            var (vm, service) = CreateViewModel();
            vm.AddRuleCommand.Execute(null);
            var rule = service.Rules[0];
            var expected = rule.GetDescription();
            Assert.Equal(expected, vm.DescriptionText);

            rule.TriggerValue = "99";

            Assert.Equal(rule.GetDescription(), vm.DescriptionText);
            Assert.Contains("99", vm.DescriptionText);
        }

        [Fact]
        public void SelectedRuleDescription_RaisesPropertyChanged_ForDescription()
        {
            var rule = new ScriptRule();
            var raised = new List<string>();
            rule.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            rule.TriggerAddress = 12;

            Assert.Contains(nameof(ScriptRule.Description), raised);
        }

        [Fact]
        public void RenamingRule_ToDuplicateName_RestoresPreviousName()
        {
            var (vm, service) = CreateViewModel();
            service.Rules.Add(new ScriptRule { Name = "Alpha" });
            var second = new ScriptRule { Name = "Beta" };
            service.Rules.Add(second);
            vm.SelectedRule = second;

            second.Name = "Alpha";

            Assert.Equal("Beta", second.Name);
            Assert.Equal("A rule with that name already exists", vm.StatusText);
        }

        [Fact]
        public void SelectingNewRule_UnsubscribesFromPreviousRule()
        {
            var (vm, service) = CreateViewModel();
            vm.AddRuleCommand.Execute(null);
            var first = service.Rules[0];
            var second = new ScriptRule { Name = "Second" };
            service.Rules.Add(second);
            vm.SelectedRule = second;

            first.TriggerValue = "UNIQUE-SENTINEL-77"; // must not touch the VM anymore

            Assert.DoesNotContain("UNIQUE-SENTINEL-77", vm.DescriptionText);
        }

        [Fact]
        public void RemovingSelectedRule_Externally_FallsBackToAnotherRule()
        {
            var (vm, service) = CreateViewModel();
            vm.AddRuleCommand.Execute(null);
            var first = service.Rules[0];
            service.Rules.Add(new ScriptRule { Name = "Second" });
            vm.SelectedRule = first;

            service.Rules.Remove(first); // e.g. removed through the REST API

            Assert.Equal("Second", vm.SelectedRule!.Name);

            service.Rules.Clear();
            Assert.Null(vm.SelectedRule);
        }

        [Fact]
        public void ResetOneTimeCommand_ForwardsToService()
        {
            var (vm, service) = CreateViewModel();
            vm.AddRuleCommand.Execute(null);

            vm.ResetOneTimeCommand.Execute(null);

            Assert.Equal(1, service.ResetCount);
            Assert.Equal("One-time rules armed again", vm.StatusText);
        }

        private sealed class FakeRuleService : IScriptRuleService
        {
            public ObservableCollection<ScriptRule> Rules { get; } = new();
            public int ResetCount { get; private set; }

            public void AddRule(ScriptRule rule) => Rules.Add(rule);

            public void RemoveRule(ScriptRule rule) => Rules.Remove(rule);

            public void UpdateRule(ScriptRule rule)
            {
                var index = IndexOf(r => r.Name == rule.Name);
                if (index >= 0)
                {
                    Rules[index] = rule;
                }
            }

            private int IndexOf(Predicate<ScriptRule> match)
            {
                for (var i = 0; i < Rules.Count; i++)
                {
                    if (match(Rules[i]))
                    {
                        return i;
                    }
                }
                return -1;
            }

            public Task EvaluateRulesAsync() => Task.CompletedTask;

            public void ResetOneTimeRules() => ResetCount++;

            public void ClearRules() => Rules.Clear();

            public Task<object?> GetRegisterValueAsync(string area, int address) => Task.FromResult<object?>(null);
        }

        private sealed class FakeFileDialogService : IFileDialogService
        {
            public string? SavePath { get; set; }
            public string? OpenPath { get; set; }

            public string? ShowSaveFileDialog(string title, string filter, string defaultFileName) => SavePath;
            public string? ShowOpenFileDialog(string title, string filter) => OpenPath;
            public Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName) => Task.FromResult(SavePath);
            public Task<string?> ShowOpenFileDialogAsync(string title, string filter) => Task.FromResult(OpenPath);
        }

        private sealed class FakeFileSystem : IFileSystem
        {
            public Dictionary<string, string> Files { get; } = new();
            public Exception? WriteThrows { get; set; }

            public Task<string> ReadAllTextAsync(string path)
            {
                if (!Files.TryGetValue(path, out var text))
                {
                    throw new FileNotFoundException(path);
                }
                return Task.FromResult(text);
            }

            public Task WriteAllTextAsync(string path, string contents)
            {
                if (WriteThrows != null) throw WriteThrows;
                Files[path] = contents;
                return Task.CompletedTask;
            }

            public bool FileExists(string path) => Files.ContainsKey(path);
        }

        private sealed class FakeMessageBoxService : IMessageBoxService
        {
            public DialogResult Result { get; set; } = DialogResult.Yes;

            public Task<DialogResult> ShowAsync(string message, string title, DialogButton button, DialogIcon icon)
                => Task.FromResult(Result);
        }
    }
}
