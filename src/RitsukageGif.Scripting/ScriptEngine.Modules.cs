namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptEngine
    {
        private static StringComparer PathComparer =>
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public async Task<object?> RunAsync(ScriptNode program, string file, CancellationToken token = default)
        {
            file = Path.GetFullPath(file);
            if (!_loading.Add(file))
                throw new InvalidOperationException(UiText.Get("ScriptImportCycle"));
            Logger.ZLogInformation($"Script execution started. DebuggerAttached={_debugger is not null}");
            try
            {
                var module = await RunModuleAsync(program, file, token);
                Logger.ZLogInformation(
                    $"Script execution completed. ExplicitExports={module.HasExports} CachedModules={_modules.Count}");
                return module.HasExports ? module : module.ReturnValue;
            }
            catch (OperationCanceledException)
            {
                Logger.ZLogInformation($"Script execution canceled.");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(
                    $"Script execution failed. ExceptionType={error.GetType().Name} Line={_current?.Line ?? 0} Column={_current?.Column ?? 0} StackTrace={error.StackTrace}");
                throw;
            }
            finally
            {
                _loading.Remove(file);
            }
        }

        private void HoistFunctions(IReadOnlyList<ScriptNode> statements, Scope scope)
        {
            foreach (var statement in statements)
            {
                var declaration = statement.Kind == "exportDeclaration" ? statement.Children[0] : statement;
                if (declaration is { Kind: "functionDeclaration" } or { Kind: "exportDefault", Text.Length: > 0 })
                    scope.Define(declaration.Text, new Function(declaration.Children[0], scope, _files.Peek()), true);
            }
        }

        private static IEnumerable<string> ExportedNames(ScriptNode pattern)
        {
            if (pattern.Kind == "binding-name")
                yield return pattern.Text;
            else
                foreach (var child in pattern.Kind == "binding-default" ? pattern.Children.Take(1) : pattern.Children)
                foreach (var name in ExportedNames(child))
                    yield return name;
        }

        private async Task<ScriptModule> RunModuleAsync(ScriptNode program, string file, CancellationToken token)
        {
            _files.Push(file);
            var module = new ScriptModule();
            try
            {
                _current = program;
                var scope = new Scope(_globals, functionScope: true);
                var statements = program.Kind == "block" ? program.Children : [program];
                HoistFunctions(statements, scope);
                var exports = new List<string>();
                foreach (var statement in statements)
                {
                    _current = statement;
                    switch (statement.Kind)
                    {
                        case "exportDeclaration":
                        {
                            module.HasExports = true;
                            var declaration = statement.Children[0];
                            var names = declaration.Kind == "functionDeclaration"
                                ? [declaration.Text]
                                : ExportedNames(declaration.Binding ?? NamePattern(declaration.Text));
                            foreach (var name in names)
                            {
                                module.Export(name, () => scope.GetLocal(name));
                                exports.Add(name);
                            }
                        }

                            break;
                        case "exportBindings":
                        {
                            module.HasExports = true;
                            foreach (var binding in statement.Children)
                            {
                                var name = (string)binding.Value!;
                                module.Export(binding.Text, () => scope.GetLocal(name));
                                exports.Add(name);
                            }
                        }

                            break;
                        case "exportDefault":
                        {
                            module.HasExports = true;
                            module.Export("default", () => module.ReturnValue);
                        }

                            break;
                    }
                }

                foreach (var statement in statements)
                {
                    Completion completion = default;
                    switch (statement.Kind)
                    {
                        case "exportDeclaration":
                        {
                            completion = await ExecuteAsync(statement.Children[0], scope, token);
                        }

                            break;
                        case "exportDefault":
                        {
                            await CheckAsync(statement, scope, token);
                            var value = statement.Text.Length > 0
                                ? new(scope.GetLocal(statement.Text))
                                : await EvalAsync(statement.Children[0], scope, token);
                            module.ReturnValue = value.Value;
                            completion = value.Completion;
                        }

                            break;
                        case not "exportBindings":
                        {
                            completion = await ExecuteAsync(statement, scope, token);
                        }

                            break;
                    }

                    if (completion.Kind == FlowKind.Normal)
                        continue;
                    if (completion.Kind != FlowKind.Return || module.HasExports)
                        throw new InvalidOperationException(UiText.Format("DslArguments", "export/return"));
                    module.ReturnValue = completion.Value;
                    scope.ValidatePropagation(module.ReturnValue);
                    return module;
                }

                if (module.HasExports && scope.HasPropagation)
                    throw new InvalidOperationException(UiText.Format("DslArguments", "export/return"));
                scope.ValidatePropagation(module.ReturnValue);
                foreach (var name in exports)
                    if (!scope.ContainsLocal(name))
                        throw new InvalidOperationException(UiText.Format("DslUnknownName", name));
                return module;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ScriptExecutionException)
            {
                throw;
            }
            catch (Thrown)when (_files.Count > 1)
            {
                throw;
            }
            catch (Exception error)
            {
                var message = error.Message;
                throw error is Thrown thrown
                    ? new ScriptExecutionException(thrown.Line, thrown.Column, message, error, thrown.File,
                        thrown.Frames)
                    : new ScriptExecutionException(_current?.Line ?? 1, _current?.Column ?? 1, message, error, file,
                        CaptureFrames(_current, file));
            }
            finally
            {
                _files.Pop();
            }
        }

        private async ValueTask<ScriptModule> ImportAsync(string relative, CancellationToken token)
        {
            var file = Path.GetFullPath(relative, Path.GetDirectoryName(_files.Peek())!);
            if (_modules.TryGetValue(file, out var cached))
                return cached;
            if (_loading.Count >= 32 || !_loading.Add(file))
                throw new InvalidOperationException(UiText.Get("ScriptImportCycle"));
            Logger.ZLogDebug($"Script module loading started. LoadingDepth={_loading.Count}");
            var callerNode = _current;
            _callSites.Add(new("import", _files.Peek(), callerNode?.Line ?? 1, callerNode?.Column ?? 1));
            var completed = false;
            try
            {
                if (new FileInfo(file).Length > 262144)
                    throw new InvalidOperationException(UiText.Get("DslTooComplex"));
                var source = await File.ReadAllTextAsync(file, token);
                var program = ScriptParser.Parse(source);
                var value = await RunModuleAsync(program, file, token);
                _modules[file] = value;
                completed = true;
                Logger.ZLogDebug(
                    $"Script module loaded. CachedModules={_modules.Count} ExplicitExports={value.HasExports}");
                return value;
            }
            catch (PresetSyntaxException error)
            {
                Logger.ZLogWarning($"Script module parsing rejected. Line={error.Line} Column={error.Column}");
                throw new ScriptExecutionException(error.Line, error.Column, error.RawMessage, error, file,
                    CaptureFrames(error.Line, error.Column, file));
            }
            finally
            {
                _loading.Remove(file);
                _callSites.RemoveAt(_callSites.Count - 1);
                if (completed)
                    _current = callerNode;
            }
        }

        private async ValueTask ImportStatementAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            var module = await ImportAsync((string)node.Value!, token);
            switch (node.Kind)
            {
                case "import":
                    scope.Define(node.Text, module.HasExports ? module : module.ReturnValue, true);
                    break;
                case "importNamed":
                    foreach (var binding in node.Children)
                    {
                        var name = (string)binding.Value!;
                        if (name == "*")
                        {
                            scope.Define(binding.Text, module, true);
                        }
                        else
                        {
                            _ = module.Get(name);
                            scope.DefineImport(binding.Text, () => module.Get(name));
                        }
                    }

                    break;
            }
        }
    }
}
