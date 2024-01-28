using System;
using Konsole;

namespace EEG
{
    /// <summary>
    /// Represents a 6-channel EEG packet.
    /// </summary>
    public sealed class Packet6
    {
        #region Constants
        /// <summary>
        /// Expected sync byte #1.
        /// </summary>
        private const Byte ESYNC0 = 0xA5;
        /// <summary>
        ///  Expected sync byte #2.
        /// </summary>
        private const Byte ESYNC1 = 0x5A;

        #endregion

        #region Packet structure
        public Byte? Sync0 { get; set; }
        public Byte? Sync1 { get; set; }
        public Byte? Version { get; set; }
        public Byte? PacketCounter { get; set; }
        public UInt16?[] Data { get; private set; } = new UInt16?[2];
        public Byte? Switches { get; set; }

        #endregion

        /// <summary>
        /// Gets a value indicating whether the packet is malformed.
        /// </summary>
        public bool Malformed => Sync0 != ESYNC0 || Sync1 != ESYNC1 || Version == null || Version != 0x02 || PacketCounter == null || Data[0] == null || Data[1] == null || Switches == null;

        /// <summary>
        /// Clears the packet.
        /// </summary>
        public void Clear()
        {
            Sync0 = null;
            Sync1 = null;
            Version = null;
            PacketCounter = null;
            Data[0] = null;
            Data[1] = null;
            Switches = null;
        }

        public void ResetFrom(byte[] data)
        {
            var expectedLength = 5 + 2 * Data.Length;
            if (data.Length != expectedLength) throw new ArgumentException($"Data length must be {expectedLength} bytes.");
            Clear();

            Sync0 = data[0];
            Sync1 = data[1];
            Version = data[2];
            PacketCounter = data[3];
            // parse big endian
            Data[0] = (UInt16)(data[4] << 8 | data[5]);
            Data[1] = (UInt16)(data[6] << 8 | data[7]);
            Switches = data[8];
        }
    }
}
