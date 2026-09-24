// Deterministic reporters: human text + machine JSON.

using System.Text.Json;

namespace GregLint;

public static class Report
{
    public static string Text(LintResult result)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var f in result.Findings)
            sb.AppendLine(f.ToString());
        foreach (var fix in result.AppliedFixes)
            sb.AppendLine($"fixed {fix}");
        sb.AppendLine($"greglint: {result.Errors} error(s), {result.WarningsCount} warning(s), {result.Infos} info(s) " +
            $"in {result.ScannedFiles.Count} file(s), {result.SkippedFiles} skipped.");
        return sb.ToString();
    }

    public static string Json(LintResult result, string version)
    {
        var payload = new
        {
            tool = "greglint",
            version,
            findings = result.Findings.Select(f => new
            {
                rule = f.RuleId,
                severity = f.Severity.ToString().ToLowerInvariant(),
                file = f.File,
                subject = f.Subject,
                message = f.Message,
                fix = f.FixHint
            }).ToArray(),
            appliedFixes = result.AppliedFixes.ToArray(),
            warnings = result.Warnings.ToArray(),
            summary = new
            {
                errors = result.Errors,
                warnings = result.WarningsCount,
                infos = result.Infos,
                scannedFiles = result.ScannedFiles.Count,
                skippedFiles = result.SkippedFiles
            }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
