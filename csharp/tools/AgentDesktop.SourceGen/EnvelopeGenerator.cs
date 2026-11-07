using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AgentDesktop.SourceGen;

/// <summary>Emits the JSON writer for every type marked `EnvelopeSerialize`, appending each
/// field at its declared position. The envelope's byte order is the type's field order, and
/// nothing on the envelope path asks a runtime type model what a type contains.</summary>
[Generator]
public sealed class EnvelopeGenerator : IIncrementalGenerator
{
    private const string AttributeName = "AgentDesktop.Core.Response.EnvelopeSerializeAttribute";

    private const string FieldAttributeName = "AgentDesktop.Core.Response.EnvelopeFieldAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider
            .ForAttributeWithMetadataName<Model>(AttributeName, (node, _) => node is TypeDeclarationSyntax, (ctx, _) => Describe(ctx))
            .Where(model => !model.IsEmpty);
        context.RegisterSourceOutput(types, (spc, model) => spc.AddSource(model.FileName, SourceText.From(model.Source, Encoding.UTF8)));
    }

    private static Model Describe(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
        {
            return new Model(string.Empty, string.Empty, string.Empty) { IsEmpty = true };
        }

        var model = new Model(
            type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            Keyword(type));

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol property
                || property.IsStatic
                || property.GetMethod is null
                || !SymbolEqualityComparer.Default.Equals(property.ContainingType, type))
            {
                continue;
            }

            var field = DescribeField(property);
            if (field is not null)
            {
                model.Fields.Add(field);
            }
        }

        return model;
    }

    private static string Keyword(INamedTypeSymbol type) =>
        type.IsRecord && type.IsValueType ? "record struct" : type.IsRecord ? "record" : type.IsValueType ? "struct" : "class";

    private static Field? DescribeField(IPropertySymbol property)
    {
        var mode = Mode.Standard;
        string? jsonName = null;
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != FieldAttributeName)
            {
                continue;
            }

            var arguments = attribute.ConstructorArguments.ToList();
            if (arguments.Count > 0 && arguments[0].Value is int value)
            {
                mode = (Mode)value;
            }

            if (arguments.Count > 1 && arguments[1].Value is string name)
            {
                jsonName = name;
            }
        }

        if (mode == Mode.Skip)
        {
            return null;
        }

        var type = property.Type;
        var nullable = false;
        if (IsNullableValue(type))
        {
            nullable = true;
            type = ((INamedTypeSymbol)type).TypeArguments[0];
        }
        else if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            nullable = true;
        }

        var kind = Classify(type, out var display);
        if (kind == Kind.Unsupported)
        {
            return null;
        }

        var field = new Field(property.Name, jsonName ?? EnvelopeEmitter.Snake(property.Name), mode, kind, nullable, IsNullableValue(property.Type), display);
        if (kind == Kind.Enum)
        {
            field.EnumMembers = ((INamedTypeSymbol)type).GetMembers().Where(m => m is IFieldSymbol f && f.IsStatic).Select(m => m.Name).ToList();
        }

        return field;
    }

    private static bool IsNullableValue(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.IsGenericType
        && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static Kind Classify(ITypeSymbol type, out string display)
    {
        display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return Kind.String;
            case SpecialType.System_Boolean:
                return Kind.Bool;
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                return Kind.Number;
            case SpecialType.System_Double:
            case SpecialType.System_Single:
                return Kind.Double;
        }

        if (type is not INamedTypeSymbol named)
        {
            return Kind.Unsupported;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return Kind.Enum;
        }

        if (named.ContainingNamespace.ToDisplayString() == "System.Text.Json.Nodes" && named.Name == "JsonNode")
        {
            return Kind.Raw;
        }

        if (named.IsGenericType && named.Name == "List")
        {
            var elementKind = Classify(named.TypeArguments[0], out var elementDisplay);
            display = elementDisplay;
            return elementKind switch
            {
                Kind.String => Kind.ListString,
                Kind.Nested => Kind.ListNested,
                _ => Kind.Unsupported,
            };
        }

        return HasEnvelopeAttribute(named) ? Kind.Nested : Kind.Unsupported;
    }

    private static bool HasEnvelopeAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == AttributeName)
            {
                return true;
            }
        }

        return false;
    }

}
