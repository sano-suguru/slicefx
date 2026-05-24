using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Slice.Cli.Internal;

internal sealed class TypeSchemaReader
{
    private readonly Dictionary<string, IReadOnlyList<TypeSchemaProperty>> _propertiesByType;
    private readonly Dictionary<string, EnumSchemaInfo> _enumsByType;

    private TypeSchemaReader(
        Dictionary<string, IReadOnlyList<TypeSchemaProperty>> propertiesByType,
        Dictionary<string, EnumSchemaInfo> enumsByType)
    {
        _propertiesByType = propertiesByType;
        _enumsByType = enumsByType;
    }

    internal static TypeSchemaReader Create(IEnumerable<FileInfo> assemblyFiles)
    {
        var propertiesByType = new Dictionary<string, IReadOnlyList<TypeSchemaProperty>>(StringComparer.Ordinal);
        var enumsByType = new Dictionary<string, EnumSchemaInfo>(StringComparer.Ordinal);

        foreach (var assemblyFile in assemblyFiles)
        {
            try
            {
                ReadFromAssembly(assemblyFile, propertiesByType, enumsByType);
            }
            catch (BadImageFormatException)
            {
            }
            catch (IOException)
            {
            }
        }

        return new TypeSchemaReader(propertiesByType, enumsByType);
    }

    internal IReadOnlyList<(string Name, string Type)> ReadPublicProperties(string qualifiedTypeName)
        => [.. ReadPublicPropertySchemas(qualifiedTypeName)
            .Select(static property => (property.JsonName, property.Type))];

    internal IReadOnlyList<TypeSchemaProperty> ReadPublicPropertySchemas(string qualifiedTypeName)
    {
        var normalized = qualifiedTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? qualifiedTypeName["global::".Length..]
            : qualifiedTypeName;

        return _propertiesByType.TryGetValue(normalized, out var properties) ? properties : [];
    }

    internal EnumSchemaInfo? ReadEnumSchema(string qualifiedTypeName)
    {
        var normalized = qualifiedTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? qualifiedTypeName["global::".Length..]
            : qualifiedTypeName;

        return _enumsByType.TryGetValue(normalized, out var schema) ? schema : null;
    }

