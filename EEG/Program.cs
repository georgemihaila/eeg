
using System.Diagnostics;
using System.Text;
using InfluxDB.Client;
using Konsole;
using RJCP.IO.Ports;

namespace EEG
{

  public class Program
  {
    #region InfluxDB

    const string bucket = "EEG";
    const string org = "CCE";
    const string token = "9_23SxQUTuSrHFh14TVMiVV66wKxrZXDWb4srsHBsZF0mf456e2eXeohYUBku9JFT-4gmKbEZggAlLymO1eKuw==";

    #endregion
    private static readonly Stopwatch PerformanceStopwatch = new();
    private static readonly Stopwatch GlobalStopwatch = Stopwatch.StartNew();
    private static UInt32 MeasurementBytesRead = 0;
    private static readonly EEGClient _EEGClient = new("/dev/ttyUSB1", 57600);
    private static readonly IConsole MainWindow = Window.OpenBox($"Olimex EEG console");
    private static readonly IConsole TopWindow = MainWindow.SplitTop($"{_EEGClient.PortName}");
    private static readonly IConsole BottomWindow = MainWindow.SplitBottom($"Vizualization");
    private static readonly IConsole FFTWindow = BottomWindow.SplitRight($"FFT");
    private static readonly IConsole BarsWindow = BottomWindow.SplitLeft($"Amplitude");
    static InfluxLogger? _influxLogger;
    private static long _startupTime;
    public static void Main(string[] args)
    {
      Thread.Sleep(1000);
      _startupTime = DateTime.Now.Ticks * 100;
      Console.CursorVisible = false;
      for (int i = 0; i < 2; i++)
      {
        _progressBars[i] = new(BarsWindow, 1023);
        _progressBars[i].Refresh(0, $"CH{i}");
      }
      _progressBars[2] = new(TopWindow, InfluxLogger.MAX_QUEUE_SIZE);

      using InfluxDBClient _client = new(new InfluxDBClientOptions("http://influx-dev-1.cce.sh:8086")
      {
        Token = token,
        Bucket = bucket,
        Org = org
      });
      _influxLogger = new(_client);

      _EEGClient.OnChannelValueReceived += EEGClient_OnChannelValueReceived;
      var builder = new StringBuilder();
      /*
      int pkc = 0;
      _EEGClient.OnPacketReceived += (s, e) =>
      {
        pkc++;
        if (pkc >= 1024) // ~ 4 seconds
        {
          pkc = 0;
          File.AppendAllLines($"dump.eer", [builder.ToString()]);
          builder.Clear();
        }
        builder.AppendLine($"{_startupTime + GlobalStopwatch.Elapsed.TotalNanoseconds} {string.Join(" ", _EEGClient.LastReadings.Select(r => r.Value ?? 0))}");
      };*/
      _influxLogger.StartBackgroundTask();
      PerformanceStopwatch.Start();
      _EEGClient.Start();
      MainWindow.PrintAt(0, MainWindow.WindowHeight - 1, "Press CTRL+C to exit.");
      var cancellationRequested = false;
      Console.CancelKeyPress += (s, e) =>
      {
        cancellationRequested = true;
      };
      while (!cancellationRequested)
      {
        Thread.Sleep(100);
      }
      Console.CursorVisible = true;
      Console.Clear();
    }
    static UInt32 _lastValidPackets = 0;
    const int _REFRESH_RATE = 250;
    private static void EEGClient_OnChannelValueReceived(object? sender, EEGReading e)
    {
      // Print bitrate every second
      if (PerformanceStopwatch.Elapsed.TotalMilliseconds > _REFRESH_RATE)
      {
        PerformanceStopwatch.Stop();
        var bps = (_EEGClient.TotalBytesRead - MeasurementBytesRead) / PerformanceStopwatch.Elapsed.TotalSeconds;
        TopWindow.CursorTop = 1;
        _progressBars[2].Refresh(_influxLogger?.QueueSize ?? 0, $"Queued ({_influxLogger?.QueueSize}/{InfluxLogger.MAX_QUEUE_SIZE})");
        TopWindow.WriteLine($"Data rate: {bps.ToString("N0")} bytes/s".PadRight(40));
        TopWindow.WriteLine($"Error count: {_EEGClient.TotalMalformedPackets.ToString("N0")}".PadRight(40));
        var packetRate = (_EEGClient.TotalValidPackets - _lastValidPackets) * 1000 / PerformanceStopwatch.Elapsed.TotalMilliseconds;
        _lastValidPackets = _EEGClient.TotalValidPackets;
        TopWindow.WriteLine($"Packet count: {_EEGClient.TotalValidPackets.ToString("N0")}".PadRight(40));
        TopWindow.WriteLine($"Packet rate: {packetRate.ToString("N0")} packets/ch/s".PadRight(40));
        TopWindow.WriteLine($"Total: {(_EEGClient.TotalBytesRead / 1024).ToString("N0")}kB transferred".PadRight(40));
        TopWindow.WriteLine($"InfluxDB writes: {_influxLogger?.Written.ToString("N0")}".PadRight(40));
        TopWindow.WriteLine($"InfluxDB drops: {_influxLogger?.Dropped}".PadRight(40));
        TopWindow.WriteLine(_EEGClient.LastReadings.Last().Timestamp.Ticks.ToString());
        PerformanceStopwatch.Restart();
        MeasurementBytesRead = _EEGClient.TotalBytesRead;
        UpdateBars();
      }
      _influxLogger?.Log(e);
      //File.AppendAllLines($"ch{e.Channel}.raw", [$"{(int)GlobalStopwatch.Elapsed.TotalSeconds}.{GlobalStopwatch.Elapsed.Milliseconds} {e.Value}"]);
    }

    private static readonly ProgressBar[] _progressBars = new ProgressBar[3];

    private static void UpdateBars()
    {
      for (int i = 0; i < 2; i++)
      {
        var reading = _EEGClient.LastReadings[i];
        if (!reading.Channel.HasValue) continue;

        _progressBars[reading.Channel.Value].Refresh(reading.Value ?? 0, $"CH{reading.Channel.Value} {reading.Value}");
      }
    }
  }
}