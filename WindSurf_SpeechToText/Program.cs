using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;
using Vosk;

namespace WindSurf_SpeechToText
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logsDir);
                var recognizedLogPath = Path.Combine(AppContext.BaseDirectory, "recognized_log.txt");
                var errorLogPath = Path.Combine(AppContext.BaseDirectory, "error_log.txt");
                var sessionBuffer = new List<string>();
                var listening = false;
                var sessionActive = false;
                var sessionLock = new object();

                var key = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
                var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");

                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(region))
                {
                    // Azure çevrimiçi yol
                    var speechConfig = SpeechConfig.FromSubscription(key, region);
                    speechConfig.SpeechRecognitionLanguage = "tr-TR";
                    speechConfig.OutputFormat = OutputFormat.Detailed;

                    using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
                    using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

                    recognizer.Recognized += (s, e) =>
                    {
                        try
                        {
                            if (e.Result.Reason == ResultReason.RecognizedSpeech)
                            {
                                var text = e.Result.Text;
                                double confidence = TryGetConfidence(e.Result, out var c) ? c : -1;
                                var ts = DateTime.Now.ToString("HH:mm:ss");
                                var confStr = confidence >= 0 ? confidence.ToString("0.00") : "n/a";
                                var line = $"[{ts}] (Conf: {confStr}) Recognized: \"{text}\"";
                                Console.WriteLine(line);
                                AppendLineSafe(recognizedLogPath, line);
                                lock (sessionLock)
                                {
                                    if (sessionActive) sessionBuffer.Add(line);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError(errorLogPath, ex);
                        }
                    };

                    recognizer.Canceled += (s, e) =>
                    {
                        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var msg = $"[{ts}] Canceled: Reason={e.Reason} ErrorCode={e.ErrorCode} Details={e.ErrorDetails}";
                        Console.WriteLine(msg);
                        AppendLineSafe(errorLogPath, msg);
                    };

                    recognizer.SessionStarted += (s, e) => { lock (sessionLock) sessionActive = true; };
                    recognizer.SessionStopped += (s, e) => { lock (sessionLock) sessionActive = false; };

                    Console.WriteLine("Azure modunda hazır. Enter: Başlat/Durdur, 's': Oturumu kaydet");

                    while (true)
                    {
                        if (Console.KeyAvailable)
                        {
                            var keyInfo = Console.ReadKey(true);
                            if (keyInfo.Key == ConsoleKey.Enter)
                            {
                                if (!listening)
                                {
                                    try { await recognizer.StartContinuousRecognitionAsync(); listening = true; Console.WriteLine("Dinleme: AÇIK"); }
                                    catch (Exception ex) { LogError(errorLogPath, ex); }
                                }
                                else
                                {
                                    try { await recognizer.StopContinuousRecognitionAsync(); listening = false; Console.WriteLine("Dinleme: KAPALI"); }
                                    catch (Exception ex) { LogError(errorLogPath, ex); }
                                }
                            }
                            else if (keyInfo.Key == ConsoleKey.S)
                            {
                                SaveSession(logsDir, sessionBuffer);
                            }
                        }
                        await Task.Delay(50);
                    }
                }
                else
                {
                    // Vosk çevrimdışı yol
                    Console.WriteLine("Azure anahtarları yok. Çevrimdışı Vosk moduna geçiliyor.");
                    var modelPath = Environment.GetEnvironmentVariable("VOSK_MODEL");
                    if (string.IsNullOrWhiteSpace(modelPath))
                        modelPath = Path.Combine(AppContext.BaseDirectory, "models", "tr");

                    if (!Directory.Exists(modelPath))
                    {
                        Console.WriteLine("Vosk Türkçe modeli bulunamadı: " + modelPath);
                        Console.WriteLine("Lütfen bir Türkçe model indirin (örn: vosk-model-small-tr-0.3) ve models/tr klasörüne çıkarın veya VOSK_MODEL ortam değişkeni ile yol verin.");
                        return 2;
                    }

                    Vosk.Vosk.SetLogLevel(0);
                    using var model = new Model(modelPath);

                    int deviceCount = 0;
                    try { deviceCount = WaveInEvent.DeviceCount; } catch { deviceCount = 0; }
                    Console.WriteLine("Mevcut mikrofon cihazları:");
                    for (int i = 0; i < deviceCount; i++)
                    {
                        try
                        {
                            var cap = WaveInEvent.GetCapabilities(i);
                            Console.WriteLine($"[{i}] {cap.ProductName} ({cap.Channels}ch)");
                        }
                        catch
                        {
                            Console.WriteLine($"[{i}] (cihaz)");
                        }
                    }
                    int selectedDevice = 0;
                    try
                    {
                        Console.Write("Mikrofon cihaz numarası (Enter=0): ");
                        var inp = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(inp) && int.TryParse(inp, out var idx) && idx >= 0 && idx < deviceCount)
                            selectedDevice = idx;
                    }
                    catch { }

                    int sampleRate = 16000;
                    using var waveIn = new WaveInEvent
                    {
                        DeviceNumber = selectedDevice,
                        WaveFormat = new WaveFormat(sampleRate, 16, 1)
                    };

                    using var recognizer = new VoskRecognizer(model, sampleRate);
                    recognizer.SetMaxAlternatives(0);
                    recognizer.SetWords(true);

                    waveIn.DataAvailable += (s, a) =>
                    {
                        try
                        {
                            if (recognizer.AcceptWaveform(a.Buffer, a.BytesRecorded))
                            {
                                var res = recognizer.Result();
                                var text = ExtractVoskText(res);
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    var ts = DateTime.Now.ToString("HH:mm:ss");
                                    var line = $"[{ts}] Recognized: \"{text}\"";
                                    Console.WriteLine(line);
                                    AppendLineSafe(recognizedLogPath, line);
                                    lock (sessionLock) { if (sessionActive) sessionBuffer.Add(line); }
                                }
                            }
                            else
                            {
                                var partial = recognizer.PartialResult();
                                var ptext = ExtractVoskPartial(partial);
                                if (!string.IsNullOrWhiteSpace(ptext))
                                {
                                    var ts = DateTime.Now.ToString("HH:mm:ss");
                                    var line = $"[{ts}] Partial: \"{ptext}\"";
                                    Console.WriteLine(line);
                                    AppendLineSafe(recognizedLogPath, line);
                                    lock (sessionLock) { if (sessionActive) sessionBuffer.Add(line); }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError(errorLogPath, ex);
                        }
                    };

                    Console.WriteLine("Vosk modunda hazır. Enter: Başlat/Durdur, 's': Oturumu kaydet");

                    while (true)
                    {
                        if (Console.KeyAvailable)
                        {
                            var keyInfo = Console.ReadKey(true);
                            if (keyInfo.Key == ConsoleKey.Enter)
                            {
                                try
                                {
                                    if (!listening)
                                    {
                                        recognizer.Reset();
                                        waveIn.StartRecording();
                                        listening = true;
                                        lock (sessionLock) sessionActive = true;
                                        Console.WriteLine("Dinleme: AÇIK");
                                    }
                                    else
                                    {
                                        waveIn.StopRecording();
                                        listening = false;
                                        lock (sessionLock) sessionActive = false;
                                        Console.WriteLine("Dinleme: KAPALI");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogError(errorLogPath, ex);
                                }
                            }
                            else if (keyInfo.Key == ConsoleKey.S)
                            {
                                SaveSession(logsDir, sessionBuffer);
                            }
                        }
                        await Task.Delay(50);
                    }
                }
            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(AppContext.BaseDirectory, "error_log.txt");
                LogError(errorLogPath, ex);
                Console.WriteLine("Bir hata oluştu. Detaylar error_log.txt içindedir.");
                return 1;
            }
        }

        static void SaveSession(string logsDir, List<string> sessionBuffer)
        {
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var path = Path.Combine(logsDir, $"session-{stamp}.txt");
                File.WriteAllLines(path, sessionBuffer, Encoding.UTF8);
                Console.WriteLine($"Oturum kaydedildi: {path}");
            }
            catch (Exception ex)
            {
                var errorLogPath = Path.Combine(AppContext.BaseDirectory, "error_log.txt");
                LogError(errorLogPath, ex);
            }
        }

        static bool TryGetConfidence(SpeechRecognitionResult result, out double confidence)
        {
            confidence = -1;
            try
            {
                var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                if (string.IsNullOrWhiteSpace(json)) return false;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("NBest", out var nbest) && nbest.ValueKind == JsonValueKind.Array && nbest.GetArrayLength() > 0)
                {
                    var first = nbest[0];
                    if (first.TryGetProperty("Confidence", out var c) && c.TryGetDouble(out var val))
                    {
                        confidence = val;
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        static string ExtractVoskText(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("text", out var t))
                    return t.GetString() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        static string ExtractVoskPartial(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("partial", out var p))
                    return p.GetString() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        static void AppendLineSafe(string path, string line)
        {
            try
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        static void LogError(string errorLogPath, Exception ex)
        {
            try
            {
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var msg = new StringBuilder()
                    .Append('[').Append(ts).Append("] ")
                    .Append(ex.GetType().FullName).Append(": ").Append(ex.Message)
                    .AppendLine()
                    .Append(ex.StackTrace)
                    .ToString();
                AppendLineSafe(errorLogPath, msg);
            }
            catch { }
        }
    }
}
