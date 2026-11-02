using System.Text;
using System.Text.Json;

namespace RitsukageGif.Core
{
    internal sealed class OnnxFormulaRecognizer : IDisposable
    {
        private readonly OnnxInferenceSession _decoder;
        private readonly OnnxInferenceSession _encoder;
        private readonly OnnxInferenceSession _resizer;
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly string[] _vocabulary;

        internal OnnxFormulaRecognizer(OnnxRuntimeLibrary runtime, string directory)
        {
            _runtime = runtime;
            using var tokenizer = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "tokenizer.json")));
            var words = tokenizer.RootElement.GetProperty("model").GetProperty("vocab");
            _vocabulary = new string[words.EnumerateObject().Max(item => item.Value.GetInt32()) + 1];
            foreach (var item in words.EnumerateObject()) _vocabulary[item.Value.GetInt32()] = item.Name;
            OnnxInferenceSession? resizer = null, encoder = null, decoder = null;
            try
            {
                resizer = new(runtime, Path.Combine(directory, "image_resizer.onnx"));
                encoder = new(runtime, Path.Combine(directory, "encoder.onnx"));
                decoder = new(runtime, Path.Combine(directory, "decoder.onnx"));
                _resizer = resizer;
                _encoder = encoder;
                _decoder = decoder;
            }
            catch
            {
                decoder?.Dispose();
                encoder?.Dispose();
                resizer?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _decoder.Dispose();
            _encoder.Dispose();
            _resizer.Dispose();
        }

        internal string Recognize(GrayImage source, CancellationToken token)
        {
            using var run = new OnnxRun(_runtime, token);
            var original = source.CropAndPad().Fit();
            var width = original.Width;
            var height = original.Height;
            var ratio = 1d;
            var image = original;
            float[] pixels = [];
            for (var iteration = 0; iteration < 10; iteration++)
            {
                token.ThrowIfCancellationRequested();
                height = Math.Max(1, (int)(height * ratio));
                image = original.Resize(width, height, ratio > 1).Fit().CropAndPad();
                pixels = image.Normalize();
                using var input = new OnnxTensor(_runtime, pixels, [1, 1, image.Height, image.Width]);
                var outputs = _resizer.Run(run, input);
                try
                {
                    var predicted = MaximumIndex(outputs[0].Floats);
                    width = (predicted + 1) * 32;
                    if (width == image.Width) break;
                    ratio = width / (double)image.Width;
                }
                finally
                {
                    foreach (var output in outputs) output.Dispose();
                }
            }

            using var encoderInput = new OnnxTensor(_runtime, pixels, [1, 1, image.Height, image.Width]);
            var context = _encoder.Run(run, encoderInput);
            try
            {
                var ids = new long[513];
                var mask = new byte[513];
                Array.Fill(mask, (byte)1);
                ids[0] = 1;
                var result = new StringBuilder();
                for (var index = 1; index < ids.Length; index++)
                {
                    using var tokens = new OnnxTensor(_runtime, ids, [1, index]);
                    using var attention = new OnnxTensor(_runtime, mask, [1, index]);
                    var decoded = _decoder.Run(run, tokens, attention, context[0]);
                    try
                    {
                        var vocabularySize = checked((int)decoded[0].Dimensions[^1]);
                        var id = MaximumIndex(decoded[0].Floats[^vocabularySize..]);
                        if (id == 2) return LatexTextNormalizer.Normalize(result.ToString().Replace("Ġ", " ").Trim());
                        ids[index] = id;
                        if (id > 2) result.Append(_vocabulary[id]);
                    }
                    finally
                    {
                        foreach (var output in decoded) output.Dispose();
                    }
                }

                throw new InvalidOperationException("The formula exceeded the model's output length.");
            }
            finally
            {
                foreach (var output in context) output.Dispose();
            }
        }

        private static int MaximumIndex(ReadOnlySpan<float> values)
        {
            var best = 0;
            for (var index = 1; index < values.Length; index++)
                if (values[index] > values[best])
                    best = index;
            return best;
        }
    }
}
