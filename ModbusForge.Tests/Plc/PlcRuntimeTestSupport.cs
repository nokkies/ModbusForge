using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ModbusForge.Core.Plc;
using ModbusForge.Data;
using ModbusForge.Services;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Builds small Unity XEF documents (dataBlock, one or more FBD sections, the
    /// task table) and runs them through the real importer and PLC runtime.
    /// </summary>
    internal sealed class XefBuilder
    {
        private readonly StringBuilder _variables = new();
        private readonly StringBuilder _types = new();
        private readonly StringBuilder _programs = new();
        private readonly StringBuilder _tasks = new();

        public XefBuilder Variable(string name, string type, string? address = null, string? init = null, string? inner = null)
        {
            _variables.Append($"<variables name=\"{name}\" typeName=\"{Escape(type)}\"");
            if (address != null) _variables.Append($" topologicalAddress=\"{address}\"");
            _variables.Append('>');
            if (init != null) _variables.Append($"<variableInit value=\"{Escape(init)}\"/>");
            if (inner != null) _variables.Append(inner);
            _variables.Append("</variables>");
            return this;
        }

        public XefBuilder Raw(string rootLevelXml)
        {
            _types.Append(rootLevelXml);
            return this;
        }

        /// <summary>An FBD section; <paramref name="network"/> is the networkFBD content.</summary>
        public XefBuilder Section(string name, string network, string task = "MAST", string? condition = null)
        {
            _programs.Append($"<program><identProgram name=\"{name}\" type=\"section\" task=\"{task}\"/><FBDSource><networkFBD>{network}</networkFBD></FBDSource></program>");
            _tasks.Append($"<sectionDesc name=\"{name}\" task=\"{task}\"");
            if (condition != null) _tasks.Append($" activationCondition=\"{condition}\"");
            _tasks.Append("/>");
            return this;
        }

        /// <summary>An ST section with its program text.</summary>
        public XefBuilder StSection(string name, string source, string task = "MAST")
        {
            _programs.Append($"<program><identProgram name=\"{name}\" type=\"section\" task=\"{task}\"/><STSource>{Escape(source).Replace(">", "&gt;")}</STSource></program>");
            _tasks.Append($"<sectionDesc name=\"{name}\" task=\"{task}\"/>");
            return this;
        }

        public XDocument Build(string? taskXml = null)
        {
            var taskDesc = taskXml ?? $"<taskDesc task=\"MAST\" taskType=\"cyclic\">{_tasks}</taskDesc>";
            return XDocument.Parse(
                $"<FEFExchangeFile>{_types}<dataBlock>{_variables}</dataBlock>{_programs}<logicConf><resource>{taskDesc}</resource></logicConf></FEFExchangeFile>");
        }

        public PlcProject Project(string? taskXml = null)
        {
            var result = new PlcXmlImporter().ImportFromXml(Build(taskXml), "test.xef");
            Assert.True(result.Success, string.Join("; ", result.Errors));
            return Assert.IsType<PlcProject>(result.Project);
        }

        private static string Escape(string text) => text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

        /// <summary>
        /// One FFBBlock. Pins are "IN1=Var" (actual parameter), "IN1" (free, or fed by a
        /// link), "!IN1=Var" (negated pin); inputs before "|", outputs after.
        /// </summary>
        public static string Block(string instance, string type, int x, int y, string pins, bool enEno = false, string? execAfter = null)
        {
            var sb = new StringBuilder();
            sb.Append($"<FFBBlock instanceName=\"{instance}\" typeName=\"{type}\" additionnalPinNumber=\"0\" enEnO=\"{(enEno ? "true" : "false")}\" width=\"7\" height=\"6\">");
            sb.Append($"<objPosition posX=\"{x}\" posY=\"{y}\"/>");
            sb.Append($"<descriptionFFB execAfter=\"{execAfter ?? ""}\">");
            var sides = pins.Split('|');
            foreach (var (side, element) in new[] { (sides[0], "inputVariable"), (sides.Length > 1 ? sides[1] : "", "outputVariable") })
            {
                foreach (var pin in side.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var inverted = pin.StartsWith('!');
                    var body = inverted ? pin[1..] : pin;
                    var eq = body.IndexOf('=');
                    var formal = eq < 0 ? body : body[..eq];
                    var actual = eq < 0 ? null : body[(eq + 1)..];
                    sb.Append($"<{element} invertedPin=\"{(inverted ? "true" : "false")}\" formalParameter=\"{formal}\"");
                    if (actual != null) sb.Append($" effectiveParameter=\"{Escape(actual)}\"");
                    sb.Append("/>");
                }
            }
            sb.Append("</descriptionFFB></FFBBlock>");
            return sb.ToString();
        }

        public static string Link(string fromInstance, string fromPin, string toInstance, string toPin)
            => $"<linkFB><linkSource parentObjectName=\"{fromInstance}\" pinName=\"{fromPin}\"/><linkDestination parentObjectName=\"{toInstance}\" pinName=\"{toPin}\"/></linkFB>";
    }

    /// <summary>A runtime scanning one data store with a fixed scan period.</summary>
    internal sealed class PlcHarness
    {
        public PlcHarness(PlcProject project)
        {
            Runtime = new PlcRuntime(project);
            Store = new DataStore();
            // Tests poke located variables before the first scan binds the store.
            Runtime.Memory.Store = Store;
        }

        public PlcRuntime Runtime { get; }
        public DataStore Store { get; }

        public PlcHarness Scan(int times = 1, int periodMs = 100)
        {
            for (var i = 0; i < times; i++) Runtime.Scan(Store, TimeSpan.FromMilliseconds(periodMs));
            return this;
        }

        public PlcValue Read(string reference) => Runtime.Read(reference);

        public bool Bool(string reference) => Runtime.Read(reference).AsBool();

        public long Int(string reference) => Runtime.Read(reference).AsInteger();

        public double Real(string reference) => Runtime.Read(reference).AsDouble();

        public PlcBlock BlockNamed(string instance) => Runtime.Project.Blocks.Single(b => b.InstanceName == instance);
    }
}
