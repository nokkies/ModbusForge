using System.Collections.Generic;
using System.Collections.ObjectModel;
using ModbusForge.Core.Plc;
using ModbusForge.Models;
using ModbusForge.Services;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Blocks of the Control Expert Communication library (Communication Block Library,
    /// Schneider Electric 33002527.24). The simulation has no remote devices: exchanges
    /// complete at once without data, and the user is told once per block type.
    /// </summary>
    public class PlcCommunicationLibraryTests
    {
        private static PlcHarness Fbd(string network, params (string Name, string Type)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type) in variables) xef.Variable(name, type);
            return new PlcHarness(xef.Section("S", network).Project());
        }

        [Fact]
        public void TcpIpAddr_PacksSlotMapIndexAndAddressIntoWordArr5()
        {
            var plc = Fbd(Block("FBI_1", "TCP_IP_ADDR", 2, 2, "MAP_IDX=6,SLOT_ID=254,IP_B4=192,IP_B3=168,IP_B2=1,IP_B1=20|ADDRFLD=A"),
                ("A", "WordArr5")).Scan();

            Assert.Equal(new long[] { 0xFE06, 192, 168, 1, 20 }, Enumerable.Range(1, 5).Select(i => plc.Int($"A[{i}]")).ToArray());
        }

        [Fact]
        public void ReadReg_CompletesAtOnceWithoutData_AndSaysSoOnce()
        {
            var plc = Fbd(Block("FBI_1", "READ_REG", 2, 2, "REQ=Q,SLAVEREG=1,NO_REG=2,ADDRFLD=A|NDR=N,ERROR=E,REG_READ=D,STATUS=S"),
                ("Q", "BOOL"), ("A", "WordArr5"), ("N", "BOOL"), ("E", "BOOL"), ("D", "ARRAY[1..2] OF INT"), ("S", "WORD"));
            plc.Runtime.Write("D[1]", PlcValue.FromInteger(PlcType.Int, 7));

            plc.Runtime.Write("Q", PlcOps.True);
            plc.Scan();
            Assert.True(plc.Bool("N"));
            Assert.False(plc.Bool("E"));
            Assert.Equal(7, plc.Int("D[1]")); // nothing was read

            plc.Scan();
            Assert.False(plc.Bool("N")); // one cycle only

            plc.Runtime.Write("Q", PlcOps.False);
            plc.Scan();
            plc.Runtime.Write("Q", PlcOps.True);
            plc.Scan();
            var notice = Assert.Single(plc.Runtime.Notices);
            Assert.Contains("READ_REG", notice);
        }

        [Fact]
        public void Xxmit_CompletesAtOnce_WithoutAReply()
        {
            var plc = Fbd(Block("FBI_1", "XXMIT", 2, 2, "START=G,COMMAND=1,MSGOUT=M,MSGLEN=4|ACTIVE=A,DONE=D,ERROR=E,RECCOUNT=R"),
                ("G", "BOOL"), ("M", "ARRAY[1..4] OF BYTE"), ("A", "BOOL"), ("D", "BOOL"), ("E", "BOOL"), ("R", "INT"));

            plc.Runtime.Write("G", PlcOps.True);
            plc.Scan();

            Assert.True(plc.Bool("D"));
            Assert.False(plc.Bool("A") || plc.Bool("E"));
            Assert.Equal(0, plc.Int("R"));
            Assert.Contains(plc.Runtime.Notices, n => n.Contains("XXMIT"));
        }

        [Fact]
        public void Notices_ReachTheUser_ThroughTheRuntimeService()
        {
            var xef = new XefBuilder().Variable("Q", "BOOL").Variable("A", "WordArr5").Variable("D", "ARRAY[1..2] OF INT")
                .Section("S", Block("FBI_1", "READ_REG", 2, 2, "REQ=TRUE,SLAVEREG=1,NO_REG=2,ADDRFLD=A|REG_READ=D"));
            var result = new PlcXmlImporter().ImportFromXml(xef.Build(), "test.xef");
            var reports = new List<string>();
            using var service = new PlcRuntimeService();
            service.ProblemReported += reports.Add;
            service.Load(result.Project);
            service.SetScanIntervalMs(10_000);
            service.Start(new VisualNodeEditorConfig { Nodes = new ObservableCollection<VisualNode>(result.Sections.Single().Nodes), ScanIntervalMs = 10_000 });

            service.Tick(TimeSpan.FromMilliseconds(100));
            service.Tick(TimeSpan.FromMilliseconds(100));

            Assert.Single(reports, r => r.Contains("READ_REG"));
        }
    }
}
