using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe class CaptureFrameSignal : IDisposable
    {
        private sealed record Subscription(CaptureFrameSignal Signal, bool Closed);

        [StructLayout(LayoutKind.Sequential)]
        private struct Handler
        {
            public nint Vtable, Target;
            public int References;
            public Guid Interface;
        }

        private static readonly nint HandlerVtable = CreateVtable();
        private readonly AutoResetEvent _arrived = new(false);
        private int _closed;
        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        public nint CreateHandler(bool closed)
        {
            var target = GCHandle.Alloc(new Subscription(this, closed));
            Handler* handler;
            try
            {
                handler = (Handler*)NativeMemory.AllocZeroed((nuint)sizeof(Handler));
            }
            catch
            {
                target.Free();
                throw;
            }

            handler->Vtable = HandlerVtable;
            handler->Target = GCHandle.ToIntPtr(target);
            handler->References = 1;
            handler->Interface = closed
                ? new Guid("e9c610c0-a68c-5bd9-8021-8589346eeee2")
                : new Guid("51a947f7-79cf-5a3e-a3a5-1289cfa6dfe8");
            return (nint)handler;
        }

        public void Wait(long deadline, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosed) throw new CaptureSourceLostException();
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0) throw new IOException(UiText.Get("CaptureSourceTimeout"));
            var timeout = (int)Math.Min(remaining, int.MaxValue);
            var signalled = token.CanBeCanceled
                ? WaitHandle.WaitAny([_arrived, token.WaitHandle], timeout) != WaitHandle.WaitTimeout
                : _arrived.WaitOne(timeout);
            token.ThrowIfCancellationRequested();
            if (IsClosed) throw new CaptureSourceLostException();
            if (!signalled) throw new IOException(UiText.Get("CaptureSourceTimeout"));
        }

        private void Notify(bool closed)
        {
            if (closed) Volatile.Write(ref _closed, 1);
            try
            {
                _arrived.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose() => _arrived.Dispose();

        private static nint CreateVtable()
        {
            var table = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(CaptureFrameSignal),
                4 * sizeof(nint));
            table[0] = (nint)(delegate* unmanaged[Stdcall]<Handler*, Guid*, nint*, int>)&Query;
            table[1] = (nint)(delegate* unmanaged[Stdcall]<Handler*, uint>)&AddReference;
            table[2] = (nint)(delegate* unmanaged[Stdcall]<Handler*, uint>)&ReleaseReference;
            table[3] = (nint)(delegate* unmanaged[Stdcall]<Handler*, nint, nint, int>)&Invoke;
            return (nint)table;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Query(Handler* handler, Guid* iid, nint* result)
        {
            if (result == null) return unchecked((int)0x80004003);
            *result = 0;
            if (iid == null) return unchecked((int)0x80004003);
            if (*iid != handler->Interface && *iid != new Guid("00000000-0000-0000-c000-000000000046") &&
                *iid != new Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")) return unchecked((int)0x80004002);
            Interlocked.Increment(ref handler->References);
            *result = (nint)handler;
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddReference(Handler* handler) => (uint)Interlocked.Increment(ref handler->References);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint ReleaseReference(Handler* handler)
        {
            var references = Interlocked.Decrement(ref handler->References);
            if (references != 0) return (uint)references;
            GCHandle.FromIntPtr(handler->Target).Free();
            NativeMemory.Free(handler);
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(Handler* handler, nint sender, nint arguments)
        {
            var subscription = (Subscription)GCHandle.FromIntPtr(handler->Target).Target!;
            subscription.Signal.Notify(subscription.Closed);
            return 0;
        }
    }
}
