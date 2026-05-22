using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Slice.Cli.Internal;

internal sealed class TypeSchemaReader
{
    private readonly Dictionary<string, IReadOnlyList<(string Name, string Type)>> _propertiesByType;

    private TypeSchemaReader(Dictionary<string, IReadOnlyList<(string Name, string Type)>> propertiesByType)
        => _propertiesByType = propertiesByType;

    internal static TypeSchemaReader Create(IEnumerable<FileInfo> assemblyFiles)
    {
        var propertiesByType = new Dictionary<string, IReadOnlyList<(string Name, string Type)>>(StringComparer.Ordinal);

        foreach (var assemblyFile in assemblyFiles)
        {
            try
            {
                ReadFromAssembly(assemblyFile, propertiesByType);
            }
            catch (BadImageFormatException)
            {
            }
            catch (IOException)
            {
            }
        }

        return new TypeSchemaReader(propertiesByType);
    }

    internal IReadOnlyList<(string Name, string Type)> ReadPublicProperties(string qualifiedTypeName)
    {
        var normalized = qualifiedTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? qualifiedTypeName["global::".Length..]
            : qualifiedTypeName;

        return _propertiesByType.TryGetValue(normalized, out var properties) ? properties : [];
    }

    private static void ReadFromAssembly(
        FileInfo assemblyFile,
        Dictionary<string, IReadOnlyList<(string Name, string Type)>> propertiesByType)
    {
        using var stream = new FileStream(
            assemblyFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return;
        }

        var reader = peReader.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var fqn = BuildFullTypeName(reader, typeHandle);
            if (propertiesByType.ContainsKey(fqn))
            {
                continue;
            }

            var properties = ReadProperties(reader, typeHandle);
            if (properties.Count > 0)
            {
                propertiesByType.Add(fqn, properties);
            }
        }
    }

    private static string BuildFullTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        if (type.IsNested)
        {
            var enclosing = BuildFullTypeName(reader, type.GetDeclaringType());
            return enclosing + "." + reader.GetString(type.Name);
        }

        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static List<(string Name, string Type)> ReadProperties(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        var result = new List<(string, string)>();

        foreach (var propHandle in type.GetProperties())
        {
            var prop = reader.GetPropertyDefinition(propHandle);
            var accessors = prop.GetAccessors();
            if (accessors.Getter.IsNil)
            {
                continue;
            }

            var getter = reader.GetMethodDefinition(accessors.Getter);
            if (!getter.Attributes.HasFlag(MethodAttributes.Public))
            {
                continue;
            }

            var propName = reader.GetString(prop.Name);
            try
            {
                var sig = prop.DecodeSignature(SignatureTypeProvider.Instance, 0);
                result.Add((propName, sig.ReturnType));
            }
            catch (BadImageFormatException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return result;
    }

    private sealed class SignatureTypeProvider : ISignatureTypeProvider<string, int>
    {
        internal static readonly SignatureTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.UIntPtr => "nuint",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => BuildFullTypeName(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => BuildTypeReferenceName(reader, handle);

        private static string BuildTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return BuildTypeReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope) + "." + name;
            }

            var ns = reader.GetString(type.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, int genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            var name = genericType.Contains('`') ? genericType[..genericType.IndexOf('`')] : genericType;
            return name + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(int genericContext, int index) => $"T{index}";

        public string GetGenericTypeParameter(int genericContext, int index) => $"T{index}";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
    }
}
