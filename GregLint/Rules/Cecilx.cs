// Shared Mono.Cecil helpers: type walk, attribute names, body scan.
// Read-only; never resolves or loads assemblies.

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GregLint.Rules;

internal static class Cecilx
{
    public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var t in module.Types)
            foreach (var n in Walk(t))
                yield return n;
    }

    private static IEnumerable<TypeDefinition> Walk(TypeDefinition type)
    {
        yield return type;
        foreach (var n in type.NestedTypes)
            foreach (var x in Walk(n))
                yield return x;
    }

    public static bool HasAttribute(ICustomAttributeProvider provider, string attributeFullName)
    {
        if (provider?.HasCustomAttributes != true) return false;
        try
        {
            foreach (var a in provider.CustomAttributes)
            {
                string? name = null;
                try { name = a.AttributeType?.FullName; } catch { continue; }
                if (string.Equals(name, attributeFullName, StringComparison.Ordinal))
                    return true;
            }
        }
        catch { /* best-effort */ }
        return false;
    }

    /// <summary>
    /// True when <paramref name="type"/> derives (directly or transitively)
    /// from <paramref name="baseFullName"/>, resolved or by name.
    /// </summary>
    public static bool DerivesFrom(TypeDefinition type, string baseFullName, int depth = 8)
    {
        try
        {
            var b = type.BaseType;
            for (int i = 0; b != null && i < depth; i++)
            {
                string? name = null;
                try { name = b.FullName; } catch { break; }
                if (string.Equals(name, baseFullName, StringComparison.Ordinal))
                    return true;
                TypeDefinition? resolved = null;
                try { resolved = b.Resolve(); } catch { break; }
                if (resolved == null) break;
                b = resolved.BaseType;
            }
        }
        catch { /* best-effort */ }
        return false;
    }

    public static bool Implements(TypeDefinition type, string interfaceFullName)
    {
        try
        {
            foreach (var i in type.Interfaces)
            {
                string? name = null;
                try { name = i.InterfaceType?.FullName; } catch { continue; }
                if (string.Equals(name, interfaceFullName, StringComparison.Ordinal))
                    return true;
            }
        }
        catch { /* best-effort */ }
        return false;
    }

    public static IEnumerable<Instruction> Instructions(MethodDefinition method)
    {
        // HasBody is false for abstract/P/Invoke stubs (no body to read).
        // A throwing Body getter means corrupt metadata: let it propagate
        // to the engine, which isolates + reports per rule.
        if (method?.HasBody != true || method.Body?.Instructions == null)
            return Enumerable.Empty<Instruction>();
        return method.Body.Instructions;
    }

    public static bool HasExceptionHandler(MethodDefinition method)
    {
        if (method?.HasBody != true) return false;
        return method.Body.ExceptionHandlers.Count > 0;
    }

    public static string Display(MethodDefinition method)
    {
        if (method == null) return "?";
        return $"{method.DeclaringType?.FullName}::{method.Name}";
    }

    public static bool PathHasSegment(string path, string segment)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(segment)) return false;
        return path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Any(s => string.Equals(s, segment, StringComparison.OrdinalIgnoreCase));
    }
}
