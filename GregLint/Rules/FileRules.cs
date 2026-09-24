// File/tree rules GL101–GL105 (no game needed, deterministic).
// Manifest validation mirrors GregModpackManifest (gregCore.Core.Mods);
// this tool is standalone so the logic lives here too.

using System.Text.Json;
using Mono.Cecil;

namespace GregLint.Rules;

/// <summary>GL101 Error: MelonMod DLL below Mods root (must be top-level; gregNative excepted).</summary>
public sealed class ModLocationRule : ITreeRule
{
    public string Id => "GL101";
    public string Title => "Melon mods live directly in Mods/";
    public IReadOnlyList<Finding> Check(LintContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var file in Tree.AllFiles(ctx.Root, ".dll"))
        {
            string? dir = null;
            try { dir = Path.GetDirectoryName(Path.GetFullPath(file)); } catch { continue; }
            string root;
            try { root = Path.GetFullPath(ctx.Root); } catch { continue; }
            if (string.Equals(dir, root, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                continue;
            if (Cecilx.PathHasSegment(file, "gregNative")) continue;
            if (!IsMelonMod(file)) continue;
            out_.Add(new Finding
            {
                RuleId = Id, Severity = Severity.Error, File = Rel(ctx.Root, file),
                Subject = Path.GetFileName(file),
                Message = "Melon mods must live directly in Mods/ (no subfolders).",
                FixHint = "Move the DLL to Mods/ top level (or ship deps via gregDependencies)."
            });
        }
        return out_;
    }

    private static bool IsMelonMod(string file)
    {
        try
        {
            using var module = ModuleDefinition.ReadModule(file, new ReaderParameters { ReadSymbols = false });
            foreach (var type in Cecilx.AllTypes(module))
            {
                if (type.IsInterface || type.IsAbstract) continue;
                if (Cecilx.DerivesFrom(type, Known.MelonModBase)) return true;
            }
        }
        catch { /* not managed or unreadable */ }
        return false;
    }

    private static string Rel(string root, string file)
    {
        try { return Path.GetRelativePath(root, file); } catch { return file; }
    }
}

/// <summary>GL102 Warning: same filename active AND deactivated (disabled mod still loads).</summary>
public sealed class DeactivatedDuplicateRule : ITreeRule
{
    public string Id => "GL102";
    public string Title => "No duplicate active copy of deactivated mods";
    public IReadOnlyList<Finding> Check(LintContext ctx)
    {
        var out_ = new List<Finding>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Tree.AllFiles(ctx.Root, ".dll"))
            active.Add(Path.GetFileName(file));
        foreach (var file in Tree.DeactivatedFiles(ctx.Root, ".dll"))
        {
            if (active.Contains(Path.GetFileName(file)))
                out_.Add(new Finding
                {
                    RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, file),
                    Subject = Path.GetFileName(file),
                    Message = "Deactivated copy exists, but an active copy with the same name still loads.",
                    FixHint = "Remove the duplicate: keep exactly one copy (active or .deactivated)."
                });
        }
        return out_;
    }
}

/// <summary>GL103 Mods/manifest.json validation (missing=Warning, broken=Error).</summary>
public sealed class ManifestRule : ITreeRule
{
    public string Id => "GL103";
    public string Title => "Modpack manifest valid";
    public IReadOnlyList<Finding> Check(LintContext ctx)
    {
        var out_ = new List<Finding>();
        string path = Path.Combine(ctx.Root, "manifest.json");
        if (!File.Exists(path)) return out_;
        Dictionary<string, JsonElement>? doc;
        try
        {
            doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            out_.Add(Err(path, "manifest", $"manifest.json is not valid JSON: {ex.GetBaseException().Message}.", "Fix the JSON syntax."));
            return out_;
        }
        if (doc == null) return out_;
        foreach (var section in new[] { "Mods", "Library", "Plugins" })
        {
            if (!doc.TryGetValue(section, out var el) && !doc.TryGetValue(section.ToLowerInvariant(), out el))
                continue;
            if (el.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                CheckEntry(ctx, path, section, item.GetString() ?? "", out_);
            }
        }
        return out_;
    }

