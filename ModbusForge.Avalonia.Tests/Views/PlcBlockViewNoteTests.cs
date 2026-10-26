using System.Collections.Generic;
using ModbusForge.Avalonia.Views;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Avalonia.Tests.Views
{
    /// <summary>
    /// The note on a block the PLC runtime leaves idle says why: an instance of a
    /// protected DFB is empty by design, and any other block it does not simulate
    /// is labelled as not simulated. A note wider than the block's frame is left out.
    /// </summary>
    public sealed class PlcBlockViewNoteTests
    {
        [Fact]
        public void IdleNote_SaysWhyABlockIsNotComputed()
        {
            var none = new Dictionary<string, string>();
            Assert.Equal("empty (protected DFB)", PlcBlockView.IdleNote(new PlcLiveState(none, none, true, false, ProtectedDfb: true)));
            Assert.Equal("not simulated", PlcBlockView.IdleNote(new PlcLiveState(none, none, true, false)));
            Assert.Null(PlcBlockView.IdleNote(new PlcLiveState(none, none, true, true)));
            Assert.Null(PlcBlockView.IdleNote(null));
        }

        [Fact]
        public void FitsInFrame_OnlyWhenNoWiderThanTheFrame()
        {
            Assert.True(PlcBlockView.FitsInFrame(40, 60));
            Assert.True(PlcBlockView.FitsInFrame(60, 60));
            Assert.False(PlcBlockView.FitsInFrame(60.5, 60));
        }
    }
}
