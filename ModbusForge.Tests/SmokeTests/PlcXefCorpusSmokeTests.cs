using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ModbusForge.Core.Plc;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.SmokeTests;

/// <summary>
/// Imports every real Unity Pro / Control Expert export in the local corpus (the
/// sibling UnityParseEngine repo's resources/XML, or MODBUSFORGE_XEF_CORPUS) and
/// checks the result against counts taken straight from the XML. Excluded from the
/// default unit-test filter by its "SmokeTests" name: the corpus is ~230 MB.
/// </summary>
public class PlcXefCorpusSmokeTests
{
    private readonly ITestOutputHelper _output;

    public PlcXefCorpusSmokeTests(ITestOutputHelper output) => _output = output;

    private static string CorpusFolder =>
        Environment.GetEnvironmentVariable("MODBUSFORGE_XEF_CORPUS")
        ?? Path.Combine(FlaUiAppHelper.GetSolutionDirectory(), "..", "UnityParseEngine", "resources", "XML");

    public static IEnumerable<object[]> CorpusFiles()
    {
        var files = Directory.Exists(CorpusFolder)
            ? Directory.GetFiles(CorpusFolder, "*.XEF").Select(Path.GetFileName).OrderBy(n => n).ToList()
            : new List<string?>();
        if (files.Count == 0)
        {
            files.Add("(no corpus)");
        }

        return files.Select(f => new object[] { f! });
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void CorpusFile_ImportsEveryFbdSectionBlockAndLink(string fileName)
    {
        var path = Path.Combine(CorpusFolder, fileName);
        if (!File.Exists(path))
        {
            _output.WriteLine($"XEF corpus not found at {CorpusFolder} - skipping.");
            return;
        }

        var xml = XDocument.Load(path);
        var fbdSections = xml.Descendants("program")
            .Where(p => (string?)p.Element("identProgram")?.Attribute("type") == "section")
            .Select(p => p.Element("FBDSource"))
            .Where(f => f != null)
            .ToList();
        var expectedBlocks = fbdSections.Sum(f => f!.Descendants("FFBBlock").Count());
        var expectedTextBoxes = fbdSections.Sum(f => f!.Descendants("textBox").Count());
        var expectedLinks = fbdSections.Sum(f => f!.Descendants("linkFB").Count());
        var expectedBends = fbdSections.Sum(f => f!.Descendants("gridObjPosition").Count());

        var started = DateTime.UtcNow;
        var result = new PlcXmlImporter().Import(path);
        _output.WriteLine($"{fileName}: {(DateTime.UtcNow - started).TotalMilliseconds:0} ms, " +
                          $"{result.SectionsFound} sections, {result.TotalNodes} blocks, {result.TotalConnections} links, " +
                          $"{result.Warnings.Count} warnings");

        var nodes = result.Sections.SelectMany(s => s.Nodes).ToList();
        var links = result.Sections.SelectMany(s => s.Connections).ToList();
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(fbdSections.Count, result.SectionsFound);
        Assert.Equal(expectedBlocks, nodes.Count(n => n.ElementType != PlcElementType.PlcComment));
        Assert.Equal(expectedTextBoxes, nodes.Count(n => n.ElementType == PlcElementType.PlcComment));
        Assert.Equal(expectedLinks, links.Count);
        Assert.Equal(expectedBends, links.Sum(l => l.RoutePoints.Count));
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("Link references unknown instance", StringComparison.Ordinal));

        // Every link must start and end on a pin the block actually draws.
        var nodeById = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var dangling = links
            .Where(l => !HasPin(nodeById[l.SourceNodeId], l.SourcePin, isInput: false)
                        || !HasPin(nodeById[l.TargetNodeId], l.TargetPin, isInput: true))
            .Select(l => $"{l.SourceNodeId}.{l.SourcePin} -> {l.TargetNodeId}.{l.TargetPin}")
            .ToList();
        Assert.True(dangling.Count == 0, $"{dangling.Count} link(s) end on no drawn pin, e.g. {string.Join(", ", dangling.Take(3))}");

        // Every drawn pin must sit where Control Expert recorded the link endpoint
        // (linkSource/linkDestination objPosition, cell centre in canvas pixels).
        // Links become connections in document order, so the k-th link of a section
        // is its k-th connection.
        var endpoints = 0;
        var misplaced = new List<string>();
        for (var ordinal = 0; ordinal < fbdSections.Count; ordinal++)
        {
            var section = result.Sections[ordinal];
            var sectionNodes = section.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            var xmlLinks = fbdSections[ordinal]!.Descendants("linkFB")
                .Where(l => l.Element("linkSource") != null && l.Element("linkDestination") != null)
                .ToList();
            Assert.Equal(xmlLinks.Count, section.Connections.Count);

            for (var k = 0; k < xmlLinks.Count; k++)
            {
                var connection = section.Connections[k];
                foreach (var (end, nodeId, pinName, isInput) in new[]
                         {
                             ("linkSource", connection.SourceNodeId, connection.SourcePin, false),
                             ("linkDestination", connection.TargetNodeId, connection.TargetPin, true)
                         })
                {
                    var pos = xmlLinks[k].Element(end)!.Element("objPosition");
                    var node = sectionNodes[nodeId];
                    var pin = node.Plc?.Pins.FirstOrDefault(p => p.IsInput == isInput && p.Name == pinName);
                    if (pos == null || pin == null) continue;

                    endpoints++;
                    var drawnX = isInput ? node.X + node.Plc!.FrameInset : node.X + node.Width - node.Plc!.FrameInset;
                    var drawnY = node.Y + pin.CenterY;
                    var recordedX = 60 + (((int)pos.Attribute("posX")! + 0.5) * 20);
                    var recordedY = 40 + (((int)pos.Attribute("posY")! + 0.5) * 20);
                    if (drawnX != recordedX || drawnY != recordedY)
                    {
                        misplaced.Add($"{node.Id} {node.Plc.TypeName}.{pin.Name} drawn ({drawnX},{drawnY}) recorded ({recordedX},{recordedY})");
                    }
                }
            }
        }

        _output.WriteLine($"{fileName}: {endpoints} link endpoints, {misplaced.Count} not on their drawn pin");
        Assert.True(misplaced.Count == 0,
            $"{misplaced.Count} of {endpoints} link endpoints miss their drawn pin, e.g. {string.Join("; ", misplaced.Take(3))}");
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void CorpusFile_ListsEveryDfbType(string fileName)
    {
        var path = Path.Combine(CorpusFolder, fileName);
        if (!File.Exists(path))
        {
            _output.WriteLine($"XEF corpus not found at {CorpusFolder} - skipping.");
            return;
        }

        var xml = XDocument.Load(path);
        var fbSources = xml.Root!.Elements("FBSource").ToList();
        var result = new PlcXmlImporter().Import(path);
        _output.WriteLine($"{fileName}: {result.DfbTypes.Count} DFB types, " +
                          $"{result.DfbTypes.Count(t => t.IsProtected)} protected, " +
                          $"{result.DfbTypes.Sum(t => t.Sections.Count)} sections");
        Assert.True(result.Success, string.Join("; ", result.Errors));

        Assert.Equal(fbSources.Select(f => (string?)f.Attribute("nameOfFBType") ?? ""), result.DfbTypes.Select(t => t.Name));
        Assert.Equal(
            fbSources.Select(f => (IsProtected: f.Element("crypted") != null,
                Inputs: InterfaceCount(f, "inputParameters"),
                Outputs: InterfaceCount(f, "outputParameters"),
                InOuts: InterfaceCount(f, "inOutParameters"))),
            result.DfbTypes.Select(t => (IsProtected: t.IsProtected,
                Inputs: t.Inputs.Count,
                Outputs: t.Outputs.Count,
                InOuts: t.InOuts.Count)));

        // Every section in XEF order, with the blocks its FBD canvas draws (text boxes are comments, not blocks).
        var expectedSections = fbSources.SelectMany(f => DfbSectionsOf(f).Select(s => (
            Type: (string?)f.Attribute("nameOfFBType") ?? "",
            Name: s.Name,
            Language: s.Language,
            Blocks: s.Language == "FBD" ? s.Source.Descendants("FFBBlock").Count() : (int?)null))).ToList();
        var actualSections = result.DfbTypes.SelectMany(t => t.Sections.Select(s => (
            Type: t.Name,
            Name: s.Name,
            Language: s.Language,
            Blocks: s.Canvas?.Nodes.Count(n => n.ElementType != PlcElementType.PlcComment)))).ToList();
        Assert.Equal(expectedSections, actualSections);
    }

    [Fact]
    public void Lcplc001_ListsItsFourProtectedTypes()
    {
        var path = Path.Combine(CorpusFolder, "LCPLC001.XEF");
        if (!File.Exists(path))
        {
            _output.WriteLine($"XEF corpus not found at {CorpusFolder} - skipping.");
            return;
        }

        var result = new PlcXmlImporter().Import(path);
        Assert.True(result.Success, string.Join("; ", result.Errors));

        var protectedNames = result.DfbTypes.Where(t => t.IsProtected).Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "xANL101", "xANL102", "zPLCInfo", "zScale" }, protectedNames);