    private void CheckEntry(LintContext ctx, string manifest, string section, string entry, List<Finding> out_)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            out_.Add(new Finding { RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, manifest), Subject = section, Message = "Empty entry.", FixHint = "Remove it." });
            return;
        }
        string root = Path.GetFullPath(ctx.Root);
        string rel = entry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (rel.IndexOf('\0') >= 0)
        {
            out_.Add(Err(manifest, section, $"'{entry}' is not a valid path.", "Fix the path."));
            return;
        }
        string full = Path.GetFullPath(Path.Combine(root, rel));
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            out_.Add(Err(manifest, section, $"'{entry}' escapes Mods/ (traversal).", "Keep entries inside Mods/."));
            return;
        }
        if (Cecilx.PathHasSegment(full, ".deactivated"))
        {
            out_.Add(Err(manifest, section, $"'{entry}' points into .deactivated (never loaded).", "Activate the mod first, then reference it."));
            return;
        }
        if (!full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            out_.Add(new Finding { RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, manifest), Subject = section, Message = $"'{entry}' is not a .dll.", FixHint = "Reference DLLs only." });
            return;
        }
        if (!File.Exists(full))
            out_.Add(new Finding { RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, manifest), Subject = section, Message = $"'{entry}' not found.", FixHint = "Ship the file or drop the entry." });
    }

    private Finding Err(string manifest, string subject, string message, string hint) =>
        new() { RuleId = Id, Severity = Severity.Error, File = manifest, Subject = subject, Message = message, FixHint = hint };
}

/// <summary>Shared mod.json reader (schema + recommended split across GL104/GL105).</summary>
internal static class ModJson
{
    public sealed record Doc(string Dir, string Manifest, string Raw, Dictionary<string, JsonElement> Data);

    public enum Outcome { Missing, Ok, Invalid, Unreadable }

    public static Outcome TryRead(string dir, out Doc? doc, out string error)
    {
        doc = null;
        error = "";
        string manifest = Path.Combine(dir, "mod.json");
        if (!File.Exists(manifest)) return Outcome.Missing;
        string raw;
        try { raw = File.ReadAllText(manifest); }
        catch (Exception ex) { error = ex.GetBaseException().Message; return Outcome.Unreadable; }
        Dictionary<string, JsonElement>? data;
        try { data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw); }
        catch (Exception ex) { error = ex.GetBaseException().Message; return Outcome.Invalid; }
        if (data == null) return Outcome.Ok;
        doc = new Doc(dir, manifest, raw, data);
        return Outcome.Ok;
    }

    public static string Str(Dictionary<string, JsonElement> doc, string key) =>
        doc.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
}

