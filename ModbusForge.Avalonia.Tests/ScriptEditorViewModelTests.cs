using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    /// <summary>
    /// View-model level coverage for the Script Editor toolbar commands
    /// (Add / Remove / Move / Clone) and their enable-state transitions.
    /// </summary>
    public sealed class ScriptEditorViewModelTests
    {
        private readonly ScriptEditorViewModel _vm;

        public ScriptEditorViewModelTests()
        {
            _vm = new ScriptEditorViewModel(
                new ScriptRunner(NullLogger<ScriptRunner>.Instance),
                new FakeConnectionManager(),
                new SyncDispatcher());
        }

        [Fact]
        public void AddCommand_AddsAReadHoldingCommandAndSelectsIt()
        {
            _vm.AddCommand.Execute(null);

            Assert.Single(_vm.Script.Commands);
            var added = _vm.Script.Commands[0];
            Assert.Equal(ScriptCommandType.ReadHoldingRegisters, added.CommandType);
            Assert.Same(added, _vm.SelectedCommand);
            Assert.True(_vm.CanRemoveSelected);
            Assert.True(_vm.CanRun);
        }

        [Fact]
        public void AddCommand_CanBeReused_ToBuildACommandList()
        {
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);

            Assert.Equal(3, _vm.Script.Commands.Count);
            // Each new command becomes the selection.
            Assert.Same(_vm.Script.Commands[2], _vm.SelectedCommand);
        }

        [Fact]
        public void RemoveCommand_RemovesTheSelectedCommand_AndReassignsSelection()
        {
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);

            _vm.SelectedCommand = _vm.Script.Commands[1];
            _vm.RemoveCommand.Execute(null);

            Assert.Equal(2, _vm.Script.Commands.Count);
            Assert.Same(_vm.Script.Commands[1], _vm.SelectedCommand);

            _vm.RemoveCommand.Execute(null);
            _vm.RemoveCommand.Execute(null);

            Assert.Empty(_vm.Script.Commands);
            Assert.Null(_vm.SelectedCommand);
            Assert.False(_vm.CanRun);
        }

        [Fact]
        public void MoveUpCommand_MovesTheSelectedCommandTowardsTheTop()
        {
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);

            var first = _vm.Script.Commands[0];
            _vm.SelectedCommand = first;
            Assert.False(_vm.CanMoveUp);

            _vm.SelectedCommand = _vm.Script.Commands[2];
            _vm.MoveUpCommand.Execute(null);
            Assert.Same(_vm.Script.Commands[1], _vm.SelectedCommand);
            Assert.True(_vm.CanMoveUp);

            _vm.MoveUpCommand.Execute(null);
            Assert.Same(_vm.Script.Commands[0], _vm.SelectedCommand);
            Assert.False(_vm.CanMoveUp);
        }

        [Fact]
        public void MoveDownCommand_MovesTheSelectedCommandTowardsTheBottom()
        {
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);

            var last = _vm.Script.Commands[2];
            _vm.SelectedCommand = last;
            Assert.False(_vm.CanMoveDown);

            _vm.SelectedCommand = _vm.Script.Commands[0];
            var originalFirst = _vm.SelectedCommand;
            _vm.MoveDownCommand.Execute(null);
            Assert.Same(_vm.Script.Commands[1], _vm.SelectedCommand);
            Assert.True(_vm.CanMoveDown);

            _vm.MoveDownCommand.Execute(null);
            Assert.Same(_vm.Script.Commands[2], _vm.SelectedCommand);
            Assert.Same(originalFirst, _vm.SelectedCommand);
            Assert.False(_vm.CanMoveDown);
        }

        [Fact]
        public void CloneCommand_CopiesTheSelectedCommandAndAppendsIt()
        {
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);

            var original = _vm.Script.Commands[0];
            original.Message = "original";
            _vm.SelectedCommand = original;

            _vm.CloneCommand.Execute(null);

            Assert.Equal(3, _vm.Script.Commands.Count);
            var clone = _vm.Script.Commands[2];
            Assert.Equal("original", clone.Message);
            Assert.Equal(original.CommandType, clone.CommandType);
            Assert.NotSame(original, clone);
            Assert.Same(clone, _vm.SelectedCommand);
        }

        [Fact]
        public void ToolbarCommands_AreDisabledUntilACommandExists()
        {
            Assert.False(_vm.CanRun);
            Assert.False(_vm.CanRemoveSelected);
            Assert.False(_vm.CanCloneSelected);
            Assert.False(_vm.CanMoveUp);
            Assert.False(_vm.CanMoveDown);

            _vm.AddCommand.Execute(null);

            Assert.True(_vm.CanRun);
            Assert.True(_vm.CanRemoveSelected);
            Assert.True(_vm.CanCloneSelected);
            Assert.False(_vm.CanMoveUp);
            Assert.False(_vm.CanMoveDown);
        }

        [Fact]
        public void RunScriptCommand_CanExecute_TracksWhetherCommandsExist()
        {
            // The Run Script button's enabled state is driven by the command's
            // CanExecute; it must become true once a command exists.
            Assert.False(_vm.RunScriptCommand.CanExecute(null));

            _vm.AddCommand.Execute(null);
            Assert.True(_vm.RunScriptCommand.CanExecute(null));

            _vm.RemoveCommand.Execute(null);
            Assert.False(_vm.RunScriptCommand.CanExecute(null));
        }

        [Fact]
        public void RemoveCommand_CanExecute_TracksSelection()
        {
            _vm.AddCommand.Execute(null);
            Assert.True(_vm.RemoveCommand.CanExecute(null));

            _vm.SelectedCommand = null;
            Assert.False(_vm.RemoveCommand.CanExecute(null));
        }

        [Fact]
        public void MoveCommands_CanExecute_TrackPosition()
        {
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);
            _vm.AddCommand.Execute(null);

            _vm.SelectedCommand = _vm.Script.Commands[0];
            Assert.False(_vm.MoveUpCommand.CanExecute(null));
            Assert.True(_vm.MoveDownCommand.CanExecute(null));

            _vm.SelectedCommand = _vm.Script.Commands[2];
            Assert.True(_vm.MoveUpCommand.CanExecute(null));
            Assert.False(_vm.MoveDownCommand.CanExecute(null));
        }

        [Fact]
        public void ClearLogCommand_CanExecute_TracksLogEntries()
        {
            Assert.False(_vm.ClearLogCommand.CanExecute(null));

            _vm.OutputLog.Add("first line");
            Assert.True(_vm.ClearLogCommand.CanExecute(null));

            _vm.ClearLogCommand.Execute(null);
            Assert.False(_vm.ClearLogCommand.CanExecute(null));
            Assert.Empty(_vm.OutputLog);
        }

        private sealed class FakeConnectionManager : IConnectionManager
        {
            public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
            public ConnectionProfile? ActiveProfile => null;
            public IModbusService? ActiveService => null;
            public event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
            public event EventHandler<ConnectionProfile>? ProfileConnected;
            public event EventHandler<ConnectionProfile>? ProfileDisconnected;

            public void AddProfile(ConnectionProfile profile) => Profiles.Add(profile);
            public void RemoveProfile(ConnectionProfile profile) => Profiles.Remove(profile);
            public void SetActiveProfile(ConnectionProfile profile) => ActiveProfileChanged?.Invoke(this, profile);
            public Task<bool> ConnectProfileAsync(ConnectionProfile profile)
            {
                ProfileConnected?.Invoke(this, profile);
                return Task.FromResult(true);
            }
            public Task DisconnectProfileAsync(ConnectionProfile profile)
            {
                ProfileDisconnected?.Invoke(this, profile);
                return Task.CompletedTask;
            }
            public Task DisconnectAllAsync() => Task.CompletedTask;
            public IModbusService? GetServiceForProfile(ConnectionProfile profile) => null;
            public void SaveProfiles() { }
            public void LoadProfiles() { }
        }
    }
}
