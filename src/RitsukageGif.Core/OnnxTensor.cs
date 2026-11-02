using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    internal sealed unsafe class OnnxTensor : IDisposable
    {
        internal readonly long[] Dimensions;
        internal readonly nint Handle;
        private readonly int _length;
        private readonly OnnxRuntimeLibrary _runtime;
        private readonly OnnxTensorElementType _type;
        private GCHandle _pin;

        internal OnnxTensor(OnnxRuntimeLibrary runtime, float[] data, long[] dimensions)
            : this(runtime, data, OnnxTensorElementType.Float, 4, dimensions)
        {
        }

        internal OnnxTensor(OnnxRuntimeLibrary runtime, long[] data, long[] dimensions)
            : this(runtime, data, OnnxTensorElementType.Int64, 8, dimensions)
        {
        }

        internal OnnxTensor(OnnxRuntimeLibrary runtime, byte[] data, long[] dimensions)
            : this(runtime, data, OnnxTensorElementType.Boolean, 1, dimensions)
        {
        }

        private OnnxTensor(OnnxRuntimeLibrary runtime, Array data, OnnxTensorElementType type, int elementSize,
            long[] dimensions)
        {
            _runtime = runtime;
            _type = type;
            Dimensions = dimensions;
            long length = 1;
            foreach (var dimension in dimensions) length = checked(length * dimension);
            _length = checked((int)length);
            if (_length > data.Length)
                throw new ArgumentException("Tensor dimensions exceed its supplied data.", nameof(dimensions));
            _pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            nint handle = 0;
            try
            {
                fixed (long* shape = dimensions)
                    runtime.Check(
                        ((delegate* unmanaged<nint, nint, nuint, long*, nuint, int, nint*, nint>)runtime.Api[
                            (int)OnnxApi.CreateTensorWithDataAsOrtValue])(
                            runtime.MemoryInfo, _pin.AddrOfPinnedObject(), checked((nuint)(data.Length * elementSize)),
                            shape, (nuint)dimensions.Length, (int)type, &handle));
                Handle = handle;
            }
            catch
            {
                _pin.Free();
                throw;
            }
        }

        internal OnnxTensor(OnnxRuntimeLibrary runtime, nint handle)
        {
            _runtime = runtime;
            Handle = handle;
            nint shapeInfo = 0;
            runtime.Check(
                ((delegate* unmanaged<nint, nint*, nint>)runtime.Api[(int)OnnxApi.GetTensorTypeAndShape])(handle,
                    &shapeInfo));
            try
            {
                var type = 0;
                nuint rank = 0, count = 0;
                runtime.Check(
                    ((delegate* unmanaged<nint, int*, nint>)runtime.Api[(int)OnnxApi.GetTensorElementType])(shapeInfo,
                        &type));
                runtime.Check(
                    ((delegate* unmanaged<nint, nuint*, nint>)runtime.Api[(int)OnnxApi.GetDimensionsCount])(shapeInfo,
                        &rank));
                Dimensions = new long[checked((int)rank)];
                fixed (long* dimensions = Dimensions)
                    runtime.Check(
                        ((delegate* unmanaged<nint, long*, nuint, nint>)runtime.Api[(int)OnnxApi.GetDimensions])(
                            shapeInfo, dimensions, rank));
                runtime.Check(
                    ((delegate* unmanaged<nint, nuint*, nint>)runtime.Api[(int)OnnxApi.GetTensorShapeElementCount])(
                        shapeInfo, &count));
                _length = checked((int)count);
                _type = (OnnxTensorElementType)type;
            }
            finally
            {
                runtime.Release(OnnxApi.ReleaseTensorTypeAndShapeInfo, shapeInfo);
            }
        }

        internal ReadOnlySpan<float> Floats
        {
            get
            {
                if (_type != OnnxTensorElementType.Float)
                    throw new InvalidOperationException("The tensor does not contain float values.");
                nint data = 0;
                _runtime.Check(
                    ((delegate* unmanaged<nint, nint*, nint>)_runtime.Api[(int)OnnxApi.GetTensorMutableData])(Handle,
                        &data));
                return new((void*)data, _length);
            }
        }

        public void Dispose()
        {
            _runtime.Release(OnnxApi.ReleaseValue, Handle);
            if (_pin.IsAllocated) _pin.Free();
        }
    }
}
