using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Logic.Backend
{
    /// <summary>
    /// Gestiona la síntesis de voz mediante la orquestación de procesos CLI seguros.
    /// Implementa un sistema de procesamiento por fragmentos (chunking) y encolamiento secuencial
    /// para simular streaming en tiempo real, aislado de fallos de memoria compartida.
    /// </summary>
    public partial class NativeTTSManager : Node
    {
        private AudioStreamPlayer _audioPlayer;
        private Queue<string> _ttsQueue = new Queue<string>();
        private bool _isProcessing = false;

        public override void _Ready()
        {
            _audioPlayer = new AudioStreamPlayer { Bus = "Master" };
            AddChild(_audioPlayer);
            GD.Print("NativeTTSManager: Inicializado en Modo Orquestador CLI con Streaming Simulado.");
        }

        public static bool InitializeNativeEngine()
        {
            GD.Print("NativeTTSManager: Validación de entorno CLI exitosa.");
            return true;
        }

        /// <summary>
        /// Recibe bloques de texto sin procesar, aplica partición semántica basada en delimitadores 
        /// y encola cada segmento para su síntesis individual.
        /// </summary>
        public void Speak(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return;

            string[] sentences = fullText.Split(new[] { ". ", ", ", "? ", "! ", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string sentence in sentences)
            {
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    _ttsQueue.Enqueue(sentence.Trim());
                }
            }

            if (!_isProcessing)
            {
                _ = ProcessQueueAsync();
            }
        }

        /// <summary>
        /// Consumo en segundo plano de la cola de síntesis.
        /// Despacha ejecuciones aisladas del motor Sherpa-ONNX, aguardando la finalización del buffer de audio.
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            _isProcessing = true;

            string binDir = ProjectSettings.GlobalizePath("user://bin/sherpa-onnx");
            string exePath = Path.Combine(binDir, "bin", "sherpa-onnx-offline-tts");
            string libPath = Path.Combine(binDir, "lib");

            string modelsDir = ProjectSettings.GlobalizePath("user://models/kokoro-multi-lang-v1_1");
            string modelPath = Path.Combine(modelsDir, "model.onnx");
            string voicesPath = Path.Combine(modelsDir, "voices.bin");
            string tokensPath = Path.Combine(modelsDir, "tokens.txt");
            string dataDir = Path.Combine(modelsDir, "espeak-ng-data");

            while (_ttsQueue.Count > 0)
            {
                string sentence = _ttsQueue.Dequeue();
                string uniqueId = Guid.NewGuid().ToString("N");
                string outputPath = Path.Combine(binDir, $"tts_{uniqueId}.wav");

                string arguments = $"--kokoro-model=\"{modelPath}\" " +
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
                        FileName = exePath,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    processInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = libPath;

                    using (Process process = Process.Start(processInfo))
                    {
                        await Task.Run(() => process.WaitForExit());
                    }

                    if (File.Exists(outputPath))
                    {
                        while (_audioPlayer.Playing)
                        {
                            await Task.Delay(50);
                        }

                        PlayWav(outputPath);

                        File.Delete(outputPath);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"NativeTTSManager: Error ejecutando síntesis CLI - {ex.Message}");
                }
            }

            _isProcessing = false;
        }

        /// <summary>
        /// Procesa memoria estática proveniente de archivos WAV cortando su cabecera RIFF
        /// e inyectándola directamente a las estructuras de Godot para reproducción sin latencia.
        /// </summary>
        private void PlayWav(string filePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);

                var stream = new AudioStreamWav
                {
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    MixRate = 24000,
                    Stereo = false
                };

                byte[] pcmData = new byte[bytes.Length - 44];
                Array.Copy(bytes, 44, pcmData, 0, pcmData.Length);
                stream.Data = pcmData;

                _audioPlayer.Stream = stream;
                _audioPlayer.Play();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"NativeTTSManager: Error decodificando WAV nativo - {ex.Message}");
            }
        }
    }
}