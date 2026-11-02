using System.Runtime.InteropServices;
using System.Text;

namespace RitsukageGif.Core
{
    internal sealed unsafe class OnnxInferenceSession : IDisposable
    {
        private readonly nint[] _inputNames;
        private readonly OnnxTensorInfo[] _inputs;
        private readonly nint[] _outputNames;
        private readonly OnnxTensorInfo[] _outputs;
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly nint _session;

        internal OnnxInferenceSession(OnnxRuntimeLibrary runtime, string path)
        {
            _runtime = runtime;
            nint options = 0, session = 0;
            runtime.Check(((delegate* unmanaged<nint*, nint>)runtime.Api[(int)OnnxApi.CreateSessionOptions])(&options));
            try
            {
                runtime.Check(
                    ((delegate* unmanaged<nint, int, nint>)runtime.Api[(int)OnnxApi.SetIntraOpNumThreads])(options,
                        Math.Min(4, Environment.ProcessorCount)));
                runtime.Check(
                    ((delegate* unmanaged<nint, int, nint>)runtime.Api[(int)OnnxApi.SetSessionGraphOptimizationLevel])(
                        options, 99));
                if (OperatingSystem.IsWindows())
                {
                    fixed (char* file = Path.GetFullPath(path))
                        runtime.Check(
                            ((delegate* unmanaged<nint, void*, nint, nint*, nint>)runtime.Api[
                                (int)OnnxApi.CreateSession])(runtime.Environment, file, options, &session));
                }
                else
                {
                    var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(path) + '\0');
                    fixed (byte* file = bytes)
                        runtime.Check(
                            ((delegate* unmanaged<nint, void*, nint, nint*, nint>)runtime.Api[
                                (int)OnnxApi.CreateSession])(runtime.Environment, file, options, &session));
                }

                _session = session;
                _inputs = ReadTensorInfo(true);
                _outputs = ReadTensorInfo(false);
                _inputNames = [.. _inputs.Select(info => Marshal.StringToCoTaskMemUTF8(info.Name))];
                _outputNames = [.. _outputs.Select(info => Marshal.StringToCoTaskMemUTF8(info.Name))];
            }
            catch
            {
                runtime.Release(OnnxApi.ReleaseSession, session);
                throw;
            }
            finally
            {
                runtime.Release(OnnxApi.ReleaseSessionOptions, options);
            }
        }

        public void Dispose()
        {
            _runtime.Release(OnnxApi.ReleaseSession, _session);
            foreach (var name in _inputNames) Marshal.FreeCoTaskMem(name);
            foreach (var name in _outputNames) Marshal.FreeCoTaskMem(name);
        }

        private OnnxTensorInfo[] ReadTensorInfo(bool input)
        {
            nuint count = 0;
            _runtime.Check(
                ((delegate* unmanaged<nint, nuint*, nint>)_runtime.Api[
                    (int)(input ? OnnxApi.SessionGetInputCount : OnnxApi.SessionGetOutputCount)])(_session, &count));
            var result = new OnnxTensorInfo[checked((int)count)];
            for (var index = 0; index < result.Length; index++)
            {
                nint name = 0;
                _runtime.Check(
                    ((delegate* unmanaged<nint, nuint, nint, nint*, nint>)_runtime.Api[
                        (int)(input ? OnnxApi.SessionGetInputName : OnnxApi.SessionGetOutputName)])(_session,
                        (nuint)index, _runtime.Allocator, &name));
                result[index] = new(_runtime.AllocatedString(name));
            }

            return result;
        }

        internal string? Metadata(string key)
        {
            nint metadata = 0, text = 0;
            _runtime.Check(
                ((delegate* unmanaged<nint, nint*, nint>)_runtime.Api[(int)OnnxApi.SessionGetModelMetadata])(_session,
                    &metadata));
            try
            {
                var bytes = Encoding.UTF8.GetBytes(key + '\0');
                fixed (byte* name = bytes)
                    _runtime.Check(
                        ((delegate* unmanaged<nint, nint, byte*, nint*, nint>)_runtime.Api[
                            (int)OnnxApi.ModelMetadataLookupCustomMetadataMap])(metadata, _runtime.Allocator, name,
                            &text));
                return text == 0 ? null : _runtime.AllocatedString(text);
            }
            finally
            {
                _runtime.Release(OnnxApi.ReleaseModelMetadata, metadata);
            }
        }

        internal OnnxTensor[] Run(OnnxRun run, params OnnxTensor[] inputs)
        {
            run.Token.ThrowIfCancellationRequested();
            if (inputs.Length != _inputs.Length)
                throw new ArgumentException("The supplied tensor count does not match the model.", nameof(inputs));
            var values = stackalloc nint[inputs.Length];
            for (var index = 0; index < inputs.Length; index++) values[index] = inputs[index].Handle;
            var outputs = new nint[_outputs.Length];
            var result = new OnnxTensor[outputs.Length];
            try
            {
                fixed (nint* inputNames = _inputNames)
                fixed (nint* outputNames = _outputNames)
                fixed (nint* outputValues = outputs)
                {
                    var status =
                        ((delegate* unmanaged<nint, nint, nint*, nint*, nuint, nint*, nuint, nint*, nint>)_runtime.Api[
                            (int)OnnxApi.Run])(
                            _session, run.Handle, inputNames, values, (nuint)inputs.Length, outputNames,
                            (nuint)outputs.Length, outputValues);
                    if (status != 0 && run.Token.IsCancellationRequested)
                    {
                        _runtime.Release(OnnxApi.ReleaseStatus, status);
                        run.Token.ThrowIfCancellationRequested();
                    }

                    _runtime.Check(status);
                }

                for (var index = 0; index < outputs.Length; index++)
                {
                    result[index] = new(_runtime, outputs[index]);
                    outputs[index] = 0;
                }

                return result;
            }
            catch
            {
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                foreach (var tensor in result) tensor?.Dispose();
                foreach (var output in outputs) _runtime.Release(OnnxApi.ReleaseValue, output);
                throw;
            }
        }
    }
}
