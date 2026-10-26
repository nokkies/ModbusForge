using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ModbusForge.Core.Plc;
using ModbusForge.Data;
using ModbusForge.Services;
using ModbusForge.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.SmokeTests;

/// <summary>
/// Compiles every XEF of the local corpus for the PLC runtime and scans it: every
/// FBD block and link must compile, and scans must run without a block failing.
/// Excluded from the default unit-test filter by its "SmokeTests" name.
/// </summary>
public class PlcRuntimeCorpusSmokeTests
{
    private const int Scans = 50;
    private readonly ITestOutputHelper _output;

    public PlcRuntimeCorpusSmokeTests(ITestOutputHelper output) => _output = output;

    private static string CorpusFolder =>
        Environment.GetEnvironmentVariable("MODBUSFORGE_XEF_CORPUS")
        ?? Path.Combine(FlaUiAppHelper.GetSolutionDirectory(), "..", "UnityParseEngine", "resources", "XML");

    public static IEnumerable<object[]> CorpusFiles() => PlcXefCorpusSmokeTests.CorpusFiles();

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void CorpusFile_CompilesAndScansInThePlcRuntime(string fileName)
    {
        var path = Path.Combine(CorpusFolder, fileName);
        if (!File.Exists(path))
        {
            _output.WriteLine($"XEF corpus not found at {CorpusFolder} - skipping.");
            return;
        }

        var xml = XDocument.Load(path);
        var fbd = xml.Descendants("program")
            .Where(p => (string?)p.Element("identProgram")?.Attribute("type") == "section")
            .Select(p => p.Element("FBDSource"))
            .Where(f => f != null)
            .ToList();
        var expectedBlocks = fbd.Sum(f => f!.Descendants("FFBBlock").Count());
        var expectedLinks = fbd.Sum(f => f!.Descendants("linkFB").Count());

        var result = new PlcXmlImporter().Import(path);
        var project = Assert.IsType<PlcProject>(result.Project);
        var blocks = project.Blocks.ToList();
        var linkedPins = blocks.SelectMany(b => b.Inputs.Append(b.En)).Count(p => p?.IsLinked == true);

        var expectedSt = xml.Descendants("program")
            .Count(p => (string?)p.Element("identProgram")?.Attribute("type") == "section" && !string.IsNullOrWhiteSpace(p.Element("STSource")?.Value));
        Assert.Equal(result.SectionsFound, project.Sections.Count(s => s.Program == null));
        Assert.Equal(expectedSt, project.Sections.Count(s => s.Program != null));
        Assert.Equal(expectedBlocks, blocks.Count);
        Assert.Equal(expectedLinks, linkedPins);
        Assert.DoesNotContain(project.Warnings, w => w.Contains("unknown pin", StringComparison.Ordinal));

        var runtime = new PlcRuntime(project);
        var store = new DataStore();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Scans; i++)
        {
            runtime.Scan(store, TimeSpan.FromMilliseconds(100));
        }
        watch.Stop();

        var byKind = blocks.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count());
        var unresolved = blocks.SelectMany(b => b.Inputs.Concat(b.Outputs))
            .Select(p => p.Operand).OfType<PlcUnresolvedOperand>().ToList();
        var unsupported = blocks.Where(b => b.Kind == PlcBlockKind.Unsupported)
            .GroupBy(b => b.TypeName).OrderByDescending(g => g.Count()).Take(8)
            .Select(g => $"{g.Key} x{g.Count()}");
        _output.WriteLine($"{fileName}: {blocks.Count} blocks - " +
                          $"{byKind.GetValueOrDefault(PlcBlockKind.Function) + byKind.GetValueOrDefault(PlcBlockKind.FunctionBlock)} simulated, " +
                          $"{byKind.GetValueOrDefault(PlcBlockKind.UserFunctionBlock)} DFB, " +
                          $"{byKind.GetValueOrDefault(PlcBlockKind.Unsupported)} unsupported; " +
                          $"{unresolved.Count} unresolved parameters; " +
                          $"{watch.Elapsed.TotalMilliseconds / Scans:0.00} ms/scan; " +
                          $"index errors {runtime.IndexErrors}; unsupported: {string.Join(", ", unsupported)}");
        foreach (var sample in unresolved.GroupBy(u => u.Reason).Select(g => $"  {g.Count()} {g.Key}: e.g. {g.First().Text}"))
        {
            _output.WriteLine(sample);
        }

        var stSections = project.Sections.Count(s => s.Program != null);
        var stProblems = project.Warnings.Where(w => w.Contains("(ST)", StringComparison.Ordinal)).ToList();
        _output.WriteLine($"  ST sections: {stSections}, ST problems: {stProblems.Count}, ST run errors: {runtime.StErrors}; " +
                          $"DFB code problems: {runtime.DfbProblems.Count}; skipped: {string.Join(", ", project.SkippedSections)}");
        foreach (var problem in stProblems.Concat(runtime.DfbProblems).Take(40))
        {
            _output.WriteLine("    " + problem);
        }

        Assert.Equal(0, runtime.BlockErrors);
    }
}
