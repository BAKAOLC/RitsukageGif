using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static unsafe partial class WindowsSpeechSynthesis
    {
        private const string RuntimeClass = "Windows.Media.SpeechSynthesis.SpeechSynthesizer";

        internal static SpeechCapabilities GetCapabilities()
        {
            Check(RoInitialize(1));
            nint factory = 0, voices = 0, defaultVoice = 0;
            try
            {
                factory = Activate();
                voices = Object(factory, 6);
                defaultVoice = Object(factory, 7);
                uint count = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Table(voices)[7])(voices, &count));
                var result = new List<SpeechVoice>((int)count);
                for (uint index = 0; index < count; index++)
                {
                    nint voice = 0;
                    try
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(voices)[6])(voices, index,
                            &voice));
                        result.Add(new(Text(voice, 7), Text(voice, 6), Text(voice, 8)));
                    }
                    finally
                    {
                        Release(ref voice);
                    }
                }

                return new(result.Count > 0, [.. result], defaultVoice == 0 ? null : Text(defaultVoice, 7),
                    result.Count == 0 ? SpeechUnavailableReason.NoVoices : SpeechUnavailableReason.None);
            }
            finally
            {
                Release(ref defaultVoice);
                Release(ref voices);
                Release(ref factory);
                RoUninitialize();
            }
        }

        internal static void Synthesize(string content, string voiceId, string output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Check(RoInitialize(1));
            nint name = 0,
                factory = 0,
                voices = 0,
                synthObject = 0,
                synth = 0,
                text = 0,
                operation = 0,
                info = 0,
                result = 0,
                random = 0,
                stream = 0;
            try
            {
                Check(WindowsCreateString(RuntimeClass, RuntimeClass.Length, out name));
                Check(RoActivateInstance(name, out synthObject));
                synth = Query(synthObject, new("ce9f7c76-97f4-4ced-ad68-d51c458e45c6"));
                factory = Activate();
                voices = Object(factory, 6);
                uint count = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Table(voices)[7])(voices, &count));
                var found = false;
                for (uint index = 0; index < count; index++)
                {
                    nint voice = 0;
                    try
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(voices)[6])(voices, index,
                            &voice));
                        if (Text(voice, 7) != voiceId) continue;
                        Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Table(synth)[8])(synth, voice));
                        found = true;
                        break;
                    }
                    finally
                    {
                        Release(ref voice);
                    }
                }

                if (!found)
                    throw new InvalidOperationException("The selected system speech voice is no longer installed.");
                Check(WindowsCreateString(content, content.Length, out text));
                Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Table(synth)[6])(synth, text, &operation));
                info = Query(operation, new("00000036-0000-0000-c000-000000000046"));
                while (true)
                {
                    var status = 0;
                    Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Table(info)[7])(info, &status));
                    if (status != 0) break;
                    if (token.IsCancellationRequested)
                    {
                        _ = ((delegate* unmanaged[Stdcall]<nint, int>)Table(info)[9])(info);
                        token.ThrowIfCancellationRequested();
                    }

                    Thread.Sleep(10);
                }

                token.ThrowIfCancellationRequested();
                result = Object(operation, 8);
                random = Query(result, new("905a0fe1-bc53-11df-8c49-001e4fc686da"));
                var id = new Guid("0000000c-0000-0000-c000-000000000046");
                Check(CreateStreamOverRandomAccessStream(random, in id, out stream));
                using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = stackalloc byte[65536];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    uint read = 0;
                    Check(((delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)Table(stream)[3])(stream,
                        buffer, 65536, &read));
                    if (read == 0) break;
                    if (destination.Length + read > 64 * 1024 * 1024)
                        throw new InvalidOperationException("Speech audio exceeds the segment size limit.");
                    destination.Write(new(buffer, (int)read));
                }
            }
            finally
            {
                Release(ref stream);
                Release(ref random);
                Close(ref result);
                if (info != 0) _ = ((delegate* unmanaged[Stdcall]<nint, int>)Table(info)[10])(info);
                Release(ref info);
                Release(ref operation);
                Close(ref synth);
                Release(ref synthObject);
                Release(ref voices);
                Release(ref factory);
                _ = WindowsDeleteString(text);
                _ = WindowsDeleteString(name);
                RoUninitialize();
            }
        }

        private static nint Activate()
        {
            Check(WindowsCreateString(RuntimeClass, RuntimeClass.Length, out var name));
            try
            {
                var id = new Guid("7d526ecc-7533-4c3f-85be-888c2baeebdc");
                Check(RoGetActivationFactory(name, in id, out var result));
                return result;
            }
            finally
            {
                _ = WindowsDeleteString(name);
            }
        }

        private static nint* Table(nint instance)
        {
            return *(nint**)instance;
        }

        private static void Check(int value)
        {
            if (value < 0) Marshal.ThrowExceptionForHR(value);
        }

        private static nint Query(nint value, Guid id)
        {
            nint result = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(value)[0])(value, &id, &result));
            return result;
        }

        private static nint Object(nint value, int slot)
        {
            nint result = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(value)[slot])(value, &result));
            return result;
        }

        private static string Text(nint value, int slot)
        {
            var text = Object(value, slot);
            try
            {
                return new((char*)WindowsGetStringRawBuffer(text, out var length), 0, checked((int)length));
            }
            finally
            {
                _ = WindowsDeleteString(text);
            }
        }

        private static void Release(ref nint value)
        {
            if (value == 0) return;
            ((delegate* unmanaged[Stdcall]<nint, uint>)Table(value)[2])(value);
            value = 0;
        }

        private static void Close(ref nint value)
        {
            if (value == 0) return;
            var id = new Guid("30d5a829-7fa4-4026-83bb-d75bae4ea99e");
            nint closable = 0;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(value)[0])(value, &id, &closable) >= 0)
            {
                _ = ((delegate* unmanaged[Stdcall]<nint, int>)Table(closable)[6])(closable);
                Release(ref closable);
            }

            Release(ref value);
        }

        [LibraryImport("combase.dll")]
        private static partial int RoInitialize(uint kind);

        [LibraryImport("combase.dll")]
        private static partial void RoUninitialize();

        [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int WindowsCreateString(string text, int length, out nint value);

        [LibraryImport("combase.dll")]
        private static partial int WindowsDeleteString(nint text);

        [LibraryImport("combase.dll")]
        private static partial nint WindowsGetStringRawBuffer(nint text, out uint length);

        [LibraryImport("combase.dll")]
        private static partial int RoGetActivationFactory(nint text, in Guid id, out nint value);

        [LibraryImport("combase.dll")]
        private static partial int RoActivateInstance(nint text, out nint value);

        [LibraryImport("shcore.dll")]
        private static partial int CreateStreamOverRandomAccessStream(nint stream, in Guid id, out nint value);
    }
}
