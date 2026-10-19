using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// Reads an assembly's metadata and IL — without loading it — and reports every type that references a Consolidated Risk
/// Index type (S39 §1.3; S40 §11.5): in a signature (field, property, method, base, interface, local) or in a method body
/// (a call, a field access, a <c>typeof</c>, a generic argument), lambdas and async/iterator state machines attributed to
/// the type that declares them.
/// </summary>
internal static class IcrReferenceScanner
{
    /// <summary>One type outside the allowlist that names the index, and what it names.</summary>
    public sealed record Violation(string Assembly, string Owner, string Reference)
    {
        public override string ToString() => $"{Assembly}: {Owner} -> {Reference}";
    }

    /// <summary>The namespaces that are the index (S39 §1.3): its service, its model and its jobs.</summary>
    public static readonly string[] IndexNamespaces = ["ServerServices.RiskIndex", "Model.RiskIndex", "BackgroundJobs.Jobs.RiskIndex"];

    /// <summary>
    /// Whether <paramref name="fullName"/> is an index type: in an index namespace, or named <c>RiskIndex…</c> /
    /// <c>IRiskIndex…</c> anywhere else (the DAL entities and enums, the wave-4 controllers and REST clients). EF's
    /// migration classes share the prefix and are not the index.
    /// </summary>
    public static bool IsIndexType(string fullName)
    {
        var (ns, name) = Split(fullName);
        if (IndexNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal))) return true;
        if (ns == "DAL.Migrations") return false;
        return name.StartsWith("RiskIndex", StringComparison.Ordinal) || name.StartsWith("IRiskIndex", StringComparison.Ordinal);
    }

    public static IReadOnlyList<Violation> Scan(string assemblyPath, Func<string, string, bool> ownerAllowed)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var assembly = md.GetString(md.GetAssemblyDefinition().Name);
        var provider = new NameProvider();
        var violations = new List<Violation>();

        foreach (var handle in md.TypeDefinitions)
        {
            var owner = Outermost(md, handle);
            if (ownerAllowed(assembly, owner)) continue;

            provider.Sink.Clear();
            CollectType(md, pe, handle, provider);

            violations.AddRange(provider.Sink.Where(IsIndexType).Distinct().OrderBy(n => n, StringComparer.Ordinal)
                .Select(reference => new Violation(assembly, owner, reference)));
        }

        return violations.Distinct().ToList();
    }

    private static void CollectType(MetadataReader md, PEReader pe, TypeDefinitionHandle handle, NameProvider provider)
    {
        var type = md.GetTypeDefinition(handle);
        if (!type.BaseType.IsNil) Record(md, type.BaseType, provider);
        foreach (var i in type.GetInterfaceImplementations()) Record(md, md.GetInterfaceImplementation(i).Interface, provider);

        foreach (var f in type.GetFields()) md.GetFieldDefinition(f).DecodeSignature(provider, null);
        foreach (var p in type.GetProperties()) md.GetPropertyDefinition(p).DecodeSignature(provider, null);

        foreach (var m in type.GetMethods())
        {
            var method = md.GetMethodDefinition(m);
            method.DecodeSignature(provider, null);
            if (method.RelativeVirtualAddress == 0) continue;

            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            if (!body.LocalSignature.IsNil)
                md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(provider, null);
            CollectIl(md, body.GetILReader(), provider);
        }
    }

    private static readonly IReadOnlyDictionary<int, OperandType> Operands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => (int)(ushort)o.Value, o => o.OperandType);

    private static void CollectIl(MetadataReader md, BlobReader il, NameProvider provider)
    {
        while (il.RemainingBytes > 0)
        {
            int code = il.ReadByte();
            if (code == 0xFE) code = 0xFE00 | il.ReadByte();
            if (!Operands.TryGetValue(code, out var operand))
                throw new InvalidOperationException($"Unknown IL opcode 0x{code:X}");

            switch (operand)
            {
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    Record(md, MetadataTokens.EntityHandle(il.ReadInt32()), provider);
                    break;
                case OperandType.InlineSwitch:
                    var targets = il.ReadInt32();
                    il.Offset += 4 * targets;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    il.Offset += 1;
                    break;
                case OperandType.InlineVar:
                    il.Offset += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    il.Offset += 8;
                    break;
                default: // InlineBrTarget, InlineI, InlineSig, InlineString, ShortInlineR
                    il.Offset += 4;
                    break;
            }
        }
    }

    /// <summary>Records the types a token names: the type itself, a member's declaring type and signature, generic arguments.</summary>
    private static void Record(MetadataReader md, EntityHandle handle, NameProvider provider)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                provider.Sink.Add(TypeName(md, (TypeDefinitionHandle)handle));
                break;
            case HandleKind.TypeReference:
                provider.Sink.Add(TypeName(md, (TypeReferenceHandle)handle));
                break;
            case HandleKind.TypeSpecification:
                md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(provider, null);
                break;
            case HandleKind.MethodDefinition:
            {
                var method = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                provider.Sink.Add(TypeName(md, method.GetDeclaringType()));
                method.DecodeSignature(provider, null);
                break;
            }
            case HandleKind.FieldDefinition:
            {
                var field = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                provider.Sink.Add(TypeName(md, field.GetDeclaringType()));
                field.DecodeSignature(provider, null);
                break;
            }
            case HandleKind.MemberReference:
            {
                var member = md.GetMemberReference((MemberReferenceHandle)handle);
                if (member.Parent.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    or HandleKind.MethodDefinition)
                    Record(md, (EntityHandle)member.Parent, provider);
                if (member.GetKind() == MemberReferenceKind.Method) member.DecodeMethodSignature(provider, null);
                else member.DecodeFieldSignature(provider, null);
                break;
            }
            case HandleKind.MethodSpecification:
            {
                var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                Record(md, spec.Method, provider);
                spec.DecodeSignature(provider, null);
                break;
            }
            case HandleKind.StandaloneSignature:
                break;
        }
    }

    private static string Outermost(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        while (!type.GetDeclaringType().IsNil)
        {
            handle = type.GetDeclaringType();
            type = md.GetTypeDefinition(handle);
        }

        return TypeName(md, handle);
    }

    private static string TypeName(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var name = md.GetString(type.Name);
        if (!type.GetDeclaringType().IsNil) return TypeName(md, type.GetDeclaringType()) + "+" + name;
        var ns = md.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string TypeName(MetadataReader md, TypeReferenceHandle handle)
    {
        var type = md.GetTypeReference(handle);
        var name = md.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeName(md, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        var ns = md.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static (string Namespace, string Name) Split(string fullName)
    {
        var outer = fullName.Split('+')[0];
        var dot = outer.LastIndexOf('.');
        var name = fullName.Contains('+') ? fullName[(fullName.LastIndexOf('+') + 1)..] : outer[(dot + 1)..];
        return (dot < 0 ? "" : outer[..dot], name);
    }

    /// <summary>Decodes signatures to type names, recording every named type it meets (generic arguments included).</summary>
    private sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        public HashSet<string> Sink { get; } = new(StringComparer.Ordinal);

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            Add(TypeName(reader, handle));

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            Add(TypeName(reader, handle));

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle,
            byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(",", typeArguments) + ">";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

        private string Add(string name)
        {
            Sink.Add(name);
            return name;
        }
    }
}
