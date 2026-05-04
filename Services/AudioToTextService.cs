using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EBookDashboard.Services
{
    public class AudioToTextService : IAudioToTextService
    {
        private readonly string _subscriptionKey;
        private readonly string _region;
        private readonly ILogger<AudioToTextService> _logger;

        public AudioToTextService(IConfiguration configuration,
                                  ILogger<AudioToTextService> logger)
        {
            _subscriptionKey = configuration["AzureSpeech:SubscriptionKey"] ?? string.Empty;
            _region = configuration["AzureSpeech:Region"] ?? string.Empty;
            _logger = logger;
        }

        // ================================
        // MAIN ENTRY METHOD
        // ================================
        public async Task<string> ConvertAsync(AudioToTextRequest request)
        {
            if (request.AudioFile == null || request.AudioFile.Length == 0)
                throw new ArgumentException("Audio file is empty");

            var tempWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

            try
            {
                await ConvertWebmToWavAsync(request.AudioFile, tempWav);

                var speechConfig = SpeechConfig.FromSubscription(
                    _subscriptionKey, _region);

                speechConfig.SpeechRecognitionLanguage =
                    request.Language ?? "en-US";

                using var audioConfig = AudioConfig.FromWavFileInput(tempWav);
                using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

                var result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    return result.Text;
                }

                if (result.Reason == ResultReason.NoMatch)
                {
                    _logger.LogWarning("No speech recognized.");
                    return string.Empty;
                }

                if (result.Reason == ResultReason.Canceled)
                {
                    var cancel = CancellationDetails.FromResult(result);
                    throw new Exception($"Speech canceled: {cancel.Reason}");
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio-to-text conversion failed");
                throw;
            }
            finally
            {
                if (File.Exists(tempWav))
                    File.Delete(tempWav);
            }
        }

        // ================================
        // WEBM → WAV CONVERSION (FIXED)
        // ================================
        private async Task ConvertWebmToWavAsync(IFormFile webmFile, string wavPath)
        {
            var tempWebm = Path.Combine(
                Path.GetTempPath(), $"{Guid.NewGuid()}.webm");

            try
            {
                // Save uploaded WebM
                await using (var fs = new FileStream(tempWebm, FileMode.Create))
                {
                    await webmFile.CopyToAsync(fs);
                }

                // MediaFoundationReader REQUIRES FILE PATH
                using var reader = new MediaFoundationReader(tempWebm);

                var targetFormat = new WaveFormat(16000, 16, 1);

                using var resampler =
                    new MediaFoundationResampler(reader, targetFormat)
                    {
                        ResamplerQuality = 60
                    };

                WaveFileWriter.CreateWaveFile(wavPath, resampler);
            }
            finally
            {
                if (File.Exists(tempWebm))
                    File.Delete(tempWebm);
            }
        }
    }
}