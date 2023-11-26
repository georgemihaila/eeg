using System;

namespace EEG
{
    /// <summary>
    /// Represents an EEG channel reading.
    /// </summary>
    public sealed class EEGReading
    {
        public byte? Channel { get; set; }

        public UInt16? Value { get; set; }

        public DateTimeOffset Timestamp { get; internal set; }

        public void Clear()
        {
            Channel = null;
            Value = null;
        }

        public EEGReading Clone()
        {
            return new EEGReading()
            {
                Channel = Channel,
                Value = Value,
                Timestamp = Timestamp
            };
        }
    }
}