    private static void ReadFromAssembly(
        FileInfo assemblyFile,
        Dictionary<string, IReadOnlyList<TypeSchemaProperty>> propertiesByType,
        Dictionary<string, EnumSchemaInfo> enumsByType)
    {
        using var stream = new FileStream(
            assemblyFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return;
        }

        var reader = peReader.GetMetadataReader();
        var assemblyStringEnumConverter = HasJsonStringEnumConverter(reader, reader.GetAssemblyDefinition().GetCustomAttributes());
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var fqn = BuildFullTypeName(reader, typeHandle);
            var type = reader.GetTypeDefinition(typeHandle);
            var typeConverter = ReadJsonConverter(reader, type.GetCustomAttributes());
            if (typeConverter is JsonConverterKind.Unsupported)
            {
                throw new CliException($"Type '{fqn}' uses an unsupported System.Text.Json converter. The slice CLI cannot safely project this type into a schema.");
            }

            var typeStringEnumConverter = assemblyStringEnumConverter || typeConverter is JsonConverterKind.StringEnum;
            if (!enumsByType.ContainsKey(fqn) && IsEnum(reader, type))
            {
                var enumSchema = ReadEnumSchema(reader, typeHandle, fqn, typeStringEnumConverter);
                if (enumSchema is not null)
                {
                    enumsByType.Add(fqn, enumSchema);
                }
            }

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

    private static List<TypeSchemaProperty> ReadProperties(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        var result = new List<TypeSchemaProperty>();
        var nullableContext = ReadNullableContext(reader, type.GetCustomAttributes());
        var constructorNullableFlags = ReadConstructorNullableFlags(reader, type);
        var backingFieldNullableFlags = ReadBackingFieldNullableFlags(reader, type);

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
                var jsonIgnore = ReadJsonIgnore(reader, prop.GetCustomAttributes());
                if (jsonIgnore is JsonIgnorePolicy.Always)
                {
                    continue;
                }

                var converter = ReadJsonConverter(reader, prop.GetCustomAttributes());
                if (converter is JsonConverterKind.Unsupported)
                {
                    var typeName = BuildFullTypeName(reader, typeHandle);
                    throw new CliException($"Property '{typeName}.{propName}' uses an unsupported System.Text.Json converter. The slice CLI cannot safely project this property into a schema.");
                }

                var sig = prop.DecodeSignature(SignatureTypeProvider.Instance, 0);
                var nullableFlag = ReadNullableFlag(reader, prop.GetCustomAttributes()) ??
                    ReadNullableFlag(reader, getter.GetCustomAttributes()) ??
                    ReadMethodReturnNullableFlag(reader, getter) ??
                    ReadBackingFieldNullableFlag(backingFieldNullableFlags, propName) ??
                    ReadConstructorParameterNullableFlag(constructorNullableFlags, propName);
                var nullable = IsNullableType(sig.ReturnType) ||
                    nullableFlag is 2 ||
                    (nullableFlag is null && nullableContext is 2);
                var hasRequiredMember = HasAttribute(
                    reader,
                    prop.GetCustomAttributes(),
                    "System.Runtime.CompilerServices",
                    "RequiredMemberAttribute");
                var hasJsonRequired = HasAttribute(
                    reader,
                    prop.GetCustomAttributes(),
                    "System.Text.Json.Serialization",
                    "JsonRequiredAttribute");
                var required = HasAttribute(
                    reader,
                    prop.GetCustomAttributes(),
                    "System.Runtime.CompilerServices",
                    "RequiredMemberAttribute") || hasJsonRequired || (!nullable && jsonIgnore is not JsonIgnorePolicy.Conditional);
                var jsonName = ReadJsonPropertyName(reader, prop.GetCustomAttributes()) ?? ToJsonPropertyName(propName);
                result.Add(new TypeSchemaProperty(
                    propName,
                    jsonName,
                    sig.ReturnType,
                    required || hasRequiredMember,
                    nullable,
                    converter is JsonConverterKind.StringEnum));
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

    private static bool IsEnum(MetadataReader reader, TypeDefinition type)
    {
        var baseType = type.BaseType;
        if (baseType.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        var typeRef = reader.GetTypeReference((TypeReferenceHandle)baseType);
        return reader.StringComparer.Equals(typeRef.Namespace, "System") &&
            reader.StringComparer.Equals(typeRef.Name, "Enum");
    }

    private static EnumSchemaInfo? ReadEnumSchema(MetadataReader reader, TypeDefinitionHandle typeHandle, string typeName, bool isStringEnum)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        var names = new List<string>();
        var values = new List<long>();

        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (!field.Attributes.HasFlag(FieldAttributes.Literal) ||
                !field.Attributes.HasFlag(FieldAttributes.Static) ||
                field.Attributes.HasFlag(FieldAttributes.SpecialName))
            {
                continue;
            }

            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil)
            {
                continue;
            }

            var constant = reader.GetConstant(constantHandle);
            if (!TryReadIntegerConstant(reader, constant, out var value))
            {
                continue;
            }

            names.Add(reader.GetString(field.Name));
            values.Add(value);
        }

        return names.Count == 0 ? null : new EnumSchemaInfo(typeName, names, values, isStringEnum);
    }

    private static bool TryReadIntegerConstant(MetadataReader reader, Constant constant, out long value)
    {
        var blob = reader.GetBlobReader(constant.Value);
        if (constant.TypeCode == ConstantTypeCode.SByte)
        {
            value = blob.ReadSByte();
            return true;
        }

        if (constant.TypeCode == ConstantTypeCode.Byte)
        {
            value = blob.ReadByte();
            return true;
        }

        if (constant.TypeCode == ConstantTypeCode.Int16)
        {
            value = blob.ReadInt16();
            return true;
        }

        if (constant.TypeCode == ConstantTypeCode.UInt16)
        {
            value = blob.ReadUInt16();
            return true;
        }

        if (constant.TypeCode == ConstantTypeCode.Int32)
        {
            value = blob.ReadInt32();
            return true;
        }

        if (constant.TypeCode == ConstantTypeCode.UInt32)
        {
            value = blob.ReadUInt32();
            return true;
        }

        if (constant.TypeCode == ConstantTypeCode.Int64)
        {
            value = blob.ReadInt64();
            return true;
        }

        if (constant.TypeCode == ConstantTypeCode.UInt64)
        {
            value = unchecked((long)blob.ReadUInt64());
            return true;
        }

        value = 0;
        return false;
    }

