// Coverage tests: rule catalog, nested types, pathological metadata,
// manifest/mod.json edge cases, locks, CLI edges, engine extensibility.

using FluentAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;
using GregLint.Rules;

namespace GregLint.Tests;

public sealed class CoverageTests : IDisposable
{
    private readonly List<Fixture> _fixtures = new();
    private readonly List<string> _roots = new();

    private Fixture New(string name = "TestMod") { var f = Fixture.Create(name); _fixtures.Add(f); return f; }

    private string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "greglint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    public void Dispose()
    {
        foreach (var f in _fixtures) f.Dispose();
        foreach (var r in _roots) try { Directory.Delete(r, true); } catch { }
    }

    private static AssemblyContext Ctx(Fixture f, string file = "TestMod.dll") =>
        new() { File = file, Module = f.Module, AssemblyName = f.Assembly.Name.Name };

    // ── Catalog ──

    [Fact]
    public void Catalog_RuleIds_Unique_And_Titled()
    {
        var engine = new Engine();
        var ids = engine.AssemblyRules.Select(r => r.Id).Concat(engine.TreeRules.Select(r => r.Id)).ToArray();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeEquivalentTo("GL001", "GL002", "GL003", "GL004", "GL005", "GL006", "GL007",
            "GL101", "GL102", "GL103", "GL104", "GL105");
        var titles = engine.AssemblyRules.Select(r => r.Title).Concat(engine.TreeRules.Select(r => r.Title)).ToArray();
        titles.Should().HaveCount(12);
        foreach (var title in titles)
            title.Should().NotBeNullOrWhiteSpace();
    }

    // ── Nested types ──

