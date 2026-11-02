namespace RitsukageGif.Core
{
    internal sealed unsafe class OnnxRun : IDisposable
    {
        internal readonly nint Handle;
        internal readonly CancellationToken Token;
        private readonly CancellationTokenRegistration _registration;
        private readonly OnnxRuntimeLibrary _runtime;

        internal OnnxRun(OnnxRuntimeLibrary runtime, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _runtime = runtime;
            Token = token;
            nint handle = 0;
            runtime.Check(((delegate* unmanaged<nint*, nint>)runtime.Api[(int)OnnxApi.CreateRunOptions])(&handle));
            Handle = handle;
            _registration = token.UnsafeRegister(static state =>
            {
                var run = (OnnxRun)state!;
                var status =
                    ((delegate* unmanaged<nint, nint>)run._runtime.Api[
                        (int)OnnxApi.RunOptionsSetTerminate])(run.Handle);
                run._runtime.Release(OnnxApi.ReleaseStatus, status);
            }, this);
        }

        public void Dispose()
        {
            _registration.Dispose();
            _runtime.Release(OnnxApi.ReleaseRunOptions, Handle);
        }
    }
}
