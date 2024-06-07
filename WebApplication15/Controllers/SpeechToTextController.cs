using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System;
using System.IO;
using System.Threading.Tasks;
using Vosk;

namespace WebApplication15.Controllers
{
    [ApiController]
    [Route("api/speech-to-text")]
    public class SpeechToTextController : ControllerBase
    {
        private readonly VoskRecognizer _recognizer;
        private readonly ILogger<SpeechToTextController> _logger;

        public SpeechToTextController(ILogger<SpeechToTextController> logger)
        {
            _logger = logger;
            Vosk.Vosk.SetLogLevel(0);
            Model model = new Model("model_TR");
            _recognizer = new VoskRecognizer(model, 16000);
        }

        [HttpPost]
        [Route("transcribe")]
        public async Task<IActionResult> TranscribeAudio(IFormFile formFile)
        {
            if (formFile == null)
            {
                return BadRequest("No audio file was uploaded.");
            }

            try
            {
                var audioFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "audio");
                Directory.CreateDirectory(audioFolderPath);

                var audioFilePath = Path.Combine(audioFolderPath, formFile.FileName);
                using (var stream = new FileStream(audioFilePath, FileMode.Create))
                {
                    await formFile.CopyToAsync(stream);
                }

                string finalResult;

                if (Path.GetExtension(formFile.FileName).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = new FileStream(audioFilePath, FileMode.Open))
                    {
                        using (var reader = new WaveFileReader(stream))
                        {
                            var waveFormat = reader.WaveFormat;

                            using (var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 1)))
                            {
                                resampler.ResamplerQuality = 60;

                                var buffer = new byte[waveFormat.AverageBytesPerSecond / 5];
                                int bytesRead;

                                while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    _recognizer.AcceptWaveform(buffer, bytesRead);
                                }
                            }
                        }
                    }

                    finalResult = _recognizer.FinalResult();
                }

                else
                {
                    var wavFilePath = ConvertToWav(audioFilePath);

                    if (string.IsNullOrEmpty(wavFilePath))
                    {
                        return BadRequest("Failed to convert audio to WAV format.");
                    }

                    using (var stream = new FileStream(wavFilePath, FileMode.Open))
                    {
                        var waveFormat = new WaveFormat(16000, 1);

                        using (var resampler = new MediaFoundationResampler(new RawSourceWaveStream(stream, waveFormat), waveFormat))
                        {
                            resampler.ResamplerQuality = 60;

                            var buffer = new byte[waveFormat.AverageBytesPerSecond / 5]; 
                            int bytesRead;

                            while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                _recognizer.AcceptWaveform(buffer, bytesRead);
                            }
                        }
                    }

                    finalResult = _recognizer.FinalResult();
                }

                if (!string.IsNullOrEmpty(finalResult))
                {
                    return Ok(finalResult);
                }
                else
                {
                    return BadRequest("Transcription failed.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio with Vosk.");
                return StatusCode(500, "Internal server error.");
            }
        }


        private string ConvertToWav(string audioFilePath)
        {
            try
            {
                var wavFilePath = Path.ChangeExtension(audioFilePath, ".wav");

                using (var reader = new MediaFoundationReader(audioFilePath))
                {
                    var waveFormat = new WaveFormat(16000, 1);

                    using (var resampler = new MediaFoundationResampler(reader, waveFormat))
                    {
                        WaveFileWriter.CreateWaveFile(wavFilePath, resampler);
                    }
                }

                return wavFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert audio to WAV format.");
                return null;
            }
        }
    }
}
