// Assembly rule tests: GL001–GL007 with in-memory Cecil fixtures.
// Each rule gets a positive (fires) and negative (clean) case.

using FluentAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;
using GregLint.Rules;

namespace GregLint.Tests;

public sealed class AssemblyRuleTests : IDisposable
{
    private readonly List<Fixture> _fixtures = new();

    private Fixture New(string name = "TestMod") { var f = Fixture.Create(name); _fixtures.Add(f); return f; }

    private static AssemblyContext Ctx(Fixture f, string file = "TestMod.dll") =>
        new() { File = file, Module = f.Module, AssemblyName = f.Assembly.Name.Name };

    public void Dispose() { foreach (var f in _fixtures) f.Dispose(); }

    // ── GL001 ──

    [Fact]
    public void GL001_Fires_OnSetQualityLevelCall()
    {
        using var fx = New();
        var qs = fx.AddClass("UnityEngine", "QualitySettings");
        fx.AddMethod(qs, "SetQualityLevel", fx.Module.TypeSystem.Void,
            e => { e.Emit(OpCodes.Ldc_I4_1); e.Emit(OpCodes.Ret); });
        var mod = fx.AddClass("M", "Mod");
        var target = qs.Methods.First(m => m.Name == "SetQualityLevel");
        fx.AddMethod(mod, "Init", null, e =>
        {
            e.Emit(OpCodes.Ldc_I4_0);
            e.Emit(OpCodes.Call, fx.Module.ImportReference(target));
            e.Emit(OpCodes.Ret);
        });

        var findings = new NoSetQualityLevelRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.RuleId == "GL001" && f.Severity == Severity.Error);
    }

    [Fact]
    public void GL001_Clean_WithoutCall()
    {
        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Init");
        new NoSetQualityLevelRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    // ── GL002 ──

    [Fact]
    public void GL002_Fires_OnOnGuiMethod()
    {
        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "OnGUI");
        var findings = new NoOnGuiRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.RuleId == "GL002" && f.Severity == Severity.Error);
    }

    [Fact]
    public void GL002_Clean_WithoutOnGui()
    {
        using var fx = New();
        fx.AddClass("M", "Mod");
        new NoOnGuiRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    // ── GL003 ──

    [Fact]
    public void GL003_Fires_OnIntPrefix()
    {
        using var fx = New();
        var patch = fx.AddAttributeClass("HarmonyLib.HarmonyPatch");
        var cls = fx.AddClass("P", "Patch");
        fx.AddAttribute(cls, patch);
        fx.AddMethod(cls, "Prefix", fx.Module.TypeSystem.Int32);
        var findings = new PrefixSignatureRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.RuleId == "GL003" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void GL003_Clean_ForBoolAndVoidPrefix()
    {
        using var fx = New();
        var patch = fx.AddAttributeClass("HarmonyLib.HarmonyPatch");
        var cls = fx.AddClass("P", "Patch");
        fx.AddAttribute(cls, patch);
        fx.AddMethod(cls, "Prefix", fx.Module.TypeSystem.Boolean);
        var pre = fx.AddAttributeClass("HarmonyLib.HarmonyPrefix");
        var other = fx.AddClass("P", "Other");
        var m = fx.AddMethod(other, "Do");
        fx.AddAttribute(m, pre);
        new PrefixSignatureRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    // ── GL004 ──

    [Fact]
    public void GL004_Fires_OnUnguardedPatch_AndPassesGuarded()
    {
        using var fx = New();
        var patch = fx.AddAttributeClass("HarmonyLib.HarmonyPatch");
        var cls = fx.AddClass("P", "Patch");
        fx.AddAttribute(cls, patch);
        fx.AddMethod(cls, "Prefix");
        var guarded = fx.AddMethod(cls, "Postfix");
        fx.AddTryCatch(guarded);
        var findings = new PatchGuardRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.RuleId == "GL004" && f.Subject.Contains("Prefix"));
    }

    [Fact]
    public void GL004_Clean_OutsidePatchClass()
    {
        using var fx = New();
        var cls = fx.AddClass("M", "Plain");
        fx.AddMethod(cls, "Do");
        new PatchGuardRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    // ── GL005 ──

    [Fact]
    public void GL005_Fires_WithoutIdentity()
    {
        using var fx = New();
        fx.AddClass("M", "Lib");
        var findings = new ModIdentityRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.RuleId == "GL005" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void GL005_Clean_WithMelonInfo_GregMod_OrNativePlugin()
    {
        using var a = New("A");
        var melon = a.AddAttributeClass("MelonLoader.MelonInfoAttribute");
        a.AddAttribute(a.Assembly, melon);
        new ModIdentityRule().Check(Ctx(a)).Should().BeEmpty();

        using var b = New("B");
        var greg = b.AddAttributeClass("gregCore.PublicApi.Attributes.GregModAttribute");
        var cls = b.AddClass("M", "Mod");
        b.AddAttribute(cls, greg);
        new ModIdentityRule().Check(Ctx(b)).Should().BeEmpty();

        using var c = New("C");
        var iface = new TypeDefinition("gregCore.PublicApi", "IGregNativePlugin",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract, null);
        c.Module.Types.Add(iface);
        var impl = c.AddClass("N", "Plugin");
        impl.Interfaces.Add(new InterfaceImplementation(c.Module.ImportReference(iface)));
        new ModIdentityRule().Check(Ctx(c)).Should().BeEmpty();
    }

    // ── GL006 ──

    [Fact]
    public void GL006_Fires_OnActivationServiceCall_FromForeignAssembly()
    {
        using var fx = New("SomeMod");
        var svc = fx.AddClass("gregCore.Infrastructure.IO", "GregModActivationService");
        var target = fx.AddMethod(svc, "Activate");
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Boot", null, e =>
        {
            e.Emit(OpCodes.Ldnull);
            e.Emit(OpCodes.Call, fx.Module.ImportReference(target));
            e.Emit(OpCodes.Pop);
            e.Emit(OpCodes.Ret);
        });
        var findings = new ActivationOwnerRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.RuleId == "GL006" && f.Severity == Severity.Error);
    }

    [Fact]
    public void GL006_Clean_ForGregCoreAssembly()
    {
        using var fx = New("gregCore");
        var svc = fx.AddClass("gregCore.Infrastructure.IO", "GregModActivationService");
        var target = fx.AddMethod(svc, "Activate");
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Boot", null, e =>
        {
            e.Emit(OpCodes.Ldnull);
            e.Emit(OpCodes.Call, fx.Module.ImportReference(target));
            e.Emit(OpCodes.Pop);
            e.Emit(OpCodes.Ret);
        });
        new ActivationOwnerRule().Check(Ctx(fx)).Should().BeEmpty();
    }

    [Fact]
    public void GL006_Fires_OnIsinstTypeOperand()
    {
        using var fx = New("SomeMod");
        var svc = fx.AddClass("gregCore.Infrastructure.IO", "GregModActivationService");
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Probe", null, e =>
        {
            e.Emit(OpCodes.Ldnull);
            e.Emit(OpCodes.Isinst, fx.Module.ImportReference(svc));
            e.Emit(OpCodes.Pop);
            e.Emit(OpCodes.Ret);
        });
        new ActivationOwnerRule().Check(Ctx(fx)).Should().ContainSingle(f => f.RuleId == "GL006");
    }

    // ── GL007 ──

    [Fact]
    public void GL007_Fires_OnDeactivatedString()
    {
        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Load", null, e =>
        {
            e.Emit(OpCodes.Ldstr, "Mods/.deactivated/evil.dll");
            e.Emit(OpCodes.Pop);
            e.Emit(OpCodes.Ret);
        });
        var findings = new NoDeactivatedPathRule().Check(Ctx(fx));
        findings.Should().ContainSingle(f => f.RuleId == "GL007" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void GL007_Clean_WithoutString()
    {
        using var fx = New();
        var mod = fx.AddClass("M", "Mod");
        fx.AddMethod(mod, "Load", null, e =>
        {
            e.Emit(OpCodes.Ldstr, "Mods/ok.dll");
            e.Emit(OpCodes.Pop);
            e.Emit(OpCodes.Ret);
        });
        new NoDeactivatedPathRule().Check(Ctx(fx)).Should().BeEmpty();
    }
}
