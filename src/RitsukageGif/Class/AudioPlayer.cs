using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NAudio.Vorbis;
using NAudio.Wave;
using RitsukageGif.Enums;

namespace RitsukageGif.Class
{
    internal class AudioPlayer
    {
        private readonly IWavePlayer _audioPlaybackDevice = new WaveOutEvent
        {
            DeviceNumber = -1, // use default audio device
        };

        private readonly HttpClient _httpClient = new();
        private Stream _stream;

        public void PlayAudio(string uri)
        {
            PlayAudio(new Uri(uri));
        }

        public async void PlayAudio(Uri uri)
        {
            StopAudio();
            var audioFormat = await GetAudioFormatAsync(uri).ConfigureAwait(false);
            _stream = await OpenStreamAsync(uri).ConfigureAwait(false);
            IWaveProvider waveProvider = audioFormat switch
            {
                AudioFormat.Mp3 => new Mp3FileReader(_stream),
                AudioFormat.Wav => new WaveFileReader(_stream),
                AudioFormat.Ogg => new VorbisWaveReader(_stream),
                AudioFormat.Unknown => throw new NotSupportedException("Audio format not supported"),
                _ => throw new NotSupportedException("Audio format not supported"),
            };

            _audioPlaybackDevice.Init(waveProvider);
            _audioPlaybackDevice.Play();
        }

        public void StopAudio()
        {
            _audioPlaybackDevice.Stop();
            _stream?.Dispose();
            _stream = null;
        }

        private async Task<Stream> OpenStreamAsync(Uri uri)
        {
            switch (uri.Scheme)
            {
                case "file":
                    return File.OpenRead(uri.LocalPath);
                case "embedded":
                    return EmbeddedResourcesHelper.GetStream(uri);
                case "http":
                case "https":
                    var hash = CalcUriHash(uri);
                    var filePath = Path.Combine(Path.GetTempPath(), hash);
                    if (File.Exists(filePath))
                        return File.OpenRead(filePath);

                    var stream = await _httpClient.GetStreamAsync(uri).ConfigureAwait(false);
                    var fileStream = File.Create(filePath);
                    await stream.CopyToAsync(fileStream).ConfigureAwait(false);
                    fileStream.Dispose();
                    stream.Dispose();

                    return File.OpenRead(filePath);
                default:
                    throw new NotSupportedException("Uri scheme not supported");
            }
        }

        private async Task<AudioFormat> GetAudioFormatAsync(Uri uri)
        {
            using var stream = await OpenStreamAsync(uri).ConfigureAwait(false);

            var buffer = new byte[4];
            var offset = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (offset < buffer.Length) return AudioFormat.Unknown;
            var streamHeader = string.Join("", buffer.Select(element => element.ToString("X2")));
            if (streamHeader.StartsWith("494433"))
                return AudioFormat.Mp3;
            return streamHeader switch
            {
                "52494646" => AudioFormat.Wav,
                "4F676753" => AudioFormat.Ogg,
                _ => AudioFormat.Unknown,
            };
        }

        private static string CalcUriHash(Uri uri)
        {
            using var hmac = MD5.Create();
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(uri.ToString()));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }
}