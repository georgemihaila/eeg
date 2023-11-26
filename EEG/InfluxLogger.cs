using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using FftSharp;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using RestSharp;

namespace EEG
{
    public sealed class InfluxLogger : IDisposable
    {
        private readonly InfluxDBClient _client;

        public InfluxLogger(InfluxDBClient client)
        {
            _client = client;
        }

        public int Written = 0;
        public int Dropped = 0;
        public object LockObject { get; } = new();
        public int QueueSize => _queue.Count;
        public const int MAX_QUEUE_SIZE = 256 * 10 * 2; // 10 seconds of data
        private readonly ConcurrentBag<EEGReading> _queue = new();
        private bool _running = false;
        private CancellationTokenSource _cts = new();

        public void StartBackgroundTask()
        {
            if (_running) return;
            RunAsync(_cts.Token);
        }

        private void RunAsync(CancellationToken token)
        {
            try
            {
                if (!_running)
                {
                    Task.Run(async () =>
                   {
                       _running = true;
                       WriteApiAsync writeApi = _client.GetWriteApiAsync();
                       while (!token.IsCancellationRequested)
                       {
                           try
                           {
                               if (_queue.Count < 1024)
                               { // Dump data only if we have quite a bit of entries awaiting
                                   await Task.Delay(100, token);
                                   continue;
                               }
                               var data = _queue.Select(reading => PointData.Builder
    .Measurement($"channel_{reading.Channel}")
    .Field("current_reading", value: (double)(reading.Value ?? 0))
    .Timestamp(reading.Timestamp, WritePrecision.Ns)
    .ToPointData()).ToList();
                               // Write FFT
                               var firstTimestamp = _queue.First().Timestamp.UtcDateTime;
                               var lastTimestamp = _queue.Last().Timestamp.UtcDateTime;
                               var seconds = Math.Max(Math.Abs((int)Math.Truncate((lastTimestamp - firstTimestamp).TotalSeconds)), 1);

                               var fft0 = DoChunkedFFT(seconds, 0);
                               var fft1 = DoChunkedFFT(seconds, 1);
                               await Task.WhenAll(
                                    Task.Run(async () => await ProcessChunksAsync(fft0, 0)),
                                    Task.Run(async () => await ProcessChunksAsync(fft1, 1)),
                                    Task.Run(async () => IncrementInterlocks(await writeApi.WritePointsAsyncWithIRestResponse(data)))
                                );
                               _queue.Clear();
                           }
                           catch (Exception x)
                           {
                               System.IO.File.AppendAllLines("influxdb_errors.log", [x.ToString()]);
                               Interlocked.Increment(ref Dropped);
                           }
                       }
                   }, token);
                }
            }
            catch (OperationCanceledException)
            {

            }
            finally
            {
                _running = false;
            }
        }

        private async Task ProcessChunksAsync(IEnumerable<FFTAnalysis> fftData, int channel)
        {
            if (!fftData.Any()) return;

            var firstTimestamp = _queue.First().Timestamp;
            var intervalSeconds = 1; // Determine the time interval each FFT analysis represents

            var data = fftData.SelectMany((x, i) =>
            {
                var freqLength = x.Frequency.Length;
                var timestamp = firstTimestamp.AddSeconds(intervalSeconds * i);
                return x.PSD.Select((y, j) => PointData.Builder
                .Measurement($"channel_{channel}")
                .Field("psd", value: y)
                .Tag("frequency", (j < freqLength ? x.Frequency[j] : j).ToString()) // if frequency as tag is more appropriate
                .Timestamp(timestamp, WritePrecision.Ns)
                .ToPointData());
            }).Concat(fftData.SelectMany((x, i) =>
            {
                var timestamp = firstTimestamp.AddSeconds(intervalSeconds * i);
                return x.Spectrum.Select((y, j) => PointData.Builder
                .Measurement($"channel_{channel}_spectrum")
                .Field("real", value: y.Real)
                .Field("imaginary", value: y.Imaginary)
                .Field("magnitude", y.Magnitude)
                .Field("phase", y.Phase)
                .Timestamp(timestamp, WritePrecision.Ns)
                .ToPointData());
            }));
            WriteApiAsync writeApi = _client.GetWriteApiAsync();
            IncrementInterlocks(await writeApi.WritePointsAsyncWithIRestResponse(data));
            await ProcessChunksAsync(fftData.Where(x => x.WithHanning != null).Select(x => x.WithHanning!) ?? Enumerable.Empty<FFTAnalysis>(), channel);
        }


        private void IncrementInterlocks(RestResponse[] result)
        {
            foreach (var x in result)
            {
                if (x.IsSuccessStatusCode)
                {
                    Interlocked.Increment(ref Written);
                }
                else
                {
                    Interlocked.Increment(ref Dropped);
                    System.IO.File.AppendAllLines("influxdb_errors.log", new[] { DateTime.Now.ToString(), x.ErrorMessage!, x.Content! });
                }
            }
        }

        public void Log(EEGReading reading)
        {
            if (_queue.Count >= MAX_QUEUE_SIZE)
            {
                Interlocked.Increment(ref Dropped);
                return;
            }
            _queue.Add(reading);
        }

        public void StopBackgroundTask()
        {
            if (!_running) return;
            _cts.Cancel();
        }

        private sealed class FFTAnalysis
        {
            public required double[] PSD { get; set; }
            public required double[] Frequency { get; set; }
            public required System.Numerics.Complex[] Spectrum { get; set; }
            public FFTAnalysis? WithHanning { get; set; }
        }

        private IEnumerable<FFTAnalysis> DoChunkedFFT(int chunks, int channel, int sampleRate = 257)
        {
            for (int i = 0; i < chunks; i++)
            {
                yield return DoFFT((byte)channel, i, chunks, sampleRate);
            }
        }

        private FFTAnalysis DoFFT(byte channel, int chunkIndex, int totalChunks, int sampleRate = 257)
        {
            var chunkSize = _queue.Count / totalChunks;
            var chunk = _queue.Skip(chunkIndex * chunkSize).Take(Math.Min(chunkSize, _queue.Count - chunkIndex * chunkSize));
            double[] signal = chunk.Where(x => x.Channel == channel).Select(r => (double)(r.Value ?? 0)).ToArray();
            var closestPowerOfTwo = Math.Pow(2, Math.Ceiling(Math.Log(signal.Length, 2)));
            signal = signal.Concat(Enumerable.Repeat(0.0, (int)closestPowerOfTwo - signal.Length)).ToArray();
            System.Numerics.Complex[] spectrum = FftSharp.FFT.Forward(signal);
            double[] psd = FftSharp.FFT.Power(spectrum);
            double[] freq = FftSharp.FFT.FrequencyScale(psd.Length, sampleRate);
            var window = new FftSharp.Windows.Hanning();
            double[] windowed = window.Apply(signal);
            System.Numerics.Complex[] hSpectrum = FftSharp.FFT.Forward(windowed);
            double[] hpsd = FftSharp.FFT.Power(hSpectrum);
            double[] hfreq = FftSharp.FFT.FrequencyScale(hpsd.Length, sampleRate);
            return new FFTAnalysis()
            {
                PSD = psd,
                Frequency = freq,
                Spectrum = spectrum,
                WithHanning = new FFTAnalysis()
                {
                    PSD = hpsd,
                    Frequency = hfreq,
                    Spectrum = hSpectrum
                }
            };
        }

        public void Dispose()
        {
            StopBackgroundTask();
            _client.Dispose();
        }
    }
}
