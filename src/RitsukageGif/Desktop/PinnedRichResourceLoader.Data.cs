using System.Buffers;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichResourceLoader
    {
        private static byte[] ReadData(string source, long maximumBytes, CancellationToken token)
        {
            var separator = source.IndexOf(',', 5);
            if (separator < 0) throw new FormatException("The data resource has no payload separator.");
            var payload = source.AsSpan(separator + 1);
            if (source.AsSpan(5, separator - 5).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                return ReadBase64Data(payload, maximumBytes, token);
            long length = 0;
            for (var index = 0; index < payload.Length;)
            {
                if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
                if (payload[index] == '%')
                {
                    _ = PercentByte(payload, ref index);
                    length++;
                }
                else if (payload[index] < 128)
                {
                    index++;
                    length++;
                }
                else
                {
                    if (Rune.DecodeFromUtf16(payload[index..], out var rune, out var consumed) != OperationStatus.Done)
                        throw new FormatException("The data resource contains invalid text.");
                    length += rune.Utf8SequenceLength;
                    index += consumed;
                }

                if (length > maximumBytes)
                    throw new InvalidDataException("The image resource exceeds its encoded-byte budget.");
            }

            var bytes = new byte[(int)length];
            var offset = 0;
            for (var index = 0; index < payload.Length;)
            {
                if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
                if (payload[index] == '%')
                {
                    bytes[offset++] = PercentByte(payload, ref index);
                }
                else if (payload[index] < 128)
                {
                    bytes[offset++] = (byte)payload[index++];
                }
                else
                {
                    _ = Rune.DecodeFromUtf16(payload[index..], out var rune, out var consumed);
                    offset += rune.EncodeToUtf8(bytes.AsSpan(offset));
                    index += consumed;
                }
            }

            return bytes;
        }

        private static byte[] ReadBase64Data(ReadOnlySpan<char> payload, long maximumBytes, CancellationToken token)
        {
            var count = 0;
            var padding = 0;
            var maximumCharacters = (maximumBytes + 2) / 3 * 4;
            for (var index = 0; index < payload.Length;)
            {
                if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
                var value = DataAscii(payload, ref index);
                if (WhiteSpace(value)) continue;
                if (value == '=') padding++;
                else if (padding != 0 || !Base64Character(value))
                    throw new FormatException("Invalid base64 data resource.");
                count++;
                if (padding > 2) throw new FormatException("Invalid base64 padding.");
                if (count > maximumCharacters)
                    throw new InvalidDataException("The image resource exceeds its encoded-byte budget.");
            }

            if (count % 4 != 0) throw new FormatException("Invalid base64 data length.");
            var length = (long)count / 4 * 3 - padding;
            if (length < 0) throw new FormatException("Invalid base64 padding.");
            if (length > maximumBytes)
                throw new InvalidDataException("The image resource exceeds its encoded-byte budget.");
            var bytes = new byte[(int)length];
            Span<char> quartet = stackalloc char[4];
            var position = 0;
            var offset = 0;
            for (var index = 0; index < payload.Length;)
            {
                if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
                var value = DataAscii(payload, ref index);
                if (WhiteSpace(value)) continue;
                quartet[position++] = (char)value;
                if (position != 4) continue;
                if (!Convert.TryFromBase64Chars(quartet, bytes.AsSpan(offset), out var written))
                    throw new FormatException("Invalid base64 data resource.");
                offset += written;
                position = 0;
            }

            return bytes;
        }

        private static byte DataAscii(ReadOnlySpan<char> payload, ref int index)
        {
            if (payload[index] == '%') return PercentByte(payload, ref index);
            var value = payload[index++];
            if (value > 127) throw new FormatException("The base64 resource contains non-ASCII text.");
            return (byte)value;
        }

        private static byte PercentByte(ReadOnlySpan<char> payload, ref int index)
        {
            if (index + 2 >= payload.Length)
                throw new FormatException("Invalid data resource percent escape.");
            var high = Hex(payload[index + 1]);
            var low = Hex(payload[index + 2]);
            if (high < 0 || low < 0)
                throw new FormatException("Invalid data resource percent escape.");
            index += 3;
            return (byte)((high << 4) | low);
        }

        private static int Hex(char value)
        {
            return value switch
            {
                >= '0' and <= '9' => value - '0',
                >= 'a' and <= 'f' => value - 'a' + 10,
                >= 'A' and <= 'F' => value - 'A' + 10,
                _ => -1,
            };
        }

        private static bool WhiteSpace(byte value)
        {
            return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
        }

        private static bool Base64Character(byte value)
        {
            return value is >= (byte)'A' and <= (byte)'Z' or
                >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'/';
        }
    }
}
