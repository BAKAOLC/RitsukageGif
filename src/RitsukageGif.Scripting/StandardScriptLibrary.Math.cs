using System.Globalization;

namespace RitsukageGif.Scripting
{
    public static partial class StandardScriptLibrary
    {
        private static void RegisterMath(ScriptBindingRegistry bindings)
        {
            var math = bindings.RegisterTable("math");
            math.Value("pi", Math.PI, "number").Value("e", Math.E, "number").Value("tau", Math.Tau, "number");
            AddUnary(math, "abs", Math.Abs);
            AddUnary(math, "floor", Math.Floor);
            AddUnary(math, "ceil", Math.Ceiling);
            AddUnary(math, "round", Math.Round);
            AddUnary(math, "trunc", Math.Truncate);
            AddUnary(math, "sign", value => Math.Sign(value));
            AddUnary(math, "sin", Math.Sin);
            AddUnary(math, "cos", Math.Cos);
            AddUnary(math, "tan", Math.Tan);
            AddUnary(math, "asin", Math.Asin);
            AddUnary(math, "acos", Math.Acos);
            AddUnary(math, "atan", Math.Atan);
            AddUnary(math, "sqrt", Math.Sqrt);
            AddUnary(math, "exp", Math.Exp);
            AddUnary(math, "log", Math.Log);
            AddUnary(math, "log10", Math.Log10);
            AddBinary(math, "pow", Math.Pow);
            AddBinary(math, "atan2", Math.Atan2);
            math.Function("min", ScriptEngine.Native(args => args.Select(ScriptEngine.Number).Min()),
                [new("value", "number"), new("others", "number", Variadic: true)], "number");
            math.Function("max", ScriptEngine.Native(args => args.Select(ScriptEngine.Number).Max()),
                [new("value", "number"), new("others", "number", Variadic: true)], "number");
            math.Function("random", ScriptEngine.Native(_ => Random.Shared.NextDouble()), returnType: "number");
            math.Function("clamp", ScriptEngine.Native(args =>
            {
                var min = ScriptEngine.Number(args[1]);
                var max = ScriptEngine.Number(args[2]);
                return min <= max ? Math.Clamp(ScriptEngine.Number(args[0]), min, max) : throw TypeError();
            }), [new("value", "number"), new("minimum", "number"), new("maximum", "number")], "number");
        }

        private static double ParseNumber(object? value)
        {
            return double.TryParse(Text(value), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                ? number
                : throw TypeError();
        }

        private static int Offset(object? value, int length, bool relative = true)
        {
            var number = Math.Truncate(ScriptEngine.Number(value));
            if (relative && number < 0) number += length;
            return (int)Math.Clamp(number, 0, length);
        }
    }
}