        var xanl101 = result.DfbTypes.Single(t => t.Name == "xANL101");
        Assert.Equal(19, xanl101.Inputs.Count);
        Assert.Equal(14, xanl101.Outputs.Count);
        Assert.Equal(5, xanl101.InOuts.Count);

        // Both xANL101 blocks in Analogs_IN are instances of a protected type.
        var project = Assert.IsType<PlcProject>(result.Project);
        var instances = project.Sections.Single(s => s.Name == "Analogs_IN").Blocks
            .Where(b => b.TypeName == "xANL101").ToList();
        Assert.Equal(2, instances.Count);
        Assert.All(instances, b => Assert.True(b.IsProtectedDfb, $"{b.InstanceName} is not a protected DFB"));
    }

    [Fact]
    public void Sn6550_OpensLcnDig101AndLcnXiLockSections()
    {
        var path = Path.Combine(CorpusFolder, "SN6550.XEF");
        if (!File.Exists(path))
        {
            _output.WriteLine($"XEF corpus not found at {CorpusFolder} - skipping.");
            return;
        }

        var result = new PlcXmlImporter().Import(path);
        Assert.True(result.Success, string.Join("; ", result.Errors));

        var command = result.DfbTypes.Single(t => t.Name == "LCN_DIG101").Sections.Single(s => s.Name == "Command_FBD");
        Assert.NotNull(command.Canvas);
        Assert.Contains(command.Canvas.Nodes, n => n.ElementType != PlcElementType.PlcComment);

        var status = result.DfbTypes.Single(t => t.Name == "LCN_xILock").Sections.Single(s => s.Name == "Status");
        Assert.False(string.IsNullOrWhiteSpace(status.SourceText));
    }

    /// <summary>
    /// The code sections an XEF lists for a DFB type, in XEF order: each FBProgram with a *Source
    /// child, as (name, language, source). A protected type lists none, as the importer does.
    /// </summary>
    private static IEnumerable<(string Name, string Language, XElement Source)> DfbSectionsOf(XElement fbSource)
    {
        if (fbSource.Element("crypted") != null) yield break;

        foreach (var program in fbSource.Elements("FBProgram"))
        {
            var source = program.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Source", StringComparison.Ordinal));
            if (source == null) continue;
            yield return ((string?)program.Attribute("name") ?? "", source.Name.LocalName[..^"Source".Length], source);
        }
    }

    /// <summary>
    /// The distinct, named variables of one interface group, read from the FBSource and from its
    /// ExternalToolsOnly child. A name seen first wins, as the importer does.
    /// </summary>
    private static int InterfaceCount(XElement fbSource, string group)
    {
        var holders = new[] { fbSource, fbSource.Element("ExternalToolsOnly") };
        return holders.Where(h => h != null)
            .SelectMany(h => h!.Element(group)?.Elements("variables") ?? Enumerable.Empty<XElement>())
            .Select(v => (string?)v.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static bool HasPin(VisualNode node, string? pin, bool isInput)
        => node.Plc?.Pins.Any(p => p.IsInput == isInput && string.Equals(p.Name, pin, StringComparison.Ordinal)) == true;
}
