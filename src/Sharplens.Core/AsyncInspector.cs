using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sharplens.Core;

public sealed record StateMachineField(string Name, string Type, string Note);

public sealed record StateMachineMethod(string Name, string Signature);

public sealed record AsyncInspection(
    bool Success,
    IReadOnlyList<string> Diagnostics,
    string? MethodName,
    string? StateMachineType,
    bool IsStruct,
    IReadOnlyList<StateMachineField> Fields,
    IReadOnlyList<StateMachineMethod> Methods,
    IReadOnlyList<string> Interfaces,
    int AwaitCount,
    IReadOnlyList<string> States,
    IReadOnlyList<string> AssemblyTypes);

/// <summary>
/// Compiles an async method with Roslyn, then reads the emitted metadata to find
/// the state machine the compiler generated for it.
///
/// The metadata is read with System.Reflection.Metadata rather than by loading
/// the assembly and reflecting over it. Loading throws under WebAssembly, where
/// the runtime cannot always resolve the facade references a compilation
/// produces, and it would also mean executing nothing but paying to load
/// everything.
/// </summary>
public static class AsyncInspector
{
    private static AsyncInspection Failure(IReadOnlyList<string> diagnostics, int awaitCount = 0) =>
        new(false, diagnostics, null, null, false,
            Array.Empty<StateMachineField>(), Array.Empty<StateMachineMethod>(),
            Array.Empty<string>(), awaitCount, Array.Empty<string>(), Array.Empty<string>());

    public static AsyncInspection Inspect(string methodSource)
    {
        var source = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class Demo
{
""" + methodSource + """
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "DemoAssembly",
            new[] { tree },
            Basic.Reference.Assemblies.Net80.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var awaitCount = tree.GetRoot().DescendantNodes().OfType<AwaitExpressionSyntax>().Count();

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        var diagnostics = emit.Diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();

        if (!emit.Success) return Failure(diagnostics, awaitCount);

        stream.Position = 0;
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return Failure(new[] { "the compiler produced no metadata" }, awaitCount);

        var reader = peReader.GetMetadataReader();
        var names = new MetadataNames(reader);
        var decoder = new SignatureDecoder<string, object?>(names, reader, null);

        var types = new List<(TypeDefinitionHandle Handle, string FullName)>();
        foreach (var handle in reader.TypeDefinitions)
        {
            types.Add((handle, FullName(reader, handle)));
        }

        var machine = types
            .Select(entry => (entry.Handle, entry.FullName, Definition: reader.GetTypeDefinition(entry.Handle)))
            .FirstOrDefault(entry =>
            {
                var name = reader.GetString(entry.Definition.Name);
                return name.StartsWith('<') && name.Contains(">d__");
            });

        if (machine.Handle.IsNil)
        {
            return new AsyncInspection(true, diagnostics, null, null, false,
                Array.Empty<StateMachineField>(), Array.Empty<StateMachineMethod>(),
                Array.Empty<string>(), awaitCount, Array.Empty<string>(),
                types.Select(t => t.FullName).OrderBy(n => n).ToList());
        }

        var machineDefinition = machine.Definition;
        var machineName = reader.GetString(machineDefinition.Name);
        var methodName = machineName.TrimStart('<').Split('>')[0];

        var baseTypeName = TypeName(reader, names, machineDefinition.BaseType);
        var isStruct = baseTypeName.EndsWith("ValueType", StringComparison.Ordinal);

        var fields = new List<StateMachineField>();
        foreach (var fieldHandle in machineDefinition.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            var fieldName = reader.GetString(field.Name);
            string fieldType;

            try
            {
                var blob = reader.GetBlobReader(field.Signature);
                fieldType = decoder.DecodeFieldSignature(ref blob);
            }
            catch
            {
                fieldType = "?";
            }

            fields.Add(new StateMachineField(fieldName, Shorten(fieldType), ExplainField(fieldName)));
        }

        var methods = new List<StateMachineMethod>();
        foreach (var methodHandle in machineDefinition.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var name = reader.GetString(method.Name);
            if (name.StartsWith('<') || name == ".ctor") continue;

            string signature;
            try
            {
                var blob = reader.GetBlobReader(method.Signature);
                var decoded = decoder.DecodeMethodSignature(ref blob);
                signature = $"{Shorten(decoded.ReturnType)} {name}({string.Join(", ", decoded.ParameterTypes.Select(Shorten))})";
            }
            catch
            {
                signature = $"{name}(?)";
            }

            methods.Add(new StateMachineMethod(name, signature));
        }

        var interfaces = new List<string>();
        foreach (var implementationHandle in machineDefinition.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            var interfaceName = TypeName(reader, names, implementation.Interface);
            if (!string.IsNullOrEmpty(interfaceName)) interfaces.Add(interfaceName.Split('.').Last());
        }

        var states = new List<string> { "-1  completed" };
        states.Add(awaitCount == 0
            ? "0   start and finish, no awaits"
            : "0   start, runs to the first await");
        for (var i = 1; i <= awaitCount; i++)
        {
            states.Add($"{i}   resumes after await #{i}");
        }

        return new AsyncInspection(
            true,
            diagnostics,
            methodName,
            machineName,
            isStruct,
            fields,
            methods,
            interfaces,
            awaitCount,
            states,
            types.Select(t => t.FullName).OrderBy(n => n).ToList());
    }

    private static string TypeName(MetadataReader reader, MetadataNames names, EntityHandle handle)
    {
        if (handle.IsNil) return string.Empty;
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => names.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => names.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => names.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
            _ => string.Empty
        };
    }

    private static string Shorten(string type) => type
        .Replace("System.Runtime.CompilerServices.", string.Empty)
        .Replace("System.Threading.Tasks.", string.Empty)
        .Replace("System.Collections.Generic.", string.Empty);

    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);

        if (definition.IsNested)
        {
            return FullName(reader, definition.GetDeclaringType()) + "+" + name;
        }

        var ns = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string ExplainField(string name)
    {
        if (name.Contains("__state")) return "Which resumption point the machine is at. -1 means finished.";
        if (name.Contains("__builder")) return "The builder that owns the returned Task and completes it.";
        if (name == "<>4__this") return "The instance the method was called on, kept alive across awaits.";
        if (name.StartsWith("<>u__")) return "The awaiter for an in-flight await, so it can be resumed.";
        if (name.StartsWith("<>7__wrap")) return "Compiler bookkeeping used to track cleanup across awaits.";
        if (name.StartsWith("<>s__")) return "Saved state for a local that has to survive an await.";
        if (name.StartsWith("<>3__")) return "A local variable hoisted into the machine because it crosses an await.";
        if (name.Contains(">5__")) return "A local that lives across an await, so it had to move from the stack into a field.";
        if (name.StartsWith("<>s__")) return "Saved state for a local that has to survive an await.";
        return "Captured state.";
    }
}
