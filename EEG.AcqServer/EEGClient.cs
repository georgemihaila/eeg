using System;
using System.Diagnostics;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Exceptions;
using RJCP.IO.Ports;
using File = System.IO.File;

namespace EEG
{
    public sealed class EEGClient : IDisposable
    {
        #region DRIVER
        const bool USE_DRIVER = false;
        private const string DRIVER_FILENAME = "/dev/ttyEEG0";
        private static StreamWriter? _driverWriter;
        private bool _cancellationRequested = false;
        private static void CreateDev(bool recursed = false)
        {
            if (!USE_DRIVER || Environment.OSVersion.Platform != PlatformID.Unix) return;
            if (File.Exists(DRIVER_FILENAME))
            {
                FileInfo info = new(DRIVER_FILENAME);
                if (!info.UnixFileMode.HasFlag(UnixFileMode.UserWrite))
                {
                    Process.Start("/bin/bash", "sudo chmod 666 /dev/ttyEEG0").WaitForExit();
                }
                _driverWriter = new StreamWriter(DRIVER_FILENAME, false);
                return;
            }
            else if (!recursed)
            {
                Process.Start("/bin/bash", "sudo mknod -m 666 /dev/ttyEEG0 c 1 3").WaitForExit();
                CreateDev(true);
            }
        }

        private static void UninstallDev()
        {
            if (!USE_DRIVER || Environment.OSVersion.Platform != PlatformID.Unix) return;

            File.Delete(DRIVER_FILENAME);
            _driverWriter?.Close();
        }

        private static void DevWrite(string data)
        {
            if (!USE_DRIVER || Environment.OSVersion.Platform != PlatformID.Unix) return;

            _driverWriter?.WriteLine(data);
        }

        #endregion
        private SerialPortStream port;
        public event EventHandler<EEGReading>? OnChannelValueReceived;
        public event EventHandler<Packet6>? OnPacketReceived;
        public UInt32 TotalBytesRead { get; private set; } = 0;
        public UInt32 TotalMalformedPackets { get; private set; } = 0;
        public UInt32 TotalValidPackets { get; private set; } = 0;
        public string PortName { get; private set; }
        public Buffer6 LastPackets { get; private set; } = new(10);
        public EEGReading[] LastReadings { get; private set; } = new EEGReading[2];
        private readonly byte[] _buffer = new byte[9];
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private DateTimeOffset _startupTimestamp = new(DateTime.Now);

        public EEGClient(string portName, int baudRate)
        {
            port = new SerialPortStream(portName, baudRate, 8, Parity.None, StopBits.One);
            PortName = portName;
            for (int i = 0; i < _buffer.Length; i++)
            {
                _buffer[i] = 0x01;
            }
            for (int i = 0; i < LastReadings.Length; i++)
            {
                LastReadings[i] = new EEGReading();
            }
        }

        public void Start()
        {
            try
            {
                port.Open();
                CreateDev();
            }
            catch (UnauthorizedAccessException)
            {
                System.Console.WriteLine("Failed to open port.");
                Console.WriteLine("Either the port is already open or you don't have permission to open it.\r\nTry running this program as root or do\r\n`sudo chown $(whoami)) /dev/ttyUSBN`");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open port: {ex.Message}");
                UninstallDev();
                return;
            }

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _cancellationRequested = true;
                UninstallDev();
                port.Dispose();
                Console.WriteLine($"{PortName} closed.");
                e.Cancel = true;
            };
            ProcessAsync();
        }

        private void BufferSHL()
        {
            for (int i = 0; i < _buffer.Length - 1; i++)
            {
                _buffer[i] = _buffer[i + 1];
            }
        }

        private async void ProcessAsync()
        {
            while (!_cancellationRequested && port.IsOpen)
            {
                if (port.BytesToRead > 0)
                {
                    _buffer[_buffer.Length - 1] = (byte)port.ReadByte();
                    if (_buffer[0] == 0xA5 && _buffer[2] == 0x02 && _buffer[1] == 0x5A)
                    {
                        ProcessBuffer();
                        await Task.Delay(TimeSpan.FromMicroseconds(1));
                    }
                    BufferSHL();
                    TotalBytesRead++;
                }
            }
        }

        private long _dateHack = 0;

        private void ProcessBuffer()
        {
            LastPackets.AddFrom(_buffer);
            if (!LastPackets.Last().Malformed)
            {
                TotalValidPackets++;
                var packet = LastPackets.Last();
                for (byte i = 0; i < LastReadings.Length; i++)
                {
                    LastReadings[i].Channel = (byte)i;
                    LastReadings[i].Value = packet.Data[i];
                    LastReadings[i].Timestamp = _startupTimestamp.Add(new TimeSpan(_stopwatch.Elapsed.Ticks + (++_dateHack % InfluxLogger.MAX_QUEUE_SIZE))); // We need to add a tick to avoid duplicate timestamps; no idea why this happens
                    // 5000 ticks are way above the resolution of the EEG device so it won't add noticeable time noise
                    OnChannelValueReceived?.Invoke(this, LastReadings[i].Clone()); // Since we're reusing the same object, we need to clone it before forwarding it to the event handler; otherwise, the event handler will receive the same object(s) every time
                }
                OnPacketReceived?.Invoke(this, packet);
            }
            else
            {
                TotalMalformedPackets++;
            }
        }

        public void Dispose()
        {
            _driverWriter?.Dispose();
        }
    }
}