    [Fact]
    public void Rules_See_Nested_Types()
    {
        using var fx = New();
        var outer = fx.AddClass("M", "Outer");
        var nested = new TypeDefinition("", "Inner",
            TypeAttributes.NestedPublic | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            fx.Module.TypeSystem.Object);
        outer.NestedTypes.Add(nested);
        fx.AddMethod(nested, "OnGUI");
        var findings = new NoOnGuiRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.Subject.Contains("Inner"));
    }

    // ── Pathological metadata (genuine catch coverage) ──

    [Fact]
    public void GL001_Survives_Broken_DeclaringType()
    {
        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        var ghost = new MethodReference("Ghost", fx.Module.TypeSystem.Void)
        {
            DeclaringType = new Fixture.BoomRef(fx.Module)
        };
        fx.AddMethod(mod, "Init", null, e =>
        {
            e.Emit(OpCodes.Call, ghost);
            e.Emit(OpCodes.Ret);
        });
        new NoSetQualityLevelRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    [Fact]
    public void GL003_Survives_Broken_ReturnType()
    {
        using var fx = New();
        var patch = fx.AddAttributeClass("HarmonyLib.HarmonyPatch");
        var cls = fx.AddClass("P", "Patch");
        fx.AddAttribute(cls, patch);
        var broken = new MethodDefinition("Prefix",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            new Fixture.BoomRef(fx.Module));
        broken.Body.GetILProcessor().Emit(OpCodes.Ret);
        cls.Methods.Add(broken);
        new PrefixSignatureRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    [Fact]
    public void GL006_Survives_Broken_Operand()
    {
        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        var ghost = new MethodReference("Ghost", fx.Module.TypeSystem.Void)
        {
            DeclaringType = new Fixture.BoomRef(fx.Module)
        };
        fx.AddMethod(mod, "Boot", null, e =>
        {
            e.Emit(OpCodes.Call, ghost);
            e.Emit(OpCodes.Ret);
        });
        new ActivationOwnerRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    [Fact]
    public void HasAttribute_Skips_Broken_Attribute()
    {
        using var fx = New();
        var ghost = new MethodReference(".ctor", fx.Module.TypeSystem.Void)
        {
            DeclaringType = new Fixture.BoomRef(fx.Module)
        };
        var cls = fx.AddClass("M", "Mod");
        cls.CustomAttributes.Add(new CustomAttribute(ghost));
        new ModIdentityRule().Check(Ctx(fx))
            .Should().ContainSingle(f => f.RuleId == "GL005");
    }

    [Fact]
    public void DerivesFrom_Survives_Broken_Base()
    {
        using var fx = New();
        var cls = fx.AddClass("M", "Mod");
        cls.BaseType = new Fixture.BoomRef(fx.Module);
        Cecilx.DerivesFrom(cls, "MelonLoader.MelonMod").Should().BeFalse();
        Cecilx.DerivesFrom(fx.AddClass("M", "Plain"), "Nope").Should().BeFalse();
    }

    [Fact]
    public void Cecilx_Direct_Edge_Cases()
    {
        Cecilx.PathHasSegment("", "x").Should().BeFalse();
        Cecilx.PathHasSegment(null!, "x").Should().BeFalse();
        Cecilx.PathHasSegment("a/b", "").Should().BeFalse();
        Cecilx.Display(null!).Should().Be("?");
        using var fx = New();
        var abs = fx.AddClass("M", "A");
        var m = fx.AddMethod(abs, "Do");
        Cecilx.Instructions(m).Should().NotBeNull();
        Cecilx.HasExceptionHandler(m).Should().BeFalse();
        var noBody = new MethodDefinition("Stub",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
            fx.Module.TypeSystem.Void);
        abs.Methods.Add(noBody);
        Cecilx.Instructions(noBody).Should().BeEmpty();
    }

    [Fact]
    public void GL007_Truncates_Long_Paths()
    {
        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        string evil = "Mods/.deactivated/" + new string('a', 200) + ".dll";
        fx.AddMethod(mod, "Load", null, e =>
        {
            e.Emit(OpCodes.Ldstr, evil);
            e.Emit(OpCodes.Pop);
            e.Emit(OpCodes.Ret);
        });
        var finding = new NoDeactivatedPathRule().Check(Ctx(fx)).Should()
            .ContainSingle().Which;
        finding.Message.Should().Contain("…");
    }

    // ── Manifest edges ──

    [Fact]
    public void GL103_Edge_Entries()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "manifest.json"),
            """{"mods":["","a\u0000b.dll",42],"library":[],"plugins":[]}""");
        var result = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL103" });
        var msgs = result.Findings.Select(f => f.Message).ToArray();
        msgs.Should().Contain(m => m.Contains("Empty entry"));
        msgs.Should().Contain(m => m.Contains("not a valid path"));
    }

    [Fact]
    public void GL103_Null_Literal_IsClean()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "manifest.json"), "null");
        new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL103" })
            .Findings.Should().BeEmpty();
    }

    // ── mod.json edges ──

    [Fact]
    public void GL104_Invalid_And_Null_Json()
    {
        var root = NewRoot();
        var bad = Path.Combine(root, "bad");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "mod.json"), "{nope");
        var r1 = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL104" });
        r1.Findings.Should().ContainSingle(f => f.Severity == Severity.Error && f.Subject == "mod.json");

        var nul = Path.Combine(root, "nul");
        Directory.CreateDirectory(nul);
        File.WriteAllText(Path.Combine(nul, "mod.json"), "null");
        var r2 = new Engine().Run(new LintContext { Root = nul }, new HashSet<string> { "GL104", "GL105" });
        r2.Findings.Should().BeEmpty();
    }

    [Fact]
    public void GL104_Entrypoint_Key_Accepted()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "custom.lua"), "");
        File.WriteAllText(Path.Combine(mod, "mod.json"),
            """{"id":"m","name":"M","version":"1.0.0","entrypoint":"custom.lua"}""");
        new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL104", "GL105" })
            .Findings.Should().BeEmpty();
    }

    [Fact]
    public void GL104_Backup_Collision_Uses_Numbered_Name()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "main.lua"), "");
        string manifest = Path.Combine(mod, "mod.json");
        File.WriteAllText(manifest, """{"id":"m","name":"M","version":"1.0.0","entry":"main.lua"}""");
        File.WriteAllText(manifest + ".bak", "old");
        new Engine().Run(new LintContext { Root = root, ApplyFix = true }, new HashSet<string> { "GL104" });
        File.Exists(manifest + ".bak.1").Should().BeTrue();
    }

    [Fact]
    public void Missing_Root_Is_Clean()
    {
        var missing = Path.Combine(Path.GetTempPath(), "greglint_nope_" + Guid.NewGuid().ToString("N"));
        var result = new Engine().Run(new LintContext { Root = missing });
        result.Findings.Should().BeEmpty();
        result.ScannedFiles.Should().BeEmpty();
    }

    [Fact]
    public void Unreadable_Subdir_Does_Not_Crash()
    {
        if (OperatingSystem.IsWindows()) return; // POSIX permission semantics only
        var root = NewRoot();
        var locked = Path.Combine(root, ".deactivated", "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(root, "top.dll"), "x");
        try
        {
            File.SetUnixFileMode(locked,
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            var result = new Engine().Run(new LintContext { Root = root });
            result.Findings.Should().BeEmpty();
        }
        finally
        {
            File.SetUnixFileMode(locked,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Manifest_Without_Optional_Sections()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "m.dll"), "x");
        File.WriteAllText(Path.Combine(root, "manifest.json"), """{"mods":[]}""");
        new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL103" })
            .Findings.Should().BeEmpty();
    }

    [Fact]
    public void GL104_Unreadable_File_Warns()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "main.lua"), "");
        string manifest = Path.Combine(mod, "mod.json");
        File.WriteAllText(manifest, """{"id":"m","entry":"main.lua"}""");
        using var noRead = File.Open(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = new Engine().Run(new LintContext { Root = root }, new HashSet<string> { "GL104" });
        result.Findings.Should().ContainSingle(f => f.Subject == "mod.json");
    }

    [Fact]
    public void GL104_WriteLock_Reports_Fix_Failure()
    {
        if (OperatingSystem.IsWindows()) return; // POSIX permission semantics only
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "main.lua"), "");
        string manifest = Path.Combine(mod, "mod.json");
        File.WriteAllText(manifest, """{"id":"m","entry":"main.lua"}""");
        try
        {
            File.SetUnixFileMode(mod,
                UnixFileMode.UserRead | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            var fix = new Engine().Run(new LintContext { Root = root, ApplyFix = true }, new HashSet<string> { "GL104" });
            fix.Findings.Should().ContainSingle(f => f.Message.Contains("Auto-fix failed"));
        }
        finally
        {
            File.SetUnixFileMode(mod,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Cli_Fix_Flag_Normalizes()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "main.lua"), "");
        File.WriteAllText(Path.Combine(mod, "mod.json"), """{"id":"m","entry":"main.lua"}""");
        Program.Main(new[] { "--fix", root }).Should().Be(0);
        File.Exists(Path.Combine(mod, "mod.json.bak")).Should().BeTrue();
    }

    [Fact]
    public void GL101_Garbage_In_Subdir_IsSafe()
    {
        var root = NewRoot();
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "junk.dll"), "not managed");
        var result = new Engine().Run(new LintContext { Root = root });
        result.SkippedFiles.Should().Be(1);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Symlink_Loop_Does_Not_Hang()
    {
        var root = NewRoot();
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            File.CreateSymbolicLink(Path.Combine(sub, "loop"), root);
        }
        catch
        {
            return; // symlinks unavailable: nothing to prove
        }
        var result = new Engine().Run(new LintContext { Root = root });
        result.Findings.Should().BeEmpty();
    }

    // ── Engine extensibility + failure isolation ──

    private sealed class BoomTree : ITreeRule
    {
        public string Id => "GL900";
        public string Title => "boom";
        public IReadOnlyList<Finding> Check(LintContext ctx) => throw new InvalidOperationException("boom-tree");
    }

    private sealed class BoomAsm : IAssemblyRule
    {
        public string Id => "GL901";
        public string Title => "boom";
        public IReadOnlyList<Finding> Check(AssemblyContext ctx) => throw new InvalidOperationException("boom-asm");
    }

    private sealed class EvilIdRule : ITreeRule
    {
        public string Id => throw new InvalidOperationException("boom-id");
        public string Title => "boom";
        public IReadOnlyList<Finding> Check(LintContext ctx) => Array.Empty<Finding>();
    }

    [Fact]
    public void Engine_Isolates_Rule_Failures()
    {
        var root = NewRoot();
        var ctx = new LintContext { Root = root };
        var engine = new Engine(null, new ITreeRule[] { new BoomTree() });
        engine.Run(ctx);
        ctx.Warnings.Should().ContainSingle(w => w.Contains("boom-tree"));

        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Init");
        string dll = fx.Write("ok.dll");
        string dir = Path.GetDirectoryName(dll)!;
        var ctx2 = new LintContext { Root = dir };
        var engine2 = new Engine(new IAssemblyRule[] { new BoomAsm() }, null);
        engine2.Run(ctx2, new HashSet<string> { "GL901" });
        ctx2.Warnings.Should().ContainSingle(w => w.Contains("boom-asm"));
    }

    [Fact]
    public void Engine_Fails_Loudly_On_Broken_Rule_Metadata()
    {
        // Rule metadata (Id) is read outside the per-rule isolation:
        // a hostile custom rule fails the run instead of being masked.
        var engine = new Engine(null, new ITreeRule[] { new EvilIdRule() });
        Assert.Throws<InvalidOperationException>(() => engine.Run(new LintContext { Root = NewRoot() }));
    }

    // ── CLI edges ──

    [Fact]
    public void Cli_Help_BadFormat_MissingPath()
    {
        Program.Main(new[] { "--help" }).Should().Be(0);
        Program.Main(new[] { "-h" }).Should().Be(0);
        var root = NewRoot();
        Program.Main(new[] { "--format", "yaml", root }).Should().Be(2);
        Program.Main(new[] { Path.Combine(root, "nope") }).Should().Be(2);
    }

    [Fact]
    public void Cli_SingleFile_Keeps_Sibling_Tree_Findings()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "mod.json"), """{"name":"X"}""");
        using var fx = New();
        var cls = fx.AddClass("M", "Mod");
        fx.AddMethod(cls, "Init");
        string dll = Path.Combine(mod, "m.dll");
        File.Copy(fx.Write("m.dll"), dll, overwrite: true);
        Program.Main(new[] { dll }).Should().Be(1);
    }

    [Fact]
    public void Report_Renders_Findings_And_Fixes()
    {
        var root = NewRoot();
        var mod = Path.Combine(root, "m");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "main.lua"), "");
        File.WriteAllText(Path.Combine(mod, "mod.json"), """{"id":"m","entry":"main.lua"}""");
        var ctx = new LintContext { Root = root, ApplyFix = true };
        var result = new Engine().Run(ctx, new HashSet<string> { "GL104", "GL105" });
        string text = Report.Text(result);
        text.Should().Contain("GL105");
        text.Should().Contain("fixed normalized");
        string json = Report.Json(result, "1.0.0");
        json.Should().Contain("\"rule\": \"GL105\"");
        json.Should().Contain("\"appliedFixes\"");
    }
}
