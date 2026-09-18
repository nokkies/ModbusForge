using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    /// <summary>
    /// View modes for the PLC editor
    /// </summary>
    public enum PlcViewMode
    {
        FBD,      // Function Block Diagram
        LADDER,   // Ladder Diagram
        SFC       // Sequential Function Chart
    }

    /// <summary>
    /// ViewModel for the comprehensive PLC Editor with Ladder, SFC, and XEF support
    /// </summary>
    public partial class PlcEditorViewModel : ObservableObject
    {
        private readonly ILogger<PlcEditorViewModel> _logger;
        private readonly IXefService _xefService;
        private readonly IPlcRuntimeService _runtimeService;
        private readonly IFileDialogService _fileDialogService;
        private readonly IMessageBoxService _messageBoxService;

        [ObservableProperty]
        private PlcViewMode _selectedViewMode = PlcViewMode.FBD;

        [ObservableProperty]
        private bool _isOnline;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private PlcTag? _selectedTag;

        [ObservableProperty]
        private VisualNodeEditorViewModel? _fbdView;

        [ObservableProperty]
        private ObservableCollection<LadderRung> _ladderRungs = new();

        [ObservableProperty]
        private ObservableCollection<SfcStep> _sfcSteps = new();

        [ObservableProperty]
        private ObservableCollection<SfcTransition> _sfcTransitions = new();

        [ObservableProperty]
        private ObservableCollection<PlcTag> _tags = new();

        [ObservableProperty]
        private string _currentProjectPath = "";

        public PlcEditorViewModel(
            ILogger<PlcEditorViewModel> logger,
            IXefService xefService,
            IPlcRuntimeService runtimeService,
            IFileDialogService fileDialogService,
            IMessageBoxService messageBoxService,
            VisualNodeEditorViewModel? fbdViewModel = null)
        {
            _logger = logger;
            _xefService = xefService;
            _runtimeService = runtimeService;
            _fileDialogService = fileDialogService;
            _messageBoxService = messageBoxService;
            _fbdView = fbdViewModel;

            // Subscribe to runtime service tag updates
            if (_runtimeService is PlcRuntimeService prs)
            {
                prs.AllTags.CollectionChanged += (s, e) =>
                {
                    Tags = new ObservableCollection<PlcTag>(prs.AllTags);
                };
            }

            SetupCommands();
        }

        #region Properties

        public List<PlcViewMode> ViewModes { get; } = new()
        {
            PlcViewMode.FBD,
            PlcViewMode.LADDER,
            PlcViewMode.SFC
        };

        public bool IsLadderView => SelectedViewMode == PlcViewMode.LADDER;
        public bool IsSfcView => SelectedViewMode == PlcViewMode.SFC;
        public bool IsFbdView => SelectedViewMode == PlcViewMode.FBD;

        public bool HasSelectedTag => SelectedTag != null;

        public IEnumerable<PlcTag> ForcedTags => Tags.Where(t => t.IsForced);

        public string ConnectionStateText => IsOnline ? "🟢 Connected" : "⚪ Offline";
        public string ActiveViewText => SelectedViewMode.ToString();

        #endregion

        #region Commands

        public ICommand AddRungCommand { get; private set; } = null!;
        public ICommand AddContactCommand { get; private set; } = null!;
        public ICommand AddNcContactCommand { get; private set; } = null!;
        public ICommand AddCoilCommand { get; private set; } = null!;
        public ICommand AddStepCommand { get; private set; } = null!;
        public ICommand AddTransitionCommand { get; private set; } = null!;
        public ICommand AddActionCommand { get; private set; } = null!;
        public ICommand ImportXefCommand { get; private set; } = null!;
        public ICommand ExportXefCommand { get; private set; } = null!;
        public ICommand ToggleOnlineCommand { get; private set; } = null!;
        public ICommand ForceSelectedTagCommand { get; private set; } = null!;
        public ICommand ReleaseAllForcesCommand { get; private set; } = null!;

        private void SetupCommands()
        {
            AddRungCommand = new RelayCommand(AddRung);
            AddContactCommand = new RelayCommand(() => AddContact(false));
            AddNcContactCommand = new RelayCommand(() => AddContact(true));
            AddCoilCommand = new RelayCommand(AddCoil);
            AddStepCommand = new RelayCommand(AddStep);
            AddTransitionCommand = new RelayCommand(AddTransition);
            AddActionCommand = new RelayCommand(AddAction);
            ImportXefCommand = new AsyncRelayCommand(ImportXefAsync);
            ExportXefCommand = new AsyncRelayCommand(ExportXefAsync);
            ToggleOnlineCommand = new AsyncRelayCommand(ToggleOnlineAsync);
            ForceSelectedTagCommand = new AsyncRelayCommand(ForceSelectedTagAsync);
            ReleaseAllForcesCommand = new AsyncRelayCommand(ReleaseAllForcesAsync);
        }

        #endregion

        #region Ladder Operations

        private void AddRung()
        {
            var rung = new LadderRung
            {
                RungNumber = LadderRungs.Count + 1,
                Comment = $"New Rung {LadderRungs.Count + 1}"
            };
            LadderRungs.Add(rung);
            StatusMessage = $"Added rung {rung.RungNumber}";
        }

        private void AddContact(bool normallyClosed)
        {
            if (!LadderRungs.Any())
            {
                AddRung();
            }

            var lastRung = LadderRungs.Last();
            lastRung.AddContact(
                PlcElementType.Input,
                "NewContact",
                normallyClosed,
                lastRung.Elements.Count
            );

            StatusMessage = $"Added {(normallyClosed ? "NC" : "NO")} contact to rung {lastRung.RungNumber}";
        }

        private void AddCoil()
        {
            if (!LadderRungs.Any())
            {
                AddRung();
            }

            var lastRung = LadderRungs.Last();
            if (lastRung.Coil == null)
            {
                lastRung.SetCoil(new CoilElement
                {
                    Address = "NewCoil",
                    Negated = false
                });
                StatusMessage = $"Added coil to rung {lastRung.RungNumber}";
            }
            else
            {
                StatusMessage = "Rung already has a coil";
            }
        }

        #endregion

        #region SFC Operations

        private void AddStep()
        {
            var step = new SfcStep
            {
                Name = $"Step_{SfcSteps.Count + 1}",
                StepNumber = SfcSteps.Count + 1,
                IsInitial = !SfcSteps.Any(),
                X = 50 + (SfcSteps.Count * 150),
                Y = 50
            };
            SfcSteps.Add(step);
            StatusMessage = $"Added SFC step: {step.Name}";
        }

        private void AddTransition()
        {
            if (SfcSteps.Count < 2)
            {
                StatusMessage = "Need at least 2 steps to add a transition";
                return;
            }

            var lastStep = SfcSteps.Last();
            var nextStepIndex = SfcSteps.Count;
            
            var transition = new SfcTransition
            {
                Condition = "Transition_Condition",
                SourceStepId = lastStep.Id,
                TargetStepId = "", // Will be set when next step is created
                X = lastStep.X + 70,
                Y = lastStep.Y + 80
            };

            SfcTransitions.Add(transition);
            StatusMessage = "Added SFC transition";
        }

        private void AddAction()
        {
            var selectedStep = SfcSteps.FirstOrDefault(s => s.IsActive);
            if (selectedStep == null && SfcSteps.Any())
            {
                selectedStep = SfcSteps.Last();
            }

            if (selectedStep != null)
            {
                selectedStep.AddAction($"Action_{selectedStep.Actions.Count + 1}");
                StatusMessage = $"Added action to {selectedStep.Name}";
            }
            else
            {
                StatusMessage = "No step selected";
            }
        }

        #endregion

        #region XEF Import/Export

        private async Task ImportXefAsync()
        {
            try
            {
                var filePath = await _fileDialogService.ShowOpenAsync(new FileDialogFilter
                {
                    Name = "XEF Files",
                    Extensions = new[] { ".xef", ".xml" }
                });

                if (string.IsNullOrEmpty(filePath))
                    return;

                StatusMessage = $"Importing {filePath}...";
                
                var project = await _xefService.ImportXefAsync(filePath);
                if (project == null)
                {
                    await _messageBoxService.ShowAsync("Failed to import XEF file", "Import Error");
                    return;
                }

                CurrentProjectPath = filePath;

                // Convert to current view mode
                switch (SelectedViewMode)
                {
                    case PlcViewMode.FBD:
                        if (_fbdView != null)
                        {
                            var config = await _xefService.ConvertXefToVisualNodesAsync(project);
                            // Update FBD view with imported config
                        }
                        break;

                    case PlcViewMode.LADDER:
                        var pou = project.Pous.FirstOrDefault();
                        if (pou?.LadderImplementation != null)
                        {
                            LadderRungs = new ObservableCollection<LadderRung>(pou.LadderImplementation);
                        }
                        break;

                    case PlcViewMode.SFC:
                        var sfcPou = project.Pous.FirstOrDefault(p => p.SfcImplementation != null);
                        if (sfcPou?.SfcImplementation != null)
                        {
                            SfcSteps = new ObservableCollection<SfcStep>(sfcPou.SfcImplementation.Steps);
                            SfcTransitions = new ObservableCollection<SfcTransition>(sfcPou.SfcImplementation.Transitions);
                        }
                        break;
                }

                // Import tags
                foreach (var tag in project.GlobalVariables)
                {
                    _runtimeService.AddTag(tag);
                }

                StatusMessage = $"Successfully imported {project.ProjectInfo.Name}";
                _logger.LogInformation("Imported XEF project: {ProjectName}", project.ProjectInfo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import XEF file");
                await _messageBoxService.ShowAsync($"Import failed: {ex.Message}", "Import Error");
                StatusMessage = "Import failed";
            }
        }

        private async Task ExportXefAsync()
        {
            try
            {
                var project = new XefProject
                {
                    ProjectInfo = new XefProjectInfo
                    {
                        Name = "ModbusForge_Project",
                        Vendor = "ModbusForge",
                        Description = "Exported from ModbusForge PLC Editor"
                    }
                };

                // Export based on current view mode
                switch (SelectedViewMode)
                {
                    case PlcViewMode.FBD:
                        if (_fbdView != null)
                        {
                            // Convert FBD to XEF
                            // This would use the existing VisualNodeEditorConfig
                        }
                        break;

                    case PlcViewMode.LADDER:
                        var ladderPou = new Pou
                        {
                            Name = "Main",
                            Type = PouType.Program,
                            ImplementationLanguage = "LD",
                            LadderImplementation = LadderRungs
                        };
                        project.Pous.Add(ladderPou);
                        break;

                    case PlcViewMode.SFC:
                        var sfcChart = new SfcChart
                        {
                            Steps = SfcSteps,
                            Transitions = SfcTransitions
                        };
                        var sfcPou = new Pou
                        {
                            Name = "Main",
                            Type = PouType.Program,
                            ImplementationLanguage = "SFC",
                            SfcImplementation = sfcChart
                        };
                        project.Pous.Add(sfcPou);
                        break;
                }

                // Add tags
                foreach (var tag in Tags)
                {
                    project.GlobalVariables.Add(tag);
                }

                var filePath = await _fileDialogService.ShowSaveAsync(new FileDialogFilter
                {
                    Name = "XEF Files",
                    Extensions = new[] { ".xef" }
                });

                if (string.IsNullOrEmpty(filePath))
                    return;

                await _xefService.ExportXefAsync(project, filePath);
                StatusMessage = $"Exported to {filePath}";
                _logger.LogInformation("Exported XEF project to {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export XEF file");
                await _messageBoxService.ShowAsync($"Export failed: {ex.Message}", "Export Error");
                StatusMessage = "Export failed";
            }
        }

        #endregion

        #region Online Operations

        private async Task ToggleOnlineAsync()
        {
            try
            {
                if (IsOnline)
                {
                    await _runtimeService.DisconnectAsync();
                    IsOnline = false;
                    StatusMessage = "Disconnected from PLC runtime";
                }
                else
                {
                    await _runtimeService.ConnectAsync();
                    IsOnline = true;
                    StatusMessage = "Connected to PLC runtime";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle online state");
                IsOnline = false;
                StatusMessage = $"Connection failed: {ex.Message}";
            }
        }

        private async Task ForceSelectedTagAsync()
        {
            if (SelectedTag == null)
                return;

            try
            {
                await _runtimeService.ForceTagAsync(SelectedTag.Name, SelectedTag.CurrentValue);
                StatusMessage = $"Forced tag {SelectedTag.Name}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to force tag {TagName}", SelectedTag.Name);
                StatusMessage = $"Force failed: {ex.Message}";
            }
        }

        private async Task ReleaseAllForcesAsync()
        {
            try
            {
                var forcedTags = _runtimeService.GetForcedTags().ToList();
                foreach (var tag in forcedTags)
                {
                    await _runtimeService.ReleaseForceAsync(tag.Name);
                }
                StatusMessage = $"Released {forcedTags.Count} forced tags";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release forces");
                StatusMessage = $"Release failed: {ex.Message}";
            }
        }

        #endregion

        #region Partial Property Change Handlers

        partial void OnSelectedViewModeChanged(PlcViewMode value)
        {
            OnPropertyChanged(nameof(IsLadderView));
            OnPropertyChanged(nameof(IsSfcView));
            OnPropertyChanged(nameof(IsFbdView));
            StatusMessage = $"Switched to {value} view";
        }

        partial void OnSelectedTagChanged(PlcTag? value)
        {
            OnPropertyChanged(nameof(HasSelectedTag));
        }

        #endregion
    }
}
