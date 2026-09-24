// File/tree rule + engine + report + CLI tests with temp directories.
// Covers GL101–GL105, determinism, --fix backups, JSON shape, exit codes.

using FluentAssertions;
using Xunit;

namespace GregLint.Tests;

public sealed class FileRuleTests : IDisposable
{
    private readonly List<string> _roots = new();

    private string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "greglint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    public void Dispose() { foreach (var r in _roots) try { Directory.Delete(r, true); } catch { } }

    private static string WriteDll(string dir, string name, Action<Fixture>? build = null)
    {
        using var fx = Fixture.Create("M");
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Init");
        build?.Invoke(fx);
        return CopyTo(fx, dir, name);
    }

    private static string CopyTo(Fixture fx, string dir, string name)
    {
        string tmp = fx.Write(name);
        string dst = Path.Combine(dir, name);
        Directory.CreateDirectory(dir);
        File.Copy(tmp, dst, overwrite: true);
        return dst;
    }

    private static string WriteMelonDll(string dir, string name)
    {
        using var fx = Fixture.Create("M");
        var @base = fx.AddClass("MelonLoader", "MelonMod");
        var mod = fx.AddClass("M", "Mod", @base);
        fx.AddMethod(mod, "Init");
        return CopyTo(fx, dir, name);
    }

    // ── GL101 ──

    [Fact]
    public void GL101_Fires_ForMelonModInSubfolder()
    {
        var root = NewRoot();
        WriteMelonDll(Path.Combine(root, "sub"), "Evil.dll");
        var ctx = new LintContext { Root = root };
        var result = new Engine().Run(ctx, new HashSet<string> { "GL101" });
        result.Findings.Should().ContainSingle(f => f.RuleId == "GL101" && f.Severity == Severity.Error);
    }

    [Fact]
    public void GL101_Clean_TopLevel_And_GregNative_And_PlainLib()
    {
        var root = NewRoot();
        WriteMelonDll(root, "Top.dll");
        WriteMelonDll(Path.Combine(root, "gregNative", "pkg"), "Nat.dll");
        WriteDll(root, "Lib.dll");
        WriteDll(Path.Combine(root, "sub"), "Dep.dll");
        var result = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL101" });
        result.Findings.Should().BeEmpty();
    }

    // ── GL102 ──

    [Fact]
    public void GL102_Fires_OnActiveDuplicate()
    {
        var root = NewRoot();
        WriteDll(root, "Same.dll");
        WriteDll(Path.Combine(root, ".deactivated"), "Same.dll");
        WriteDll(Path.Combine(root, ".deactivated"), "OnlyOff.dll");
        var result = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL102" });
        result.Findings.Should().ContainSingle(f => f.RuleId == "GL102").Which
            .Subject.Should().Be("Same.dll");
    }

    // ── GL103 ──

