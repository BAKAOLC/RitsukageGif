using System.Collections.ObjectModel;
using System.Globalization;

namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptBindingRegistry
    {
        private bool IsMetadataOnly { get; set; }

        public ScriptBindingMetadata ExportMetadataDelta(ScriptBindingRegistry baseline)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            if (_genericTypes.Keys.Except(baseline._genericTypes.Keys).Any() ||
                _patterns.Keys.Except(baseline._patterns.Keys).Any() ||
                _patternCases.Keys.Except(baseline._patternCases.Keys).Any())
                throw new NotSupportedException(
                    "Additional generic or pattern registrations require metadata support.");
            ScriptMetadataGlobal[] globals =
            [
                .. _globals.Where(pair => !baseline._globals.ContainsKey(pair.Key))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => ExportGlobal(pair.Value)),
            ];
            ScriptMetadataTable[] tables =
            [
                .. _tables.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                {
                    var prior = baseline._tables.GetValueOrDefault(pair.Key);
                    ScriptMetadataGlobal[] members =
                    [
                        .. pair.Value.Members.Where(member => prior?.Contains(member.Name) != true)
                            .OrderBy(member => member.Name, StringComparer.Ordinal)
                            .Select(member => new ScriptMetadataGlobal(ExportMember(member),
                                member.Callable is null ? ExportValue(pair.Value.Get(member.Name)) : null)),
                    ];
                    return new ScriptMetadataTable(pair.Key, members);
                }).Where(table => table.Members.Length > 0 || !baseline._tables.ContainsKey(table.Name)),
            ];
            ScriptMetadataHost[] hosts =
            [
                .. _types.Where(pair => !baseline._types.ContainsKey(pair.Key))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new ScriptMetadataHost(pair.Key,
                    [
                        .. pair.Value.Members.OrderBy(member => member.Name, StringComparer.Ordinal)
                            .Select(ExportMember),
                    ])),
            ];
            ScriptMetadataSchema[] schemas =
            [
                .. _schemas.Where(pair => !baseline._schemas.ContainsKey(pair.Key))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new ScriptMetadataSchema(pair.Key, pair.Value.AllowExtra,
                    [
                        .. pair.Value.Properties.Select(property => new ScriptMetadataProperty(property.Name,
                            property.TypeName, property.Optional, ExportValue(property.DefaultValue))),
                    ])),
            ];
            ScriptMetadataEnum[] enums =
            [
                .. _enums.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                {
                    var prior = baseline._enums.GetValueOrDefault(pair.Key);
                    return new ScriptMetadataEnum(pair.Key, pair.Value.Values
                        .Where(value => prior?.Values.ContainsKey(value.Key) != true)
                        .OrderBy(value => value.Key, StringComparer.Ordinal)
                        .ToDictionary(value => value.Key, value => ExportValue(value.Value), StringComparer.Ordinal));
                }).Where(value => value.Values.Count > 0 || !baseline._enums.ContainsKey(value.Name)),
            ];
            return new(1, globals, tables, hosts, schemas, enums,
                _declarations.Where(pair => !baseline._declarations.ContainsKey(pair.Key))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        public void ImportMetadata(ScriptBindingMetadata catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            if (catalog.Version != 1) throw new InvalidDataException("Unsupported script metadata catalog version.");
            IsMetadataOnly = true;
            foreach (var schema in catalog.Schemas)
                _schemas.Add(schema.Name, new([
                        .. schema.Properties.Select(property => new ScriptPropertyDescriptor(
                            property.Name, property.TypeName, property.Optional, ImportValue(property.DefaultValue))),
                    ],
                    schema.AllowExtra));
            foreach (var host in catalog.Hosts)
            {
                var binding = new ScriptMetadataHostBinding([.. host.Members.Select(ImportMember)]);
                _types.Add(host.Name, binding);
                _hostBindings.Add(binding);
            }

            foreach (var table in catalog.Tables)
            {
                if (!_tables.TryGetValue(table.Name, out var binding))
                {
                    binding = new(this, table.Name);
                    _tables.Add(table.Name, binding);
                }

                foreach (var member in table.Members)
                    binding.ImportMetadataMember(ImportMember(member.Member),
                        ImportValue(member.Value));
            }

            foreach (var enumeration in catalog.Enums)
            {
                if (!_enums.TryGetValue(enumeration.Name, out var binding))
                {
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    binding = new(typeof(ScriptMetadataValue), values,
                        new(enumeration.Name, new ReadOnlyDictionary<string, object?>(values)));
                    _enums.Add(enumeration.Name, binding);
                }

                foreach (var value in enumeration.Values) binding.Values.Add(value.Key, ImportValue(value.Value));
            }

            foreach (var global in catalog.Globals)
            {
                var descriptor = ImportMember(global.Member);
                var value = descriptor.Callable is { } callable
                    ? new ScriptBoundFunction(callable, MetadataUnavailable)
                    : _tables.GetValueOrDefault(descriptor.Name) ?? ImportValue(global.Value);
                _globals.Add(descriptor.Name, new(value, descriptor));
            }

            foreach (var declaration in catalog.Declarations) _declarations.Add(declaration.Key, declaration.Value);
        }

        private static ScriptMetadataGlobal ExportGlobal(Global global)
        {
            return new(ExportMember(global.Descriptor), global.Descriptor.Callable is not null ||
                                                        global.Value is ScriptTableBinding
                ? null
                : ExportValue(global.Value));
        }

        private static ScriptMetadataMember ExportMember(ScriptMemberDescriptor member)
        {
            var callable = member.Callable;
            return new(member.Name, member.Kind, member.TypeName, callable is null
                    ? null
                    : new(callable.Name,
                        [
                            .. callable.Parameters.Select(parameter => new ScriptMetadataParameter(parameter.Name,
                                parameter.TypeName,
                                parameter.Optional, ExportValue(parameter.DefaultValue), parameter.Variadic,
                                parameter.ReceiverElement,
                                parameter.CallbackParameters is { } callbackParameters ? [.. callbackParameters] : null,
                                parameter.CallbackReturnType,
                                parameter.MaximumCount)),
                        ], callable.ReturnTypeName, callable.Description,
                        callable.InferredReturnTypeName, callable.InvalidatesReceiverElement), member.Description,
                member.Writable);
        }

        private static ScriptMemberDescriptor ImportMember(ScriptMetadataMember member)
        {
            var callable = member.Callable;
            return new(member.Name, member.Kind, member.TypeName, callable is null
                    ? null
                    : new(callable.Name,
                        [
                            .. callable.Parameters.Select(parameter => new ScriptParameter(parameter.Name,
                                parameter.TypeName,
                                parameter.Optional, ImportValue(parameter.DefaultValue), parameter.Variadic,
                                parameter.ReceiverElement, parameter.CallbackParameters, parameter.CallbackReturnType,
                                parameter.MaximumCount)),
                        ], callable.ReturnTypeName, callable.Description,
                        callable.InferredReturnTypeName, callable.InvalidatesReceiverElement), member.Description,
                member.Writable);
        }

        private static ScriptMetadataValue? ExportValue(object? value)
        {
            return value switch
            {
                null => null,
                ScriptMetadataValue metadata => metadata,
                string text => new("string", text),
                bool flag => new("boolean", Boolean: flag),
                Enum enumeration => new("enum", enumeration.ToString()),
                byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                    new("number", Number: Convert.ToDouble(value, CultureInfo.InvariantCulture)),
                _ => throw new NotSupportedException("A script metadata default or constant is not scalar."),
            };
        }

        private static object? ImportValue(ScriptMetadataValue? value)
        {
            return value?.Kind switch
            {
                null => null,
                "string" => value.Text,
                "boolean" => value.Boolean ?? throw new InvalidDataException(),
                "number" => value.Number ?? throw new InvalidDataException(),
                "enum" => value,
                _ => throw new InvalidDataException("Unsupported script metadata value kind."),
            };
        }

        internal static ValueTask<object?> MetadataUnavailable(IReadOnlyList<object?> arguments,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Script metadata bindings cannot execute operations.");
        }
    }
}
