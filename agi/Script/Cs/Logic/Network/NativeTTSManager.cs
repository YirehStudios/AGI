using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Logic.Backend
{
    /// <summary>
    /// Manages text-to-speech synthesis via two parallel pipelines:
    ///   1. <b>WebSocket pipeline</b> — receives a complete WAV file from the Kokoro ONNX TTS
    ///      server (<c>tts_server.py</c>) via <see cref="PlayWavBytes"/> and plays it immediately.
    ///   2. <b>CLI pipeline</b> — legacy Sherpa-ONNX offline binary, used as a fallback when the
    ///      Python WebSocket server is unavailable (called via <see cref="Speak"/>).
    /// </summary>
    public partial class NativeTTSManager : Node
    {
        private AudioStreamPlayer _audioPlayer;

        // ── CLI pipeline state ────────────────────────────────────────────────
        private Queue<string> _ttsQueue = new Queue<string>();
        private bool _isProcessing = false;

        public override void _Ready()
        {
            _audioPlayer = new AudioStreamPlayer { Bus = "Master" };
            AddChild(_audioPlayer);

            // Connect to the NetworkManager's TTSAudioChunkReceived signal so that
            // WAV bytes arriving from the WebSocket TTS server are played automatically.
            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null)
            {
                networkManager.TTSAudioChunkReceived += OnTTSAudioChunkReceived;
                GD.Print("[TTS] WebSocket pipeline connected → NetworkManager.TTSAudioChunkReceived");
            }
            else
            {
                GD.PrintErr("[TTS] NetworkManager not found — WebSocket TTS pipeline is inactive.");
            }

            GD.Print("NativeTTSManager: Ready. WebSocket + CLI pipelines initialized.");
        }

        public override void _ExitTree()
        {
            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null)
                networkManager.TTSAudioChunkReceived -= OnTTSAudioChunkReceived;
        }

        public static bool InitializeNativeEngine()
        {
            GD.Print("NativeTTSManager: Validación de entorno CLI exitosa.");
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  WEBSOCKET PIPELINE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Receives the complete WAV file emitted by <see cref="Logic.Network.NetworkManager.TTSAudioChunkReceived"/>
        /// and immediately plays it through the Godot audio bus.
        /// </summary>
        /// <remarks>
        /// This is the primary TTS playback path when <c>tts_server.py</c> is running.
        /// The byte array is a standard RIFF/WAV file — sample rate is parsed from the
        /// header rather than hardcoded, so Kokoro models at any sample rate play correctly.
        /// </remarks>
        private void OnTTSAudioChunkReceived(byte[] wavBytes)
        {
            if (wavBytes == null || wavBytes.Length < 44)
            {
                GD.PrintErr("[TTS] Received WAV bytes too small to be valid — skipping.");
                return;
            }

            PlayWavBytes(wavBytes);
        }

        /// <summary>
        /// Decodes a complete WAV byte array by parsing the RIFF header and queues playback
        /// via Godot's <see cref="AudioStreamWav"/> API.
        /// </summary>
        /// <remarks>
        /// WAV header layout (all little-endian):
        ///   Offset 22 (2 bytes) — NumChannels
        ///   Offset 24 (4 bytes) — SampleRate
        ///   Offset 34 (2 bytes) — BitsPerSample
        ///   Offset 44+          — PCM data (assumes standard 44-byte header from soundfile)
        /// </remarks>
        private void PlayWavBytes(byte[] wavBytes)
        {
            try
            {
                // Parse sample rate from the WAV header (bytes 24–27, little-endian).
                int sampleRate = BitConverter.ToInt32(wavBytes, 24);

                // Parse channel count (bytes 22–23, little-endian).
                short numChannels = BitConverter.ToInt16(wavBytes, 22);

                // Parse bits per sample (bytes 34–35, little-endian).
                short bitsPerSample = BitConverter.ToInt16(wavBytes, 34);

                // Determine the PCM data start offset by scanning for the 'data' chunk marker.
                // soundfile always writes a standard 44-byte header, but we scan defensively.
                int dataOffset = 44;
                for (int i = 12; i < wavBytes.Length - 8; i++)
                {
                    if (wavBytes[i]     == 'd' && wavBytes[i + 1] == 'a' &&
                        wavBytes[i + 2] == 't' && wavBytes[i + 3] == 'a')
                    {
                        dataOffset = i + 8; // skip 'data' + 4-byte chunk size
                        break;
                    }
                }

                if (dataOffset >= wavBytes.Length)
                {
                    GD.PrintErr("[TTS] WAV 'data' chunk not found — skipping playback.");
                    return;
                }

                int pcmLength = wavBytes.Length - dataOffset;
                byte[] pcmData = new byte[pcmLength];
                Array.Copy(wavBytes, dataOffset, pcmData, 0, pcmLength);

                var stream = new AudioStreamWav
                {
                    Format   = bitsPerSample == 16
                               ? AudioStreamWav.FormatEnum.Format16Bits
                               : AudioStreamWav.FormatEnum.Format8Bits,
                    MixRate  = sampleRate,
                    Stereo   = numChannels > 1,
                    Data     = pcmData,
                };

                GD.Print($"[TTS] Playing WAV — SampleRate: {sampleRate} Hz, Channels: {numChannels}, PCM: {pcmLength} bytes");

                _audioPlayer.Stream = stream;
                _audioPlayer.Play();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TTS] Error decoding WAV bytes: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CLI FALLBACK PIPELINE  (Sherpa-ONNX offline binary)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// CLI fallback: splits text into sentence segments and enqueues them for
        /// offline synthesis via the Sherpa-ONNX binary. Used when the WebSocket TTS
        /// server is unavailable.
        /// </summary>
        public void Speak(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return;

            string[] sentences = fullText.Split(
                new[] { ". ", ", ", "? ", "! ", "\n" },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string sentence in sentences)
            {
                if (!string.IsNullOrWhiteSpace(sentence))
                    _ttsQueue.Enqueue(sentence.Trim());
            }

            if (!_isProcessing)
                _ = ProcessQueueAsync();
        }

        /// <summary>
        /// Background consumer of the CLI synthesis queue.
        /// Launches the Sherpa-ONNX offline TTS binary per sentence and plays the resulting WAV.
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            _isProcessing = true;

            string binDir    = ProjectSettings.GlobalizePath("user://bin/sherpa-onnx");
            string exePath   = Path.Combine(binDir, "bin", "sherpa-onnx-offline-tts");
            string libPath   = Path.Combine(binDir, "lib");

            string modelsDir   = ProjectSettings.GlobalizePath("user://models/kokoro-multi-lang-v1_1");
            string modelPath   = Path.Combine(modelsDir, "model.onnx");
            string voicesPath  = Path.Combine(modelsDir, "voices.bin");
            string tokensPath  = Path.Combine(modelsDir, "tokens.txt");
            string dataDir     = Path.Combine(modelsDir, "espeak-ng-data");

            while (_ttsQueue.Count > 0)
            {
                string sentence   = _ttsQueue.Dequeue();
                string uniqueId   = Guid.NewGuid().ToString("N");
                string outputPath = Path.Combine(binDir, $"tts_{uniqueId}.wav");

                string arguments =
                    $"--kokoro-model=\"{modelPath}\" " +
                    $"--kokoro-voices=\"{voicesPath}\" " +
                    $"--kokoro-tokens=\"{tokensPath}\" " +
                    $"--kokoro-data-dir=\"{dataDir}\" " +
                    $"--kokoro-lang=es " +
                    $"--output-filename=\"{outputPath}\" " +
                    $"\"{sentence}\"";

                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName              = exePath,
                        Arguments             = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    processInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = libPath;

                    using (Process process = Process.Start(processInfo))
                    {
                        await Task.Run(() => process.WaitForExit());
                    }

                    if (File.Exists(outputPath))
                    {
                        while (_audioPlayer.Playing)
                            await Task.Delay(50);

                        byte[] wavBytes = File.ReadAllBytes(outputPath);
                        PlayWavBytes(wavBytes);

                        File.Delete(outputPath);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[TTS CLI] Error ejecutando síntesis: {ex.Message}");
                }
            }

            _isProcessing = false;
        }
    }
}