    [Fact]
    public void GL103_Validates_Manifest()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "Ok.dll"), "x");
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        File.WriteAllText(Path.Combine(root, "lib", "L.dll"), "x");
        File.WriteAllText(Path.Combine(root, "manifest.json"),
            """{"Name":"P","Mods":["Ok.dll","Missing.dll","../evil.dll",".deactivated/Off.dll","note.txt"],"Library":["lib/L.dll"],"Plugins":[]}""");
        var result = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL103" });
        var msgs = result.Findings.Select(f => f.Message).ToArray();
        msgs.Should().Contain(m => m.Contains("not found"));
        msgs.Should().Contain(m => m.Contains("escapes"));
        msgs.Should().Contain(m => m.Contains("deactivated"));
        msgs.Should().Contain(m => m.Contains("not a .dll"));
        result.Errors.Should().Be(2); // traversal + deactivated
    }

    [Fact]
    public void GL103_InvalidJson_IsError_And_MissingManifest_IsClean()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "manifest.json"), "{broken");
        var bad = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL103" });
        bad.Findings.Should().ContainSingle(f => f.RuleId == "GL103" && f.Severity == Severity.Error);

        var empty = NewRoot();
        new Engine().Run(new LintContext { Root = empty }, new HashSet<string> { "GL103" })
            .Findings.Should().BeEmpty();
    }

    // ── GL104/GL105 + --fix ──

    [Fact]
    public void GL104_Fires_OnSchemaProblems()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "mymod");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "mod.json"), """{"name":"X"}""");
        var result = new Engine().Run(new LintContext { Root = root },
            new HashSet<string> { "GL104", "GL105" });
        result.Findings.Should().Contain(f => f.RuleId == "GL104" && f.Severity == Severity.Error && f.Subject == "id");
        result.Findings.Should().Contain(f => f.RuleId == "GL104" && f.Subject == "entry");
        result.Findings.Should().Contain(f => f.RuleId == "GL105" && f.Subject == "version");
    }

    [Fact]
    public void GL104_Fix_Normalizes_WithBackup()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "mymod");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "main.lua"), "-- ok");
        string manifest = Path.Combine(mod, "mod.json");
        File.WriteAllText(manifest, """{"version":"1.0.0","name":"X","id":"x","entry":"main.lua"}""");
        var ctx = new LintContext { Root = root, ApplyFix = true };
        var result = new Engine().Run(ctx, new HashSet<string> { "GL104", "GL105" });
        result.AppliedFixes.Should().ContainSingle(s => s.Contains("mod.json"));
        File.Exists(manifest + ".bak").Should().BeTrue();
        var second = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL104", "GL105" });
        second.AppliedFixes.Should().BeEmpty();
        second.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Engine_Skips_NativeDll_And_Sorts_Deterministically()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "native.dll"), "not a managed assembly");
        var r1 = new Engine().Run(new LintContext { Root = root });
        r1.SkippedFiles.Should().Be(1);
        r1.Findings.Should().BeEmpty();
        var r2 = new Engine().Run(new LintContext { Root = root });
        r2.Findings.Select(f => f.ToString()).Should().Equal(r1.Findings.Select(f => f.ToString()));
    }

    // ── Report + CLI ──

    [Fact]
    public void Report_Text_And_Json_Shapes()
    {
        var result = new Engine().Run(new LintContext { Root = NewRoot() });
        var text = Report.Text(result);
        text.Should().Contain("0 error(s)");
        var json = Report.Json(result, "1.0.0");
        json.Should().Contain("\"tool\": \"greglint\"");
        json.Should().Contain("\"summary\"");
    }

    [Fact]
    public void Cli_ExitCodes_And_UnknownRule()
    {
        var root = NewRoot();
        Program.Main(new[] { root }).Should().Be(0);
        Program.Main(new[] { "--severity-floor", "info", root }).Should().Be(0);
        Program.Main(new[] { "--rules", "GL999", root }).Should().Be(2);
        Program.Main(Array.Empty<string>()).Should().Be(2);

        WriteMelonDll(Path.Combine(root, "sub"), "Evil.dll");
        Program.Main(new[] { root }).Should().Be(1);
        Program.Main(new[] { "--format", "json", "--severity-floor", "warning", root }).Should().Be(1);
        Program.Main(new[] { "--severity-floor", "info", "--rules", "GL105", root }).Should().Be(0);
    }

    [Fact]
    public void Cli_SingleFileMode_Filters_To_File()
    {
        var root = NewRoot();
        string dll = WriteDll(Path.Combine(root, "sub"), "Evil.dll", fx =>
        {
            var extra = fx.AddClass("M", "Bad");
            fx.AddMethod(extra, "OnGUI");
        });
        Program.Main(new[] { dll }).Should().Be(1);
        Program.Main(new[] { "--format", "json", dll }).Should().Be(1);
    }

    [Fact]
    public void Cli_Json_Reports_Scanned_Count()
    {
        var root = NewRoot();
        WriteDll(root, "a.dll");
        var sw = new StringWriter();
        var old = Console.Out;
        Console.SetOut(sw);
        try { Program.Main(new[] { "--format", "json", root }).Should().Be(0); }
        finally { Console.SetOut(old); }
        sw.ToString().Should().Contain("\"scannedFiles\": 1");
    }

    [Fact]
    public void Cli_Floor_Info_Flags_Warnings()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "mod.json"), """{"id":"m","entry":"main.lua"}""");
        File.WriteAllText(Path.Combine(mod, "main.lua"), "");
        Program.Main(new[] { "--severity-floor", "info", root }).Should().Be(1);
    }
}
