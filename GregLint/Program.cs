// greglint — static analysis + auto-fix for gregCore mods.
// Offline, deterministic, never loads assemblies (Mono.Cecil read-only).
//
// Usage: greglint [--format text|json] [--severity-floor info|warning|error]
//                 [--fix] [--rules GL001,GL002,...] <path>...
// Exit: 0 = clean, 1 = findings at/above floor, 2 = usage/IO error.

using GregLint;

internal static class Program
{
    private const string Version = "1.0.0";

    public static int Main(string[] args)
    {
        string format = "text";
        string floor = "error";
        bool fix = false;
        HashSet<string>? only = null;
        var paths = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--format" when i + 1 < args.Length:
                    format = args[++i].ToLowerInvariant();
                    break;
                case "--severity-floor" when i + 1 < args.Length:
                    floor = args[++i].ToLowerInvariant();
                    break;
                case "--fix":
                    fix = true;
                    break;
                case "--rules" when i + 1 < args.Length:
                    only = new HashSet<string>(
                        args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        StringComparer.OrdinalIgnoreCase);
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(Usage());
                    return 0;
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count == 0 || (format != "text" && format != "json"))
        {
            Console.Error.WriteLine(Usage());
            return 2;
        }

        Severity floorSev = floor switch
        {
            "info" => Severity.Info,
            "warning" => Severity.Warning,
            _ => Severity.Error,
        };

        var engine = new Engine();
        if (only != null)
        {
            var known = new HashSet<string>(engine.AssemblyRules.Select(r => r.Id)
                .Concat(engine.TreeRules.Select(r => r.Id)), StringComparer.OrdinalIgnoreCase);
            foreach (var id in only)
            {
                if (!known.Contains(id))
                {
                    Console.Error.WriteLine($"greglint: unknown rule {id}");
                    return 2;
                }
            }
        }

        var all = new List<Finding>();
        var fixes = new List<string>();
        var warnings = new List<string>();
        var scannedAll = new List<string>();
        int skipped = 0;

        foreach (var path in paths)
        {
            string root = path;
            try
            {
                if (File.Exists(path)) root = Path.GetDirectoryName(Path.GetFullPath(path)) ?? path;
                else if (!Directory.Exists(path)) { Console.Error.WriteLine($"greglint: not found: {path}"); return 2; }
            }
            catch { return BadPath(path); }

            var ctx = new LintContext { Root = root, ApplyFix = fix };
            LintResult result;
            try { result = engine.Run(ctx, only); }
            catch (Exception ex) { return Fail(path, ex); }

            // Single-file mode: keep only findings for that file (+ tree findings at its dir).
            if (File.Exists(path))
            {
                string name = Path.GetFileName(path);
                all.AddRange(result.Findings.Where(f =>
                    f.File.EndsWith(name, StringComparison.OrdinalIgnoreCase) || !f.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));
            }
            else all.AddRange(result.Findings);
            fixes.AddRange(result.AppliedFixes);
            warnings.AddRange(result.Warnings);
            scannedAll.AddRange(result.ScannedFiles);
            skipped += result.SkippedFiles;
        }

        all.Sort();
        scannedAll.Sort(StringComparer.OrdinalIgnoreCase);
        var combined = new LintResult(all, scannedAll, skipped, fixes, warnings);
        Console.Write(format == "json" ? Report.Json(combined, Version) : Report.Text(combined));
        return all.Any(f => f.Severity >= floorSev) ? 1 : 0;
    }

    private static string Usage() =>
        "Usage: greglint [--format text|json] [--severity-floor info|warning|error]\n" +
        "                [--fix] [--rules GL001,GL002,...] <path>...\n" +
        "  <path>  mod folder, game Mods dir, or single .dll (file mode filters to it).\n" +
        "  --fix   normalize mod.json files (with .bak backup). IL findings are report-only.\n" +
        "Exit: 0 clean, 1 findings at/above floor, 2 usage/IO error.";

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification =
        "Defensive: Exists checks return false instead of throwing; reachable only via I/O race.")]
    private static int BadPath(string path)
    {
        Console.Error.WriteLine($"greglint: bad path: {path}");
        return 2;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification =
        "Defensive: Engine isolates failures per rule and per file; reachable only via I/O race.")]
    private static int Fail(string path, Exception ex)
    {
        Console.Error.WriteLine($"greglint: {path}: {ex.GetBaseException().Message}");
        return 2;
    }
}
