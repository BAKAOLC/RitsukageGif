namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptEngine
    {
        private static readonly object Missing = new();

        private static ScriptNode NamePattern(string name) => new("binding-name", name, null, [], 1, 1);

        private int _bindingDepth;

        private async ValueTask<Evaluation> BindAsync(ScriptNode pattern, object? value, Scope scope, bool declare,
            bool readOnly,
            CancellationToken token, string? typeName = null)
        {
            if (++_bindingDepth > 128)
            {
                _bindingDepth--;
                throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
            }

            try
            {
                return await BindCoreAsync(pattern, value, scope, declare, readOnly, token, typeName);
            }
            finally
            {
                _bindingDepth--;
            }
        }

        private async ValueTask<Evaluation> BindCoreAsync(ScriptNode pattern, object? value, Scope scope, bool declare,
            bool readOnly,
            CancellationToken token, string? typeName)
        {
            token.ThrowIfCancellationRequested();
            if (pattern.Kind == "binding-hole") return default;
            if (pattern.Kind == "binding-default")
            {
                if (ReferenceEquals(value, Missing))
                {
                    var fallback = await EvalAsync(pattern.Children[1], scope, token);
                    if (fallback.IsReturn) return fallback;
                    value = fallback.Value;
                }

                return await BindAsync(pattern.Children[0], value, scope, declare, readOnly, token,
                    typeName ?? pattern.TypeName);
            }

            if (ReferenceEquals(value, Missing)) throw new InvalidOperationException(UiText.Get("ScriptTypeError"));
            typeName ??= pattern.TypeName;
            if (pattern.Kind == "binding-rest")
                return await BindAsync(pattern.Children[0], value, scope, declare, readOnly, token, typeName);
            if (typeName is not null) value = _bindings.Validate(value, typeName);
            switch (pattern.Kind)
            {
                case "binding-name":
                    if (declare) scope.DefineValidated(pattern.Text, value, readOnly, typeName);
                    else scope.Set(pattern.Text, value);
                    break;
                case "binding-array":
                    var items = Sequence(value);
                    for (var i = 0; i < pattern.Children.Count; i++)
                    {
                        var child = pattern.Children[i];
                        var binding = await BindAsync(child,
                            child.Kind == "binding-rest" ? items.Skip(i).ToList() :
                            i < items.Count ? items[i] : Missing,
                            scope, declare, readOnly, token);
                        if (binding.IsReturn) return binding;
                    }

                    break;
                case "binding-object":
                    if (value is ScriptModule module) value = module.Snapshot();
                    if (value is not Dictionary<string, object?> map) throw TypeError();
                    var consumed = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var child in pattern.Children)
                    {
                        Evaluation binding;
                        if (child.Kind == "binding-rest")
                            binding = await BindAsync(child,
                                map.Where(p => !consumed.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value), scope,
                                declare, readOnly, token);
                        else
                        {
                            consumed.Add(child.Text);
                            binding = await BindAsync(child.Children[0],
                                map.TryGetValue(child.Text, out var field) ? field : Missing, scope, declare, readOnly,
                                token);
                        }

                        if (binding.IsReturn) return binding;
                    }

                    break;
                case "binding-target" when !declare:
                    var target = pattern.Children[0];
                    var container = await EvalAsync(target.Children[0], scope, token);
                    if (container.IsReturn) return container;
                    var key = target.Kind == "member"
                        ? new Evaluation(target.Text)
                        : await EvalAsync(target.Children[1], scope, token);
                    if (key.IsReturn) return key;
                    Set(container.Value, key.Value, value);
                    break;
                default: throw TypeError();
            }

            return default;
        }

        private static List<object?> Sequence(object? value) => value switch
        {
            List<object?> list => list,
            string text => text.Select(c => (object?)c.ToString()).ToList(),
            _ => throw TypeError()
        };

        private async ValueTask<Evaluation> AppendAsync(List<object?> values, ScriptNode node, Scope scope,
            CancellationToken token)
        {
            var item = await EvalAsync(node.Kind == "spread" ? node.Children[0] : node, scope, token);
            if (item.IsReturn) return item;
            if (node.Kind == "spread") values.AddRange(Sequence(item.Value));
            else values.Add(item.Value);
            return default;
        }

        private async ValueTask<Evaluation> SliceAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            var source = await EvalAsync(node.Children[0], scope, token, true);
            if (source.IsReturn) return source;
            if (ReferenceEquals(source.Value, NullChain) || source.Value is null && node.Kind == "optionalSlice")
                return new Evaluation(NullChain);
            var text = source.Value as string;
            var values = source.Value as List<object?>;
            if (text is null && values is null) throw TypeError();
            var startValue = await EvalAsync(node.Children[1], scope, token);
            if (startValue.IsReturn) return startValue;
            var endValue = await EvalAsync(node.Children[2], scope, token);
            if (endValue.IsReturn) return endValue;
            var stepValue = await EvalAsync(node.Children[3], scope, token);
            if (stepValue.IsReturn) return stepValue;
            var step = stepValue.Value is null ? 1 : SliceIndex(stepValue.Value);
            if (step == 0) throw TypeError();
            var count = text?.Length ?? values!.Count;
            var start = startValue.Value is null
                ? step > 0 ? 0 : count - 1
                : Normalize(SliceIndex(startValue.Value));
            var end = endValue.Value is null ? step > 0 ? count : -1 : Normalize(SliceIndex(endValue.Value));
            var distance = step > 0 ? (long)end - start : (long)start - end;
            var stride = Math.Abs((long)step);
            var length = distance <= 0 ? 0 : (int)((distance + stride - 1) / stride);
            token.ThrowIfCancellationRequested();
            if (text is not null)
            {
                if (step == 1) return new Evaluation(text.Substring(start, length));
                return new Evaluation(string.Create(length, (text, start, step, token), static (span, state) =>
                {
                    long index = state.start;
                    for (var i = 0; i < span.Length; i++, index += state.step)
                    {
                        state.token.ThrowIfCancellationRequested();
                        span[i] = state.text[(int)index];
                    }
                }));
            }

            if (step == 1) return new Evaluation(values!.GetRange(start, length));
            var result = new List<object?>(length);
            for (long i = start; step > 0 ? i < end : i > end; i += step)
            {
                token.ThrowIfCancellationRequested();
                result.Add(values![(int)i]);
            }

            return new Evaluation(result);

            int Normalize(int index)
            {
                var adjusted = index < 0 ? (long)index + count : index;
                return (int)Math.Clamp(adjusted, step > 0 ? 0 : -1, step > 0 ? count : count - 1);
            }
        }

        private static int SliceIndex(object value)
        {
            var number = Number(value);
            return number.Equals(Math.Truncate(number)) && number >= int.MinValue && number <= int.MaxValue
                ? (int)number
                : throw TypeError();
        }
    }
}