/// <summary>GL104 mod.json schema (id/entry required) + --fix normalization.</summary>
public sealed class ModJsonRule : ITreeRule
{
    public string Id => "GL104";
    public string Title => "mod.json schema + normalization";
    public IReadOnlyList<Finding> Check(LintContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var dir in Tree.AllDirs(ctx.Root))
        {
            string manifest = Path.Combine(dir, "mod.json");
            if (!File.Exists(manifest)) continue;
            var outcome = ModJson.TryRead(dir, out var doc, out var error);
            if (outcome == ModJson.Outcome.Missing) continue;
            if (doc == null)
            {
                if (outcome == ModJson.Outcome.Invalid)
                    out_.Add(new Finding { RuleId = Id, Severity = Severity.Error, File = Tree.Rel(ctx.Root, manifest), Subject = "mod.json", Message = $"Invalid JSON: {error}.", FixHint = "Fix the JSON syntax." });
                else if (outcome == ModJson.Outcome.Unreadable)
                    out_.Add(new Finding { RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, manifest), Subject = "mod.json", Message = $"Exists but cannot be read: {error}.", FixHint = "Check file permissions." });
                continue;
            }
            if (string.IsNullOrWhiteSpace(ModJson.Str(doc.Data, "id")))
                out_.Add(new Finding { RuleId = Id, Severity = Severity.Error, File = Tree.Rel(ctx.Root, manifest), Subject = "id", Message = "Missing required \"id\".", FixHint = "Set a unique lowercase id (folder name)." });
            string entry = ModJson.Str(doc.Data, "entry");
            if (string.IsNullOrWhiteSpace(entry)) entry = ModJson.Str(doc.Data, "entrypoint");
            if (string.IsNullOrWhiteSpace(entry)) entry = "main.lua";
            if (!File.Exists(Path.Combine(dir, entry)))
                out_.Add(new Finding { RuleId = Id, Severity = Severity.Error, File = Tree.Rel(ctx.Root, manifest), Subject = "entry", Message = $"Entry file \"{entry}\" not found.", FixHint = "Ship the entry file or fix the entry field." });
            if (ctx.ApplyFix)
                Normalize(ctx, doc, out_);
        }
        return out_;
    }

    private void Normalize(LintContext ctx, ModJson.Doc doc, List<Finding> out_)
    {
        try
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var key in doc.Data.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    doc.Data[key].WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            string normalized = System.Text.Encoding.UTF8.GetString(ms.ToArray()) + Environment.NewLine;
            string current = doc.Raw.Replace("\r\n", "\n");
            if (normalized.Replace("\r\n", "\n") == current) return;
            Tree.Backup(doc.Manifest);
            File.WriteAllText(doc.Manifest, normalized);
            ctx.Fixed($"normalized {Tree.Rel(ctx.Root, doc.Manifest)} (backup .bak)");
        }
        catch (Exception ex)
        {
            out_.Add(new Finding { RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, doc.Manifest), Subject = "mod.json", Message = $"Auto-fix failed: {ex.GetBaseException().Message}.", FixHint = "Normalize manually." });
        }
    }
}

/// <summary>GL105 mod.json recommended fields (name/version).</summary>
public sealed class ModJsonRecommendedRule : ITreeRule
{
    public string Id => "GL105";
    public string Title => "mod.json recommended fields";
    public IReadOnlyList<Finding> Check(LintContext ctx)
    {
        var out_ = new List<Finding>();
        foreach (var dir in Tree.AllDirs(ctx.Root))
        {
            string manifest = Path.Combine(dir, "mod.json");
            if (!File.Exists(manifest)) continue;
            if (ModJson.TryRead(dir, out var doc, out _) != ModJson.Outcome.Ok || doc == null) continue;
            if (string.IsNullOrWhiteSpace(ModJson.Str(doc.Data, "name")))
                out_.Add(new Finding { RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, manifest), Subject = "name", Message = "Missing recommended \"name\".", FixHint = "Add a display name." });
            if (string.IsNullOrWhiteSpace(ModJson.Str(doc.Data, "version")))
                out_.Add(new Finding { RuleId = Id, Severity = Severity.Warning, File = Tree.Rel(ctx.Root, manifest), Subject = "version", Message = "Missing recommended \"version\".", FixHint = "Add a SemVer version." });
        }
        return out_;
    }
}

/// <summary>Deterministic tree enumeration (excludes .deactivated unless asked).</summary>
internal static class Tree
{
    public static IEnumerable<string> AllFiles(string root, string extension)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Enumerable.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !Cecilx.PathHasSegment(f, ".deactivated"))
                .Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return Enumerable.Empty<string>(); }
    }

    public static IEnumerable<string> DeactivatedFiles(string root, string extension)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Enumerable.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => Cecilx.PathHasSegment(f, ".deactivated"))
                .Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return Enumerable.Empty<string>(); }
    }

    public static IEnumerable<string> AllDirs(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Enumerable.Empty<string>();
        try
        {
            return new[] { root }.Concat(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                    .Where(d => !Cecilx.PathHasSegment(d, ".deactivated")))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return Enumerable.Empty<string>(); }
    }

    public static string Rel(string root, string file) => Path.GetRelativePath(root, file);

    public static void Backup(string file)
    {
        string bak = file + ".bak";
        int n = 1;
        while (File.Exists(bak) && n < 100) bak = $"{file}.bak.{n++}";
        File.Copy(file, bak, overwrite: false);
    }
}
