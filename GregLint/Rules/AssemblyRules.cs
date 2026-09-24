// IL rules GL001–GL007 (Mono.Cecil, read-only, never loads assemblies).

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GregLint.Rules;

internal static class Known
{
    public const string MelonModBase = "MelonLoader.MelonMod";
    public const string MelonInfo = "MelonLoader.MelonInfoAttribute";
    public const string GregModAttribute = "gregCore.PublicApi.Attributes.GregModAttribute";
    public const string GregNativePlugin = "gregCore.PublicApi.IGregNativePlugin";
    public const string HarmonyPatchV2 = "HarmonyLib.HarmonyPatch";
    public const string HarmonyPatchV1 = "Harmony.HarmonyPatch";
    public const string HarmonyPrefix = "HarmonyLib.HarmonyPrefix";
    public const string ActivationService = "GregModActivationService";
}

/// <summary>GL001 Error: QualitySettings.SetQualityLevel stomps game tiers (use granular levers).</summary>
public sealed class NoSetQualityLevelRule : IAssemblyRule
{
    public string Id => "GL001";
    public string Title => "No QualitySettings.SetQualityLevel";
    public IReadOnlyList<Finding> Check(AssemblyContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var type in Cecilx.AllTypes(ctx.Module))
        {
            foreach (var method in type.Methods)
            {
                foreach (var ins in Cecilx.Instructions(method))
                {
                    if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                    if (ins.Operand is not MethodReference target) continue;
                    string? decl = null, name = null;
                    try { decl = target.DeclaringType?.FullName; name = target.Name; } catch { continue; }
                    if (decl == "UnityEngine.QualitySettings" && name == "SetQualityLevel")
                        out_.Add(new Finding
                        {
                            RuleId = Id, Severity = Severity.Error, File = ctx.File,
                            Subject = Cecilx.Display(method),
                            Message = "QualitySettings.SetQualityLevel stomps game-specific quality tiers.",
                            FixHint = "Set granular QualitySettings (mipmaps, AA, shadows, LOD) and snapshot/restore instead."
                        });
                }
            }
        }
        return out_;
    }
}

/// <summary>GL002 Error: OnGUI is stripped in IL2CPP builds (use UIToolkit).</summary>
public sealed class NoOnGuiRule : IAssemblyRule
{
    public string Id => "GL002";
    public string Title => "No OnGUI methods";
    public IReadOnlyList<Finding> Check(AssemblyContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var type in Cecilx.AllTypes(ctx.Module))
        {
            if (type.IsInterface) continue;
            foreach (var method in type.Methods)
            {
                if (method.IsAbstract) continue;
                if (string.Equals(method.Name, "OnGUI", StringComparison.Ordinal))
                    out_.Add(new Finding
                    {
                        RuleId = Id, Severity = Severity.Error, File = ctx.File,
                        Subject = Cecilx.Display(method),
                        Message = "OnGUI is stripped in IL2CPP builds and never runs.",
                        FixHint = "Build UI with UIToolkit (GregPanelBuilder) instead."
                    });
            }
        }
        return out_;
    }
}

/// <summary>GL003 Warning: Harmony prefixes must return void or bool (bool=false skips original).</summary>
public sealed class PrefixSignatureRule : IAssemblyRule
{
    public string Id => "GL003";
    public string Title => "Harmony prefix bool/void signature";
    public IReadOnlyList<Finding> Check(AssemblyContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var type in Cecilx.AllTypes(ctx.Module))
        {
            bool inPatch = Cecilx.HasAttribute(type, Known.HarmonyPatchV2)
                || Cecilx.HasAttribute(type, Known.HarmonyPatchV1);
            foreach (var method in type.Methods)
            {
                bool isPrefix = Cecilx.HasAttribute(method, Known.HarmonyPrefix)
                    || (inPatch && string.Equals(method.Name, "Prefix", StringComparison.Ordinal));
                if (!isPrefix) continue;
                string ret;
                try { ret = method.ReturnType?.FullName ?? "?"; } catch { continue; }
                if (ret != "System.Void" && ret != "System.Boolean")
                    out_.Add(new Finding
                    {
                        RuleId = Id, Severity = Severity.Warning, File = ctx.File,
                        Subject = Cecilx.Display(method),
                        Message = $"Harmony prefix returns {ret}; only void/bool have defined skip semantics.",
                        FixHint = "Return bool (false skips the original) or void."
                    });
            }
        }
        return out_;
    }
}

