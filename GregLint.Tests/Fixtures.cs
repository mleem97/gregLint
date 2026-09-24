// In-memory Cecil fixture builder: crafts managed test assemblies
// (types, attributes, IL) and writes them to temp .dll files.
// Fully offline — no compilation, no game assemblies.

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GregLint.Tests;

internal sealed class Fixture : IDisposable
{
    public string Dir { get; }
    public AssemblyDefinition Assembly { get; }
    public ModuleDefinition Module => Assembly.MainModule;

    private Fixture(string dir, AssemblyDefinition asm)
    {
        Dir = dir;
        Assembly = asm;
    }

    public static Fixture Create(string assemblyName = "TestMod")
    {
        var dir = Path.Combine(Path.GetTempPath(), "greglint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)), assemblyName, ModuleKind.Dll);
        return new Fixture(dir, asm);
    }

    public TypeDefinition AddClass(string ns, string name, TypeDefinition? baseType = null)
    {
        var type = new TypeDefinition(ns, name,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            baseType ?? Module.TypeSystem.Object);
        Module.Types.Add(type);
        return type;
    }

    public TypeDefinition AddAttributeClass(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        var type = new TypeDefinition(fullName.Substring(0, dot), fullName.Substring(dot + 1),
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            Module.ImportReference(typeof(Attribute)));
        var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, Module.TypeSystem.Void);
        // Body never executes under linting (metadata only); plain Ret keeps Cecil happy.
        ctor.Body.GetILProcessor().Emit(OpCodes.Ret);
        type.Methods.Add(ctor);
        Module.Types.Add(type);
        return type;
    }

    public void AddAttribute(ICustomAttributeProvider target, TypeDefinition attr)
    {
        var ctor = attr.Methods.First(m => m.IsConstructor && !m.HasParameters);
        target.CustomAttributes.Add(new CustomAttribute(Module.ImportReference(ctor)));
    }

    /// <summary>Adds a static method with the given body emitter. Returns the method.</summary>
    public MethodDefinition AddMethod(TypeDefinition type, string name, TypeReference? ret = null,
        Action<ILProcessor>? emit = null)
    {
        var method = new MethodDefinition(name,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            ret ?? Module.TypeSystem.Void);
        var il = method.Body.GetILProcessor();
        (emit ?? (e => e.Emit(OpCodes.Ret))).Invoke(il);
        type.Methods.Add(method);
        return method;
    }

    public void AddTryCatch(MethodDefinition method)
    {
        method.Body.Instructions.Clear();
        var il = method.Body.GetILProcessor();
        var nop = il.Create(OpCodes.Nop);
        var pop = il.Create(OpCodes.Pop);
        var ret = il.Create(OpCodes.Ret);
        var end = il.Create(OpCodes.Nop);
        var leave = il.Create(OpCodes.Leave, ret);
        il.Append(nop);
        il.Append(leave);
        il.Append(pop);
        il.Append(ret);
        il.Append(end);
        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = nop,
            TryEnd = pop,
            HandlerStart = pop,
            HandlerEnd = end,
            CatchType = Module.ImportReference(typeof(Exception)),
        });
    }

    /// <summary>
    /// A TypeReference whose FullName always throws: plants genuine
    /// metadata-read failures to prove the lint guards hold.
    /// </summary>
    public sealed class BoomRef : TypeReference
    {
        public BoomRef(ModuleDefinition module)
            : base("Boom", "Boom", module, null)
        {
        }

        public override string FullName => throw new InvalidOperationException("boom");
    }

    /// <summary>Writes the assembly to Dir/name and returns the full path.</summary>
    public string Write(string name = "TestMod.dll")
    {
        string path = Path.Combine(Dir, name);
        Assembly.Write(path);
        return path;
    }

    public void Dispose()
    {
        try { Assembly.Dispose(); } catch { }
        try { Directory.Delete(Dir, true); } catch { }
    }
}
