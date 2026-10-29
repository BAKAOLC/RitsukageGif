using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static unsafe partial class WindowsOcrRecognition
    {
        private static readonly Guid EngineStatics = new("5bffa85a-3384-3540-9940-699120d428a8");

        public static OcrCapabilities GetCapabilities()
        {
            Check(RoInitialize(1));
            nint factory = 0, languages = 0;
            try
            {
                factory = Activate("Windows.Media.Ocr.OcrEngine", EngineStatics);
                uint maximum = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Table(factory)[6])(factory, &maximum));
                languages = ObjectProperty(factory, 7);
                var result = new List<OcrLanguage>();
                for (uint index = 0, count = Count(languages); index < count; index++)
                {
                    var language = At(languages, index);
                    try
                    {
                        result.Add(new(Text(language, 6), Text(language, 7), Text(language, 8)));
                    }
                    finally
                    {
                        Release(ref language);
                    }
                }

                return new(result.Count != 0, checked((int)maximum), result.AsReadOnly(),
                    result.Count == 0 ? "No Windows OCR recognition language is installed." : null);
            }
            finally
            {
                Release(ref languages);
                Release(ref factory);
                RoUninitialize();
            }
        }

        public static OcrRecognitionResult Recognize(byte[] pixels, int width, int height,
            string? languageTag, CancellationToken token)
        {
            Check(RoInitialize(1));
            nint factory = 0, engine = 0, bitmap = 0, operation = 0, asyncInfo = 0, result = 0;
            try
            {
                factory = Activate("Windows.Media.Ocr.OcrEngine", EngineStatics);
                engine = CreateEngine(factory, languageTag);
                if (engine == 0)
                    throw new NotSupportedException("The requested Windows OCR language is not installed.");
                bitmap = CreateBitmap(pixels, width, height);
                token.ThrowIfCancellationRequested();
                Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Table(engine)[6])(engine, bitmap,
                    &operation));
                asyncInfo = Query(operation, new("00000036-0000-0000-c000-000000000046"));
                var cancellationSent = false;
                while (true)
                {
                    var status = 0;
                    Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Table(asyncInfo)[7])(asyncInfo, &status));
                    if (status != 0) break;
                    if (token.IsCancellationRequested && !cancellationSent)
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint, int>)Table(asyncInfo)[9])(asyncInfo));
                        cancellationSent = true;
                    }

                    Thread.Sleep(10);
                }

                token.ThrowIfCancellationRequested();
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(operation)[8])(operation, &result));
                var recognizerLanguage = ObjectProperty(engine, 7);
                try
                {
                    return ReadResult(result, Text(recognizerLanguage, 6), width, height, token);
                }
                finally
                {
                    Release(ref recognizerLanguage);
                }
            }
            finally
            {
                Release(ref result);
                if (asyncInfo != 0)
                    ((delegate* unmanaged[Stdcall]<nint, int>)Table(asyncInfo)[10])(asyncInfo);
                Release(ref asyncInfo);
                Release(ref operation);
                Close(ref bitmap);
                Release(ref engine);
                Release(ref factory);
                RoUninitialize();
            }
        }

        private static nint CreateEngine(nint factory, string? tag)
        {
            nint engine = 0;
            if (string.IsNullOrWhiteSpace(tag))
            {
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(factory)[10])(factory, &engine));
            }
            else
            {
                var languageFactory = Activate("Windows.Globalization.Language",
                    new("9b0252ac-0c27-44f8-b792-9793fb66c63e"));
                nint text = 0, language = 0;
                try
                {
                    Check(WindowsCreateString(tag, tag.Length, out text));
                    Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Table(languageFactory)[6])(
                        languageFactory, text, &language));
                    Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Table(factory)[9])(factory, language,
                        &engine));
                }
                finally
                {
#pragma warning disable CA1806
                    WindowsDeleteString(text);
#pragma warning restore CA1806
                    Release(ref language);
                    Release(ref languageFactory);
                }
            }

            return engine;
        }

        private static nint CreateBitmap(byte[] pixels, int width, int height)
        {
            var bufferFactory = Activate("Windows.Storage.Streams.Buffer", new("71af914d-c10f-484b-bc50-14bc623b3a27"));
            nint buffer = 0, access = 0, bitmapFactory = 0, bitmap = 0;
            try
            {
                Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(bufferFactory)[6])(bufferFactory,
                    checked((uint)pixels.Length), &buffer));
                access = Query(buffer, new("905a0fef-bc53-11df-8c49-001e4fc686da"));
                byte* destination = null;
                Check(((delegate* unmanaged[Stdcall]<nint, byte**, int>)Table(access)[3])(access, &destination));
                pixels.CopyTo(new Span<byte>(destination, pixels.Length));
                Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Table(buffer)[8])(buffer, (uint)pixels.Length));
                bitmapFactory = Activate("Windows.Graphics.Imaging.SoftwareBitmap",
                    new("df0385db-672f-4a9d-806e-c2442f343e86"));
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, int, int, nint*, int>)Table(bitmapFactory)
                    [10])(
                    bitmapFactory, buffer, 87, width, height, 2, &bitmap));
                return bitmap;
            }
            finally
            {
                Release(ref bitmapFactory);
                Release(ref access);
                Release(ref buffer);
                Release(ref bufferFactory);
            }
        }

        private static OcrRecognitionResult ReadResult(nint result, string tag, int width, int height,
            CancellationToken token)
        {
            var lineVector = ObjectProperty(result, 6);
            var angle = ObjectProperty(result, 7);
            try
            {
                double? textAngle = null;
                if (angle != 0)
                {
                    double value = 0;
                    Check(((delegate* unmanaged[Stdcall]<nint, double*, int>)Table(angle)[6])(angle, &value));
                    textAngle = value;
                }

                var lines = new List<OcrLine>();
                var rectangle = stackalloc float[4];
                for (uint index = 0, count = Count(lineVector); index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var line = At(lineVector, index);
                    nint wordVector = 0;
                    try
                    {
                        wordVector = ObjectProperty(line, 6);
                        var words = new List<OcrWord>();
                        float left = float.PositiveInfinity, top = float.PositiveInfinity, right = 0, bottom = 0;
                        for (uint wordIndex = 0, wordCount = Count(wordVector); wordIndex < wordCount; wordIndex++)
                        {
                            var word = At(wordVector, wordIndex);
                            try
                            {
                                Check(
                                    ((delegate* unmanaged[Stdcall]<nint, float*, int>)Table(word)[6])(word, rectangle));
                                words.Add(new(Text(word, 7),
                                    new(rectangle[0], rectangle[1], rectangle[2], rectangle[3])));
                                left = Math.Min(left, rectangle[0]);
                                top = Math.Min(top, rectangle[1]);
                                right = Math.Max(right, rectangle[0] + rectangle[2]);
                                bottom = Math.Max(bottom, rectangle[1] + rectangle[3]);
                            }
                            finally
                            {
                                Release(ref word);
                            }
                        }

                        lines.Add(new(Text(line, 7),
                            words.Count == 0 ? default : new(left, top, right - left, bottom - top),
                            words.AsReadOnly()));
                    }
                    finally
                    {
                        Release(ref wordVector);
                        Release(ref line);
                    }
                }

                return new(Text(result, 8), tag, width, height, textAngle, lines.AsReadOnly());
            }
            finally
            {
                Release(ref angle);
                Release(ref lineVector);
            }
        }

        private static nint* Table(nint instance)
        {
            return *(nint**)instance;
        }

        private static void Check(int result)
        {
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }

        private static nint Activate(string name, Guid id)
        {
            Check(WindowsCreateString(name, name.Length, out var text));
            try
            {
                Check(RoGetActivationFactory(text, in id, out var value));
                return value;
            }
            finally
            {
#pragma warning disable CA1806
                WindowsDeleteString(text);
#pragma warning restore CA1806
            }
        }

        private static nint Query(nint instance, Guid id)
        {
            nint value = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(instance)[0])(instance, &id, &value));
            return value;
        }

        private static nint ObjectProperty(nint instance, int slot)
        {
            nint value = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(instance)[slot])(instance, &value));
            return value;
        }

        private static uint Count(nint vector)
        {
            uint count = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Table(vector)[7])(vector, &count));
            return count;
        }

        private static nint At(nint vector, uint index)
        {
            nint value = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(vector)[6])(vector, index, &value));
            return value;
        }

        private static string Text(nint instance, int slot)
        {
            var text = ObjectProperty(instance, slot);
            try
            {
                var characters = WindowsGetStringRawBuffer(text, out var length);
                return new((char*)characters, 0, checked((int)length));
            }
            finally
            {
#pragma warning disable CA1806
                WindowsDeleteString(text);
#pragma warning restore CA1806
            }
        }

        private static void Release(ref nint instance)
        {
            if (instance == 0) return;
            ((delegate* unmanaged[Stdcall]<nint, uint>)Table(instance)[2])(instance);
            instance = 0;
        }

        private static void Close(ref nint instance)
        {
            if (instance == 0) return;
            var id = new Guid("30d5a829-7fa4-4026-83bb-d75bae4ea99e");
            nint closable = 0;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(instance)[0])(instance, &id, &closable) >=
                0)
            {
                ((delegate* unmanaged[Stdcall]<nint, int>)Table(closable)[6])(closable);
                Release(ref closable);
            }

            Release(ref instance);
        }

        [LibraryImport("combase.dll")]
        private static partial int RoInitialize(uint type);

        [LibraryImport("combase.dll")]
        private static partial void RoUninitialize();

        [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int WindowsCreateString(string text, int length, out nint value);

        [LibraryImport("combase.dll")]
        // ReSharper disable once UnusedMethodReturnValue.Local
        private static partial int WindowsDeleteString(nint text);

        [LibraryImport("combase.dll")]
        private static partial nint WindowsGetStringRawBuffer(nint text, out uint length);

        [LibraryImport("combase.dll")]
        private static partial int RoGetActivationFactory(nint name, in Guid iid, out nint result);
    }
}
