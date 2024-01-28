using System;
using System.Collections;

namespace EEG
{
    public sealed class Buffer6 : IEnumerable<Packet6>
    {
        private readonly Packet6[] _buffer;
        public int Size { get; private set; }

        public Buffer6(int size)
        {
            if (size < 1) throw new ArgumentException("Size must be greater than 0.");
            Size = size;

            _buffer = new Packet6[Size];
            for (int i = 0; i < Size; i++)
            {
                _buffer[i] = new Packet6();
            }
        }

        public IEnumerator<Packet6> GetEnumerator()
        {
            return ((IEnumerable<Packet6>)_buffer).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _buffer.GetEnumerator();
        }
        private void BufferSHL()
        {
            for (int i = 0; i < _buffer.Length - 1; i++)
            {
                _buffer[i] = _buffer[i + 1];
            }
        }

        public void AddFrom(byte[] data)
        {
            BufferSHL();
            _buffer[Size - 1].ResetFrom(data);
        }

        public void Add(Packet6 packet)
        {
            BufferSHL();
            _buffer[Size - 1] = packet;
        }

        public void Clear()
        {
            for (int i = 0; i < Size; i++)
            {
                _buffer[i].Clear();
            }
        }
    }
}