/// <summary>GL004 Warning: patch methods should be exception-guarded (SafePatch convention).</summary>
public sealed class PatchGuardRule : IAssemblyRule
{
    public string Id => "GL004";
    public string Title => "Patch methods carry exception handling";
    public IReadOnlyList<Finding> Check(AssemblyContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var type in Cecilx.AllTypes(ctx.Module))
        {
            bool inPatch = Cecilx.HasAttribute(type, Known.HarmonyPatchV2)
                || Cecilx.HasAttribute(type, Known.HarmonyPatchV1);
            if (!inPatch) continue;
            foreach (var method in type.Methods)
            {
                if (method.IsAbstract || method.IsConstructor) continue;
                if (!method.HasBody) continue;
                if (Cecilx.HasExceptionHandler(method)) continue;
                out_.Add(new Finding
                {
                    RuleId = Id, Severity = Severity.Warning, File = ctx.File,
                    Subject = Cecilx.Display(method),
                    Message = "Patch method without exception handling; one throw breaks the game method.",
                    FixHint = "Wrap patch bodies in try/catch (SafePatch convention)."
                });
            }
        }
        return out_;
    }
}

/// <summary>GL005 Warning: scanned DLL has no mod identity (MelonInfo / GregMod / native plugin).</summary>
public sealed class ModIdentityRule : IAssemblyRule
{
    public string Id => "GL005";
    public string Title => "Mod identity present";
    public IReadOnlyList<Finding> Check(AssemblyContext ctx)
    {
        // No outer guard: HasAttribute/AllTypes/Implements already isolate
        // corrupt metadata per element; anything escaping is an engine-level
        // failure (isolated + reported per rule there).
        if (Cecilx.HasAttribute(ctx.Module.Assembly, Known.MelonInfo))
            return Array.Empty<Finding>();
        foreach (var type in Cecilx.AllTypes(ctx.Module))
        {
            if (Cecilx.HasAttribute(type, Known.GregModAttribute)) return Array.Empty<Finding>();
            if (!type.IsInterface && !type.IsAbstract && Cecilx.Implements(type, Known.GregNativePlugin))
                return Array.Empty<Finding>();
        }
        return new[]
        {
            new Finding
            {
                RuleId = Id, Severity = Severity.Warning, File = ctx.File,
                Subject = ctx.AssemblyName,
                Message = "No mod identity (MelonInfo / GregMod / IGregNativePlugin).",
                FixHint = "Add [MelonInfo] (Melon mods) or [GregMod] (gregCore mods)."
            }
        };
    }
}

/// <summary>GL006 Error: only gregCore may touch GregModActivationService (.deactivated moves).</summary>
public sealed class ActivationOwnerRule : IAssemblyRule
{
    public string Id => "GL006";
    public string Title => "Only gregCore activates/deactivates mods";
    public IReadOnlyList<Finding> Check(AssemblyContext ctx)
    {
        if (string.Equals(ctx.AssemblyName, "gregCore", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<Finding>();
        var out_ = new List<Finding>();
        foreach (var type in Cecilx.AllTypes(ctx.Module))
        {
            foreach (var method in type.Methods)
            {
                foreach (var ins in Cecilx.Instructions(method))
                {
                    string? full = null;
                    try
                    {
                        full = ins.Operand switch
                        {
                            TypeReference t => t.FullName,
                            MemberReference m => m.DeclaringType?.FullName,
                            _ => null
                        };
                    }
                    catch { continue; }
                    if (!string.IsNullOrEmpty(full) && full.Contains(Known.ActivationService, StringComparison.Ordinal))
                    {
                        out_.Add(new Finding
                        {
                            RuleId = Id, Severity = Severity.Error, File = ctx.File,
                            Subject = Cecilx.Display(method),
                            Message = "Only gregCore may call GregModActivationService (.deactivated moves).",
                            FixHint = "Remove the call; ask the user to enable/disable via the Mod Hub."
                        });
                        goto NextMethod;
                    }
                }
            NextMethod:;
            }
        }
        return out_;
    }
}

/// <summary>GL007 Warning: hard-coded .deactivated paths (loaders must never read them).</summary>
public sealed class NoDeactivatedPathRule : IAssemblyRule
{
    public string Id => "GL007";
    public string Title => "No hard-coded .deactivated paths";
    public IReadOnlyList<Finding> Check(AssemblyContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var type in Cecilx.AllTypes(ctx.Module))
        {
            foreach (var method in type.Methods)
            {
                foreach (var ins in Cecilx.Instructions(method))
                {
                    if (ins.OpCode != OpCodes.Ldstr) continue;
                    if (ins.Operand is not string s) continue;
                    if (s.IndexOf(".deactivated", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    out_.Add(new Finding
                    {
                        RuleId = Id, Severity = Severity.Warning, File = ctx.File,
                        Subject = Cecilx.Display(method),
                        Message = $"Hard-coded .deactivated path {s.Truncate(80)}; loaders must never read it.",
                        FixHint = "Load from the active location only; activation is gregCore's job."
                    });
                    goto NextMethod;
                }
            NextMethod:;
            }
        }
        return out_;
    }
}

internal static class Strings
{
    public static string Truncate(this string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
        return s.Substring(0, max) + "…";
    }
}
