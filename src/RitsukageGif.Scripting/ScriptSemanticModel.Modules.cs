namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        private sealed class ModuleContext
        {
            public Dictionary<string, ScriptSemanticModel> Models { get; } = new(PathComparer);
            public int Reads;
        }

        private readonly ModuleContext _moduleContext;
        private readonly string? _file;
        private readonly ScriptSourceResolver _sources;
        private readonly IReadOnlyList<string> _ancestry;

        private readonly Dictionary<string, string> _dependencies =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private readonly Dictionary<string, Value> _exports = new(StringComparer.Ordinal);
        private Value? _legacyExport;

        private static StringComparer PathComparer =>
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public bool IsCurrent => _dependencies.All(pair => _sources.Stamp(pair.Key) == pair.Value);

        public ScriptSemanticModel Refresh() =>
            IsCurrent ? this : new ScriptSemanticModel(_source, _bindings, _file, _sources);

        private void Import(ScriptNode node, Scope scope)
        {
            if (_file is null)
            {
                if (node.Kind == "import") AddImport(node, node.Text, new Value(), scope);
                else
                    foreach (var binding in node.Children)
                        AddImport(binding, binding.Text, new Value(), scope);
                return;
            }

            try
            {
                var path = Path.GetFullPath((string)node.Value!, Path.GetDirectoryName(_file)!);
                _dependencies[path] = _sources.Stamp(path);
                if (_ancestry.Count >= 32 || PathComparer.Equals(path, _file) || _ancestry.Contains(path, PathComparer))
                    throw new InvalidOperationException(UiText.Get("ScriptImportCycle"));
                if (!_moduleContext.Models.TryGetValue(path, out var module))
                {
                    if (++_moduleContext.Reads > 128) throw new InvalidOperationException(UiText.Get("DslTooComplex"));
                    var text = _sources.Read(path);
                    module = new ScriptSemanticModel(text, _bindings, path, _sources, [.. _ancestry, _file],
                        _moduleContext);
                    _moduleContext.Models[path] = module;
                    foreach (var error in PresetLanguage.Diagnose(text, _bindings, path))
                        module.Diagnostic(new ScriptNode("error", "", null, [], error.Line, error.Column),
                            error.Message);
                }

                foreach (var dependency in module._dependencies) _dependencies[dependency.Key] = dependency.Value;
                foreach (var error in module.Diagnostics)
                {
                    var diagnostic = error with
                    {
                        Source = error.Source ?? path,
                        Related = [.. error.Related, new ScriptDiagnostic(node.Line, node.Column, "import", _file)]
                    };
                    var existingIndex = _diagnostics.FindIndex(existing =>
                        existing.Source == diagnostic.Source && existing.Line == diagnostic.Line &&
                        existing.Column == diagnostic.Column && existing.Message == diagnostic.Message);
                    if (existingIndex < 0) _diagnostics.Add(diagnostic);
                    else
                        _diagnostics[existingIndex] = _diagnostics[existingIndex] with
                        {
                            Related = _diagnostics[existingIndex].Related.Concat(diagnostic.Related)
                                .DistinctBy(item => (item.Source, item.Line, item.Column, item.Message)).ToArray()
                        };
                }

                var members = new Value("object");
                foreach (var pair in module._exports)
                    members.Members[pair.Key] = module.Snapshot(pair.Value, pair.Key, 0);
                if (module._exports.Count == 0 && module._legacyExport is { } returned)
                {
                    var legacy = module.Snapshot(returned, "default", 0);
                    foreach (var pair in legacy.Members) members.Members[pair.Key] = pair.Value;
                    members.Members["default"] = legacy;
                }

                if (node.Kind == "import")
                {
                    var value = module._legacyExport is { } legacy ? module.Snapshot(legacy, node.Text, 0) : members;
                    AddImport(node, node.Text, value, scope);
                }
                else if (node.Kind == "importNamed")
                    foreach (var binding in node.Children)
                    {
                        var name = (string)binding.Value!;
                        Value value;
                        if (name == "*") value = members;
                        else if (!members.Members.TryGetValue(name, out value!))
                        {
                            Diagnostic(binding, UiText.Format("DslUnknownName", name));
                            value = new Value();
                        }

                        AddImport(binding, binding.Text, value, scope);
                    }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                              or InvalidOperationException or FormatException)
            {
                Diagnostic(node, error.Message);
            }
        }

        private void AddImport(ScriptNode node, string name, Value value, Scope scope)
        {
            var declaration = Declaration(node, name, "import");
            scope.Bindings[name] = new Binding(name, Callable(value) is null ? "variable" : "function", Offset(node),
                value.Declaration ?? declaration, value);
            _symbols.Add(declaration);
        }

        private Value Snapshot(Value value, string name, int depth)
        {
            if (depth > 16) return Typed(value.TypeName);
            if (value.Function is not null) FinishFunction(value);
            var result = value.Function is { } function && value.Closure is { } scope
                ? InferFunctionResult(function, scope,
                    ((string[])function.Value!)
                    .Select(parameter => scope.Find(parameter, int.MaxValue)?.Value ?? new Value()).ToArray(), 0)
                : value.Result;
            var copy = new Value(value.TypeName)
            {
                Callable = Callable(value, name), Description = value.Description, Declaration = value.Declaration,
                Element = value.Element is null ? null : Snapshot(value.Element, name, depth + 1),
                Result = result is null ? null : Snapshot(result, name, depth + 1)
            };
            foreach (var pair in value.Members) copy.Members[pair.Key] = Snapshot(pair.Value, pair.Key, depth + 1);
            return copy;
        }

        private void CollectExports(ScriptNode program)
        {
            foreach (var node in program.Children)
            {
                if (node.Kind == "return") _legacyExport = Infer(node.Children[0], _root);
                else if (node.Kind == "exportDefault")
                    Export("default", Infer(node.Children[0], _root), node);
                else if (node.Kind == "exportBindings")
                    foreach (var binding in node.Children)
                        ExportBinding(binding.Text, (string)binding.Value!, binding);
                else if (node.Kind == "exportDeclaration")
                {
                    var declaration = node.Children[0];
                    if (declaration.Kind == "functionDeclaration")
                        ExportBinding(declaration.Text, declaration.Text, declaration);
                    else if (declaration.Binding is { } pattern)
                        foreach (var name in ExportNames(pattern))
                            ExportBinding(name, name, declaration);
                    else ExportBinding(declaration.Text, declaration.Text, declaration);
                }
            }
        }

        private void ExportBinding(string exported, string local, ScriptNode node)
        {
            if (!_root.Bindings.TryGetValue(local, out var binding) || binding.Offset < 0)
            {
                Diagnostic(node, UiText.Format("DslUnknownName", local));
                return;
            }

            binding.Value.Declaration ??= binding.Declaration;
            Export(exported, binding.Value, node);
        }

        private void Export(string name, Value value, ScriptNode node)
        {
            value.Declaration ??= Declaration(node, node.Text.Length > 0 ? node.Text : name,
                Callable(value) is null ? "const" : "fn");
            if (!_exports.TryAdd(name, value)) Diagnostic(node, UiText.Format("DslDuplicate", name));
        }

        private static IEnumerable<string> ExportNames(ScriptNode node)
        {
            if (node.Kind == "binding-name") yield return node.Text;
            else
                foreach (var child in node.Kind == "binding-default" ? node.Children.Take(1) : node.Children)
                foreach (var name in ExportNames(child))
                    yield return name;
        }
    }
}
