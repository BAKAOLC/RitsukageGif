namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        private static void RegisterResults(ScriptBindingRegistry bindings)
        {
            var results = bindings.RegisterType<ScriptResult>("Result");
            bindings.RegisterPattern(new ScriptPatternDescriptor("Ok", "Result", ["$0"], MatchOk));
            bindings.RegisterPattern(new ScriptPatternDescriptor("Err", "Result", ["$1"], MatchErr));
            bindings.RegisterPattern(new ScriptPatternDescriptor("Some", "Option", ["$0"], MatchSome));
            bindings.RegisterPattern(new ScriptPatternDescriptor("None", "Option", [], MatchNone));
            bindings.RegisterPatternCases("Result", ["Ok", "Err"]);
            bindings.RegisterPatternCases("Option", ["Some", "None"]);
            bindings.RegisterGenericType("Result", new ScriptGenericTypeBinding(2, (value, arguments, validate) =>
                {
                    if (value is not ScriptResult outcome) throw TypeError();
                    var normalized = validate(outcome.Value, arguments[outcome.IsOk ? 0 : 1]);
                    return Equals(normalized, outcome.Value) ? outcome : new ScriptResult(outcome.IsOk, normalized);
                }, (members, arguments) => SpecializeOutcomeMembers(members, arguments), OutcomeValueParameter: 0,
                Matches: (value, arguments, matches) => value is ScriptResult outcome &&
                                                        matches(outcome.Value, arguments[outcome.IsOk ? 0 : 1])));
            var result = bindings.RegisterTable("result");
            result.Function("ok", ScriptEngine.Native(args => new ScriptResult(true, args[0])), [new("value")],
                "Result<$0,never>");
            result.Function("err", ScriptEngine.Native(args => new ScriptResult(false, args[0])), [new("error")],
                "Result<never,$0>");
            result.Function("attempt", async (args, token) =>
                {
                    try
                    {
                        var value = await bindings.InvokeCallableAsync(args[0], [], token);
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(true, value);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false,
                            exception is ScriptExecutionException execution ? execution.RawMessage : exception.Message);
                    }
                }, [new("callback", "function", CallbackParameters: [])], "Result<any,string>",
                inferredReturnType: "Result<$callback0,string>");
            results.Property("isOk", "boolean", value => value.IsOk);
            results.Property("isErr", "boolean", value => !value.IsOk);
            results.Property("value", "any", value => value.IsOk ? value.Value : throw TypeError(),
                tryRead: (outcome, out value) =>
                {
                    value = outcome.Value;
                    return outcome.IsOk;
                });
            results.Property("error", "any", value => !value.IsOk ? value.Value : throw TypeError(),
                tryRead: (outcome, out value) =>
                {
                    value = outcome.Value;
                    return !outcome.IsOk;
                });
            results.Method("unwrapOr", (value, args, _) => Result(value.IsOk ? value.Value : args[0]),
                [new("fallback")], inferredReturnType: "$receiver0|$0");
            results.Method("unwrapOrElse",
                (value, args, token) =>
                    value.IsOk ? Result(value.Value) : bindings.InvokeCallableAsync(args[0], [value.Value], token),
                [new("fallback", "function", CallbackParameters: ["$receiver1"])],
                inferredReturnType: "$receiver0|$callback0");
            results.Method("map",
                async (value, args, token) => value.IsOk
                    ? new ScriptResult(true, await bindings.InvokeCallableAsync(args[0], [value.Value], token))
                    : value, [new("transform", "function", CallbackParameters: ["$receiver0"])], "Result",
                inferredReturnType: "Result<$callback0,$receiver1>");
            results.Method("mapErr",
                async (value, args, token) => value.IsOk
                    ? value
                    : new ScriptResult(false, await bindings.InvokeCallableAsync(args[0], [value.Value], token)),
                [new("transform", "function", CallbackParameters: ["$receiver1"])], "Result",
                inferredReturnType: "Result<$receiver0,$callback0>");
            results.Method("andThen",
                async (value, args, token) =>
                    value.IsOk ? await bindings.InvokeCallableAsync(args[0], [value.Value], token) : value,
                [new("transform", "function", CallbackParameters: ["$receiver0"], CallbackReturnType: "Result")],
                "Result", inferredReturnType: "$callback0|Result<never,$receiver1>");
            results.Method("orElse",
                async (value, args, token) =>
                    value.IsOk ? value : await bindings.InvokeCallableAsync(args[0], [value.Value], token),
                [new("recover", "function", CallbackParameters: ["$receiver1"], CallbackReturnType: "Result")],
                "Result", inferredReturnType: "Result<$receiver0,never>|$callback0");
            var options = bindings.RegisterType<ScriptOption>("Option");
            bindings.RegisterGenericType("Option", new ScriptGenericTypeBinding(1, (value, arguments, validate) =>
                {
                    if (value is not ScriptOption outcome) throw TypeError();
                    if (!outcome.HasValue) return outcome;
                    var normalized = validate(outcome.Value, arguments[0]);
                    return Equals(normalized, outcome.Value) ? outcome : new ScriptOption(true, normalized);
                }, (members, arguments) => SpecializeOutcomeMembers(members, arguments), OutcomeValueParameter: 0,
                Matches: (value, arguments, matches) => value is ScriptOption outcome &&
                                                        (!outcome.HasValue || matches(outcome.Value, arguments[0]))));
            var option = bindings.RegisterTable("option");
            option.Function("some", ScriptEngine.Native(args => new ScriptOption(true, args[0])), [new("value")],
                "Option<$0>");
            option.Function("fromNullable",
                ScriptEngine.Native(args => args[0] is null ? ScriptOption.None : new ScriptOption(true, args[0])),
                [new("value")], "Option<$0>");
            option.Value("none", ScriptOption.None, "Option<never>");
            options.Property("isSome", "boolean", value => value.HasValue);
            options.Property("isNone", "boolean", value => !value.HasValue);
            options.Property("value", "any", value => value.HasValue ? value.Value : throw TypeError(),
                tryRead: (outcome, out value) =>
                {
                    value = outcome.Value;
                    return outcome.HasValue;
                });
            options.Method("unwrapOr", (value, args, _) => Result(value.HasValue ? value.Value : args[0]),
                [new("fallback")], inferredReturnType: "$receiver0|$0");
            options.Method("unwrapOrElse",
                (value, args, token) =>
                    value.HasValue ? Result(value.Value) : bindings.InvokeCallableAsync(args[0], [], token),
                [new("fallback", "function", CallbackParameters: [])],
                inferredReturnType: "$receiver0|$callback0");
            options.Method("map",
                async (value, args, token) => value.HasValue
                    ? new ScriptOption(true, await bindings.InvokeCallableAsync(args[0], [value.Value], token))
                    : value, [new("transform", "function", CallbackParameters: ["$receiver0"])], "Option",
                inferredReturnType: "Option<$callback0>");
            options.Method("andThen",
                async (value, args, token) =>
                    value.HasValue ? await bindings.InvokeCallableAsync(args[0], [value.Value], token) : value,
                [new("transform", "function", CallbackParameters: ["$receiver0"], CallbackReturnType: "Option")],
                "Option", inferredReturnType: "$callback0|Option<never>");
            options.Method("orElse",
                (value, args, token) =>
                    value.HasValue ? Result(value) : bindings.InvokeCallableAsync(args[0], [], token),
                [new("recover", "function", CallbackParameters: [], CallbackReturnType: "Option")], "Option",
                inferredReturnType: "$receiver|$callback0");
            options.Method("okOr",
                (value, args, _) => Result(new ScriptResult(value.HasValue, value.HasValue ? value.Value : args[0])),
                [new("error")], "Result", inferredReturnType: "Result<$receiver0,$0>");
        }

        private static bool MatchOk(object? value, out IReadOnlyList<object?> payload)
        {
            if (value is ScriptResult { IsOk: true } outcome)
            {
                payload = new[] { outcome.Value };
                return true;
            }

            payload = [];
            return false;
        }

        private static bool MatchErr(object? value, out IReadOnlyList<object?> payload)
        {
            if (value is ScriptResult { IsOk: false } outcome)
            {
                payload = new[] { outcome.Value };
                return true;
            }

            payload = [];
            return false;
        }

        private static bool MatchSome(object? value, out IReadOnlyList<object?> payload)
        {
            if (value is ScriptOption { HasValue: true } outcome)
            {
                payload = new[] { outcome.Value };
                return true;
            }

            payload = [];
            return false;
        }

        private static bool MatchNone(object? value, out IReadOnlyList<object?> payload)
        {
            payload = [];
            return value is ScriptOption { HasValue: false };
        }

        private static IReadOnlyList<ScriptMemberDescriptor> SpecializeOutcomeMembers(
            IReadOnlyList<ScriptMemberDescriptor> members, IReadOnlyList<string> arguments) =>
            members.Select(member => member.Name switch
            {
                "value" => member with { TypeName = arguments[0] },
                "error" when arguments.Count == 2 => member with { TypeName = arguments[1] },
                "unwrapOr" or "unwrapOrElse" when member.Callable is not null => member with
                {
                    Callable = member.Callable with { ReturnTypeName = arguments[0] }
                },
                _ => member
            }).ToArray();
    }
}
