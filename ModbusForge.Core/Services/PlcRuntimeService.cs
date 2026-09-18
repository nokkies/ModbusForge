using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Service for managing PLC tags with online operations (force, toggle, monitor)
    /// </summary>
    public interface IPlcRuntimeService
    {
        bool IsOnline { get; }
        ObservableCollection<PlcTag> AllTags { get; }
        
        Task ConnectAsync();
        Task DisconnectAsync();
        Task<PlcTag?> GetTagAsync(string tagName);
        Task<bool> SetTagValueAsync(string tagName, object value);
        Task<bool> ForceTagAsync(string tagName, object value);
        Task<bool> ReleaseForceAsync(string tagName);
        Task<bool> ToggleTagAsync(string tagName);
        Task StartMonitoringAsync(int intervalMs = 100);
        Task StopMonitoringAsync();
        void AddTag(PlcTag tag);
        void RemoveTag(string tagId);
        IEnumerable<PlcTag> GetForcedTags();
        IEnumerable<PlcTag> GetTagsByArea(PlcArea area);
    }

    /// <summary>
    /// Implementation of PLC runtime service for online tag operations
    /// </summary>
    public class PlcRuntimeService : IPlcRuntimeService, IDisposable
    {
        private readonly ILogger<PlcRuntimeService> _logger;
        private readonly IModbusService? _modbusService;
        private readonly ConcurrentDictionary<string, PlcTag> _tags = new();
        private CancellationTokenSource? _monitoringCts;
        private bool _isOnline;
        private bool _disposed;

        public bool IsOnline => _isOnline;
        public ObservableCollection<PlcTag> AllTags { get; } = new();

        public PlcRuntimeService(ILogger<PlcRuntimeService> logger, IModbusService? modbusService = null)
        {
            _logger = logger;
            _modbusService = modbusService;
            
            AllTags.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (PlcTag tag in e.NewItems)
                    {
                        _tags[tag.Id] = tag;
                        if (!string.IsNullOrEmpty(tag.Name))
                            _tags[tag.Name] = tag;
                    }
                }
                
                if (e.OldItems != null)
                {
                    foreach (PlcTag tag in e.OldItems)
                    {
                        _tags.TryRemove(tag.Id, out _);
                        if (!string.IsNullOrEmpty(tag.Name))
                            _tags.TryRemove(tag.Name, out _);
                    }
                }
            };
        }

        /// <summary>
        /// Connect to PLC runtime
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_isOnline)
                return;

            try
            {
                if (_modbusService != null && !_modbusService.IsConnected)
                {
                    await _modbusService.ConnectAsync();
                }
                
                _isOnline = true;
                _logger.LogInformation("PLC Runtime connected");
                
                // Start monitoring if not already running
                if (_monitoringCts == null || _monitoringCts.IsCancellationRequested)
                {
                    await StartMonitoringAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to PLC runtime");
                _isOnline = false;
                throw;
            }
        }

        /// <summary>
        /// Disconnect from PLC runtime
        /// </summary>
        public async Task DisconnectAsync()
        {
            await StopMonitoringAsync();
            
            if (_modbusService != null && _modbusService.IsConnected)
            {
                await _modbusService.DisconnectAsync();
            }
            
            _isOnline = false;
            _logger.LogInformation("PLC Runtime disconnected");
        }

        /// <summary>
        /// Get tag by name or ID
        /// </summary>
        public Task<PlcTag?> GetTagAsync(string tagName)
        {
            if (_tags.TryGetValue(tagName, out var tag))
                return Task.FromResult<PlcTag?>(tag);
            
            return Task.FromResult<PlcTag?>(null);
        }

        /// <summary>
        /// Set tag value (writes to both current and forced value if forced)
        /// </summary>
        public async Task<bool> SetTagValueAsync(string tagName, object value)
        {
            var tag = await GetTagAsync(tagName);
            if (tag == null)
            {
                _logger.LogWarning("Tag not found: {TagName}", tagName);
                return false;
            }

            try
            {
                // Update current value
                tag.SetCurrentValue(value);

                // If tag is forced, also update forced value
                if (tag.IsForced)
                {
                    tag.ForcedValue = value;
                }

                // Write to physical device if online
                if (_isOnline && _modbusService != null)
                {
                    await WriteToModbusAsync(tag, value);
                }

                _logger.LogDebug("Set tag {TagName} = {Value}", tagName, value);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set tag {TagName}", tagName);
                return false;
            }
        }

        /// <summary>
        /// Force a tag value (overrides normal operation)
        /// </summary>
        public async Task<bool> ForceTagAsync(string tagName, object value)
        {
            var tag = await GetTagAsync(tagName);
            if (tag == null)
            {
                _logger.LogWarning("Tag not found: {TagName}", tagName);
                return false;
            }

            try
            {
                tag.ForceValue(value);
                _logger.LogInformation("Forced tag {TagName} = {Value}", tagName, value);

                // Write forced value to physical device if online
                if (_isOnline && _modbusService != null)
                {
                    await WriteToModbusAsync(tag, value);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to force tag {TagName}", tagName);
                return false;
            }
        }

        /// <summary>
        /// Release forced value on a tag
        /// </summary>
        public async Task<bool> ReleaseForceAsync(string tagName)
        {
            var tag = await GetTagAsync(tagName);
            if (tag == null)
            {
                _logger.LogWarning("Tag not found: {TagName}", tagName);
                return false;
            }

            try
            {
                tag.ReleaseForce();
                _logger.LogInformation("Released force on tag {TagName}", tagName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release force on tag {TagName}", tagName);
                return false;
            }
        }

        /// <summary>
        /// Toggle a boolean tag
        /// </summary>
        public async Task<bool> ToggleTagAsync(string tagName)
        {
            var tag = await GetTagAsync(tagName);
            if (tag == null)
            {
                _logger.LogWarning("Tag not found: {TagName}", tagName);
                return false;
            }

            if (tag.DataType != IecDataType.BOOL)
            {
                _logger.LogWarning("Cannot toggle non-boolean tag {TagName}", tagName);
                return false;
            }

            try
            {
                tag.ToggleBoolValue();
                
                // Write toggled value if online
                if (_isOnline && _modbusService != null && tag.CurrentValue is bool b)
                {
                    await WriteToModbusAsync(tag, b ? 1 : 0);
                }

                _logger.LogDebug("Toggled tag {TagName}", tagName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle tag {TagName}", tagName);
                return false;
            }
        }

        /// <summary>
        /// Start continuous monitoring of all tags
        /// </summary>
        public async Task StartMonitoringAsync(int intervalMs = 100)
        {
            if (_monitoringCts != null && !_monitoringCts.IsCancellationRequested)
            {
                _logger.LogWarning("Monitoring already running");
                return;
            }

            _monitoringCts = new CancellationTokenSource();
            var token = _monitoringCts.Token;

            await Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await PollAllTagsAsync();
                        await Task.Delay(intervalMs, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Monitoring cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Monitoring error");
                }
            }, token);

            _logger.LogInformation("Started tag monitoring at {Interval}ms", intervalMs);
        }

        /// <summary>
        /// Stop monitoring
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            if (_monitoringCts != null)
            {
                await _monitoringCts.CancelAsync();
                _monitoringCts.Dispose();
                _monitoringCts = null;
                _logger.LogInformation("Stopped tag monitoring");
            }
        }

        /// <summary>
        /// Add a tag to the runtime
        /// </summary>
        public void AddTag(PlcTag tag)
        {
            if (tag == null)
                return;

            AllTags.Add(tag);
            _logger.LogDebug("Added tag {TagName}", tag.Name);
        }

        /// <summary>
        /// Remove a tag from the runtime
        /// </summary>
        public void RemoveTag(string tagId)
        {
            var tag = AllTags.FirstOrDefault(t => t.Id == tagId);
            if (tag != null)
            {
                AllTags.Remove(tag);
                _logger.LogDebug("Removed tag {TagName}", tag.Name);
            }
        }

        /// <summary>
        /// Get all forced tags
        /// </summary>
        public IEnumerable<PlcTag> GetForcedTags()
        {
            return AllTags.Where(t => t.IsForced);
        }

        /// <summary>
        /// Get tags by Modbus area
        /// </summary>
        public IEnumerable<PlcTag> GetTagsByArea(PlcArea area)
        {
            return AllTags.Where(t => t.Area == area);
        }

        #region Private Methods

        /// <summary>
        /// Poll all tags for updated values
        /// </summary>
        private async Task PollAllTagsAsync()
        {
            if (!_isOnline)
                return;

            try
            {
                // Group tags by area for efficient polling
                var tagsByArea = AllTags.GroupBy(t => t.Area);

                foreach (var group in tagsByArea)
                {
                    await PollAreaAsync(group.Key, group.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling tags");
            }
        }

        /// <summary>
        /// Poll tags in a specific Modbus area
        /// </summary>
        private async Task PollAreaAsync(PlcArea area, List<PlcTag> tags)
        {
            if (_modbusService == null || !_modbusService.IsConnected)
                return;

            try
            {
                // Sort tags by address for sequential reading
                var sortedTags = tags.OrderBy(t => t.Address).ToList();
                
                // Read blocks of contiguous addresses
                var blocks = CreateReadBlocks(sortedTags);

                foreach (var block in blocks)
                {
                    var values = await ReadModbusBlockAsync(area, block.StartAddress, block.Length);
                    
                    for (int i = 0; i < block.Tags.Count; i++)
                    {
                        if (i < values.Count)
                        {
                            var tag = block.Tags[i];
                            if (!tag.IsForced) // Don't overwrite forced values
                            {
                                tag.SetCurrentValue(values[i]);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling area {Area}", area);
            }
        }

        /// <summary>
        /// Create efficient read blocks from sorted tags
        /// </summary>
        private List<ReadBlock> CreateReadBlocks(List<PlcTag> sortedTags)
        {
            var blocks = new List<ReadBlock>();
            
            if (!sortedTags.Any())
                return blocks;

            var currentBlock = new ReadBlock
            {
                StartAddress = sortedTags[0].Address,
                Length = 1,
                Tags = { sortedTags[0] }
            };

            for (int i = 1; i < sortedTags.Count; i++)
            {
                var tag = sortedTags[i];
                
                // Check if tag is contiguous with current block
                if (tag.Address == currentBlock.StartAddress + currentBlock.Length && 
                    currentBlock.Length < 125) // Modbus max read size
                {
                    currentBlock.Length++;
                    currentBlock.Tags.Add(tag);
                }
                else
                {
                    // Start new block
                    blocks.Add(currentBlock);
                    currentBlock = new ReadBlock
                    {
                        StartAddress = tag.Address,
                        Length = 1,
                        Tags = { tag }
                    };
                }
            }

            blocks.Add(currentBlock);
            return blocks;
        }

        /// <summary>
        /// Read a block of values from Modbus
        /// </summary>
        private async Task<List<object>> ReadModbusBlockAsync(PlcArea area, int startAddress, int length)
        {
            var values = new List<object>();

            if (_modbusService == null)
                return values;

            try
            {
                switch (area)
                {
                    case PlcArea.Coil:
                    case PlcArea.DiscreteInput:
                        var boolValues = await _modbusService.ReadCoilsAsync(startAddress, length, area == PlcArea.DiscreteInput);
                        values.AddRange(boolValues.Cast<object>());
                        break;

                    case PlcArea.HoldingRegister:
                    case PlcArea.InputRegister:
                        var registerValues = await _modbusService.ReadHoldingRegistersAsync(startAddress, (ushort)length, area == PlcArea.InputRegister);
                        values.AddRange(registerValues.Select(v => (object)v));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Modbus block at {Address} length {Length}", startAddress, length);
            }

            return values;
        }

        /// <summary>
        /// Write value to Modbus
        /// </summary>
        private async Task WriteToModbusAsync(PlcTag tag, object value)
        {
            if (_modbusService == null)
                return;

            try
            {
                switch (tag.Area)
                {
                    case PlcArea.Coil:
                        await _modbusService.WriteCoilAsync(tag.Address, Convert.ToBoolean(value));
                        break;

                    case PlcArea.HoldingRegister:
                        await _modbusService.WriteHoldingRegisterAsync(tag.Address, Convert.ToUInt16(value));
                        break;
                }

                _logger.LogDebug("Wrote {Value} to {TagName} at {Address}", value, tag.Name, tag.FullAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write to Modbus for tag {TagName}", tag.Name);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _monitoringCts?.Cancel();
            _monitoringCts?.Dispose();
            _disposed = true;
        }

        #endregion
    }

    /// <summary>
    /// Helper class for creating efficient Modbus read blocks
    /// </summary>
    internal class ReadBlock
    {
        public int StartAddress { get; set; }
        public int Length { get; set; }
        public List<PlcTag> Tags { get; } = new();
    }
}