    private static bool IsNullableType(string type)
        => type.StartsWith("System.Nullable<", StringComparison.Ordinal) && type.EndsWith('>');

    private static string ToJsonPropertyName(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static bool HasAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string attributeNamespace,
        string attributeName)
        => attributes.Any(attributeHandle =>
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            return IsAttribute(reader, attribute, attributeNamespace, attributeName);
        });

    private static byte? ReadNullableContext(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => ReadByteAttribute(
            reader,
            attributes,
            "System.Runtime.CompilerServices",
            "NullableContextAttribute");

    private static byte? ReadNullableFlag(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => ReadByteAttribute(
            reader,
            attributes,
            "System.Runtime.CompilerServices",
            "NullableAttribute");

    private static byte? ReadMethodReturnNullableFlag(MetadataReader reader, MethodDefinition getter)
    {
        foreach (var parameterHandle in getter.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber == 0)
            {
                return ReadNullableFlag(reader, parameter.GetCustomAttributes());
            }
        }

        return null;
    }

    private static byte? ReadBackingFieldNullableFlag(Dictionary<string, byte> nullableFlags, string propertyName)
        => nullableFlags.TryGetValue(propertyName, out var nullable) ? nullable : null;

    private static Dictionary<string, byte> ReadBackingFieldNullableFlags(MetadataReader reader, TypeDefinition type)
    {
        var result = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            var fieldName = reader.GetString(field.Name);
            const string suffix = ">k__BackingField";
            if (!fieldName.StartsWith('<') || !fieldName.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var nullable = ReadNullableFlag(reader, field.GetCustomAttributes());
            if (nullable is not null)
            {
                result[fieldName[1..^suffix.Length]] = nullable.Value;
            }
        }

        return result;
    }

    private static string? ReadJsonPropertyName(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsAttribute(reader, attribute, "System.Text.Json.Serialization", "JsonPropertyNameAttribute"))
            {
                continue;
            }

            try
            {
                var value = attribute.DecodeValue(AttributeTypeProvider.Instance);
                return value.FixedArguments.Length > 0 ? value.FixedArguments[0].Value as string : null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static JsonIgnorePolicy ReadJsonIgnore(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsAttribute(reader, attribute, "System.Text.Json.Serialization", "JsonIgnoreAttribute"))
            {
                continue;
            }

            try
            {
                var value = attribute.DecodeValue(AttributeTypeProvider.Instance);
                foreach (var namedArgument in value.NamedArguments)
                {
                    if (namedArgument.Name != "Condition")
                    {
                        continue;
                    }

                    return namedArgument.Value switch
                    {
                        0 => JsonIgnorePolicy.Never,
                        1 => JsonIgnorePolicy.Always,
                        2 or 3 => JsonIgnorePolicy.Conditional,
                        _ => JsonIgnorePolicy.Always,
                    };
                }
            }
            catch (BadImageFormatException)
            {
                return JsonIgnorePolicy.Always;
            }

            return JsonIgnorePolicy.Always;
        }

        return JsonIgnorePolicy.None;
    }

    private static bool HasJsonStringEnumConverter(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => ReadJsonConverter(reader, attributes) is JsonConverterKind.StringEnum;

    private static JsonConverterKind ReadJsonConverter(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsAttribute(reader, attribute, "System.Text.Json.Serialization", "JsonConverterAttribute"))
            {
                continue;
            }

            try
            {
                var value = attribute.DecodeValue(AttributeTypeProvider.Instance);
                if (value.FixedArguments.Length == 0)
                {
                    return JsonConverterKind.Unsupported;
                }

                var converterType = value.FixedArguments[0].Value as string;
                return converterType is not null &&
                    converterType.Contains("JsonStringEnumConverter", StringComparison.Ordinal)
                    ? JsonConverterKind.StringEnum
                    : JsonConverterKind.Unsupported;
            }
            catch (BadImageFormatException)
            {
                return JsonConverterKind.Unsupported;
            }
        }

        return JsonConverterKind.None;
    }

    private static Dictionary<string, byte> ReadConstructorNullableFlags(MetadataReader reader, TypeDefinition type)
    {
        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!reader.StringComparer.Equals(method.Name, ".ctor"))
            {
                continue;
            }

            foreach (var parameterHandle in method.GetParameters())
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber == 0)
                {
                    continue;
                }

                var nullable = ReadNullableFlag(reader, parameter.GetCustomAttributes());
                if (nullable is null)
                {
                    continue;
                }

                var name = reader.GetString(parameter.Name);
                if (name.Length > 0)
                {
                    result[name] = nullable.Value;
                }
            }
        }

        return result;
    }

    private static byte? ReadConstructorParameterNullableFlag(
        Dictionary<string, byte> nullableFlags,
        string propertyName)
        => nullableFlags.TryGetValue(propertyName, out var nullable) ? nullable : null;

    private static byte? ReadByteAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        string attributeNamespace,
        string attributeName)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!IsAttribute(reader, attribute, attributeNamespace, attributeName))
            {
                continue;
            }

            try
            {
                var value = attribute.DecodeValue(AttributeTypeProvider.Instance);
                if (value.FixedArguments.Length == 0)
                {
                    return null;
                }

                return ReadNullableByte(value.FixedArguments[0].Value);
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static byte? ReadNullableByte(object? value)
    {
        if (value is byte flag)
        {
            return flag;
        }

        if (value is ImmutableArray<CustomAttributeTypedArgument<string>> { Length: > 0 } flags &&
            flags[0].Value is byte first)
        {
            return first;
        }

        return null;
    }

    private static bool IsAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        string attributeNamespace,
        string attributeName)
    {
        var constructor = attribute.Constructor;
        EntityHandle typeHandle;
        if (constructor.Kind == HandleKind.MemberReference)
        {
            typeHandle = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        }
        else if (constructor.Kind == HandleKind.MethodDefinition)
        {
            typeHandle = reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType();
        }
        else
        {
            return false;
        }

        if (typeHandle.Kind == HandleKind.TypeReference)
        {
            return IsAttributeType(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)typeHandle),
                attributeNamespace,
                attributeName);
        }

        if (typeHandle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle);
            return reader.StringComparer.Equals(type.Namespace, attributeNamespace) &&
                reader.StringComparer.Equals(type.Name, attributeName);
        }

        return false;
    }

    private static bool IsAttributeType(
        MetadataReader reader,
        TypeReference type,
        string attributeNamespace,
        string attributeName)
        => reader.StringComparer.Equals(type.Namespace, attributeNamespace) &&
           reader.StringComparer.Equals(type.Name, attributeName);

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

        internal static string BuildTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
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

    private sealed class AttributeTypeProvider : ICustomAttributeTypeProvider<string>
    {
        internal static readonly AttributeTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => BuildFullTypeName(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => SignatureTypeProvider.BuildTypeReferenceName(reader, handle);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }
}

internal enum JsonIgnorePolicy
{
    None,
    Never,
    Always,
    Conditional,
}

internal enum JsonConverterKind
{
    None,
    StringEnum,
    Unsupported,
}

internal sealed record TypeSchemaProperty(
    string Name,
    string JsonName,
    string Type,
    bool IsRequired,
    bool IsNullable,
    bool IsStringEnum);

internal sealed record EnumSchemaInfo(
    string TypeName,
    IReadOnlyList<string> Names,
    IReadOnlyList<long> Values,
    bool IsStringEnum);
