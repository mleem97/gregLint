// Engine: enumerates managed DLLs (skips .deactivated), runs rules, sorts findings.

using Mono.Cecil;
using GregLint.Rules;

namespace GregLint;

public sealed class Engine
{
    private readonly List<IAssemblyRule> _assemblyRules;
    private readonly List<ITreeRule> _treeRules;

    /// <summary>
    /// Extra rules are appended after built-ins (useful for custom
    /// project checks and for testing the per-rule failure isolation).
    /// </summary>
    public Engine(IEnumerable<IAssemblyRule>? extraAssemblyRules = null,
        IEnumerable<ITreeRule>? extraTreeRules = null)
    {
        _assemblyRules = new List<IAssemblyRule>
        {
            new NoSetQualityLevelRule(),
            new NoOnGuiRule(),
            new PrefixSignatureRule(),
            new PatchGuardRule(),
            new ModIdentityRule(),
            new ActivationOwnerRule(),
            new NoDeactivatedPathRule(),
        };
        _treeRules = new List<ITreeRule>
        {
            new ModLocationRule(),
            new DeactivatedDuplicateRule(),
            new ManifestRule(),
            new ModJsonRule(),
            new ModJsonRecommendedRule(),
        };
        if (extraAssemblyRules != null) _assemblyRules.AddRange(extraAssemblyRules);
        if (extraTreeRules != null) _treeRules.AddRange(extraTreeRules);
    }

    public IReadOnlyList<IAssemblyRule> AssemblyRules => _assemblyRules;
    public IReadOnlyList<ITreeRule> TreeRules => _treeRules;

    public LintResult Run(LintContext ctx, IReadOnlySet<string>? onlyRules = null)
    {
        var findings = new List<Finding>();
        var scanned = new List<string>();
        int skipped = 0;

        bool Wanted(string id) => onlyRules == null || onlyRules.Contains(id);

        foreach (var rule in _treeRules)
        {
            if (!Wanted(rule.Id)) continue;
            try { findings.AddRange(rule.Check(ctx)); }
            catch (Exception ex) { ctx.Warnings.Add($"{rule.Id} failed: {ex.GetBaseException().Message}"); }
        }

        var asmRules = _assemblyRules.Where(r => Wanted(r.Id)).ToArray();
        if (asmRules.Length > 0)
        {
            foreach (var file in Rules.Tree.AllFiles(ctx.Root, ".dll"))
            {
                ModuleDefinition? module = null;
                string asmName = "";
                try
                {
                    module = ModuleDefinition.ReadModule(file, new ReaderParameters { ReadSymbols = false });
                    asmName = module.Assembly?.Name?.Name ?? Path.GetFileNameWithoutExtension(file);
                }
                catch
                {
                    skipped++; // native/unmanaged or unreadable: file rules still apply
                    continue;
                }
                using (module)
                {
                    scanned.Add(file);
                    var actx = new AssemblyContext { File = Rules.Tree.Rel(ctx.Root, file), Module = module, AssemblyName = asmName };
                    foreach (var rule in asmRules)
                    {
                        try { findings.AddRange(rule.Check(actx)); }
                        catch (Exception ex) { ctx.Warnings.Add($"{rule.Id} failed on {actx.File}: {ex.GetBaseException().Message}"); }
                    }
                }
            }
        }

        findings.Sort();
        scanned.Sort(StringComparer.OrdinalIgnoreCase);
        return new LintResult(findings, scanned, skipped, new List<string>(ctx.AppliedFixes), new List<string>(ctx.Warnings));
    }
}

public sealed class LintResult
{
    public IReadOnlyList<Finding> Findings { get; }
    public IReadOnlyList<string> ScannedFiles { get; }
    public int SkippedFiles { get; }
    public IReadOnlyList<string> AppliedFixes { get; }
    public IReadOnlyList<string> Warnings { get; }

    public LintResult(IReadOnlyList<Finding> findings, IReadOnlyList<string> scanned, int skipped,
        IReadOnlyList<string> fixes, IReadOnlyList<string> warnings)
    {
        Findings = findings;
        ScannedFiles = scanned;
        SkippedFiles = skipped;
        AppliedFixes = fixes;
        Warnings = warnings;
    }

    public int Errors => Findings.Count(f => f.Severity == Severity.Error);
    public int WarningsCount => Findings.Count(f => f.Severity == Severity.Warning);
    public int Infos => Findings.Count(f => f.Severity == Severity.Info);
}
