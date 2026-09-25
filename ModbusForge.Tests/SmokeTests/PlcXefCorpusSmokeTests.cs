using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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

    private static bool HasPin(VisualNode node, string? pin, bool isInput)
        => node.Plc?.Pins.Any(p => p.IsInput == isInput && string.Equals(p.Name, pin, StringComparison.Ordinal)) == true;
}
