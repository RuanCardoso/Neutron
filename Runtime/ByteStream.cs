/*===========================================================
    Author: Ruan Cardoso
    -
    Country: Brazil(Brasil)
    -
    Contact: cardoso.ruan050322@gmail.com
    -
    Support: neutron050322@gmail.com
    -
    Unity Minor Version: 2021.3 LTS
    -
    License: Open Source (MIT)
    ===========================================================*/
using System;

namespace Neutron.Core
{
    public sealed class ByteStream
    {
        internal bool isRawBytes;
        private readonly byte[] buffer;
        private int position;
        private int bytesWritten;
        private DateTime lastWriteTime;

        public byte[] Buffer => buffer;
        public int BytesWritten => bytesWritten;
        public int BytesRemaining => bytesWritten - position;
        public int Position { get => position; set => position = value; }
        internal DateTime LastWriteTime => lastWriteTime;

        public ByteStream(int size) => buffer = new byte[size];
        public void Write(byte value)
        {
            if (ThrowIfNotEnoughSpace(sizeof(byte)))
            {
                buffer[position++] = value;
                bytesWritten += sizeof(byte);
            }
        }

        internal void WritePacket(MessageType value)
        {
            if (position != 0 || bytesWritten != 0)
                Logger.PrintError($"The ByteStream is not empty -> Position: {position} | BytesWritten: {bytesWritten}");
            else
                Write((byte)value);
        }

        public void Write(int value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
            Write((byte)(value >> 16));
            Write((byte)(value >> 24));
        }

        public void Write(uint value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
            Write((byte)(value >> 16));
            Write((byte)(value >> 24));
        }

        public void Write(short value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
        }

        public void Write(ushort value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
        }

        public void Write(long value)
        {
            Write((byte)value);
            Write((byte)(value >> 8));
            Write((byte)(value >> 16));
            Write((byte)(value >> 24));
            Write((byte)(value >> 32));
            Write((byte)(value >> 40));
            Write((byte)(value >> 48));
            Write((byte)(value >> 56));
        }

        public void Write(Span<byte> value)
        {
            for (int i = 0; i < value.Length; i++)
                Write(value[i]);
        }

        public void Write(byte[] value, int offset, int size)
        {
            int available = size - offset;
            for (int i = 0; i < available; i++)
                Write(value[offset + i]);
        }

        public void Write(ByteStream value)
        {
            Write(value.buffer, 0, value.bytesWritten);
        }

        public void Write(ByteStream value, int offset, int size)
        {
            Write(value.buffer, offset, size);
        }

        internal void SetLastWriteTime() => lastWriteTime = DateTime.UtcNow;
        public void EndWrite()
        {
            isRawBytes = false;
            position = 0;
            bytesWritten = 0;
        }

        public byte ReadByte()
        {
            if (ThrowIfNotEnoughData(sizeof(byte)))
                return buffer[position++];
            else return 0;
        }

        internal MessageType ReadPacket()
        {
            return (MessageType)ReadByte();
        }

        public int ReadInt()
        {
            int value = ReadByte();
            value |= ReadByte() << 8;
            value |= ReadByte() << 16;
            value |= ReadByte() << 24;
            return value;
        }

        public uint ReadUInt()
        {
            uint value = ReadByte();
            value |= (uint)ReadByte() << 8;
            value |= (uint)ReadByte() << 16;
            value |= (uint)ReadByte() << 24;
            return value;
        }

        public short ReadShort()
        {
            short value = ReadByte();
            value |= (short)(ReadByte() << 8);
            return value;
        }

        public ushort ReadUShort()
        {
            ushort value = ReadByte();
            value |= (ushort)(ReadByte() << 8);
            return value;
        }

        public long ReadLong()
        {
            long value = ReadByte();
            value |= (long)ReadByte() << 8;
            value |= (long)ReadByte() << 16;
            value |= (long)ReadByte() << 24;
            value |= (long)ReadByte() << 32;
            value |= (long)ReadByte() << 40;
            value |= (long)ReadByte() << 48;
            value |= (long)ReadByte() << 56;
            return value;
        }

        public void Read(byte[] value, int offset, int size)
        {
            int available = size - offset;
            for (int i = 0; i < available; i++)
                value[offset + i] = ReadByte();
        }

        private bool ThrowIfNotEnoughSpace(int size)
        {
            if (position + size > buffer.Length)
            {
                Logger.PrintError($"Byte Stream: Not enough space to write!");
                return false;
            }
            return true;
        }

        private bool ThrowIfNotEnoughData(int size)
        {
            if (position + size > bytesWritten)
            {
                Logger.PrintError($"Byte Stream: Not enough data to read!");
                return false;
            }
            return true;
        }

        static ByteStreamPool byteStreams = new(0);
        public static ByteStream Get()
        {
            ByteStream _get_ = byteStreams.Get();
            _get_.isRelease = false;
            if (_get_.position != 0 || _get_.bytesWritten != 0)
                Logger.PrintError($"The ByteStream is not empty -> Position: {_get_.position} | BytesWritten: {_get_.bytesWritten}. Maybe you are modifying a ByteStream that is being used by another thread? or are you using a ByteStream that has already been released?");
            return _get_;
        }

        internal bool isRelease = false;
        internal void Release()
        {
            if (isRelease) Logger.PrintError($"The ByteStream is already released!");
            else
            {
                isRelease = true;
                byteStreams.Release(this);
            }
        }
    }
}