﻿
using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace EventStore.Core.TransactionLog.Unbuffered
{
    public class UnbufferedIOFileStream : Stream
    {
        private readonly byte[] _buffer;
        private readonly int _blockSize;
        private int _bufferedCount;
        private bool _aligned;
        private long _lastPosition;
        private bool _needsFlush;
        private readonly SafeFileHandle _handle;
        
        private UnbufferedIOFileStream(SafeFileHandle handle, int blockSize, int internalBufferSize)
        {
            _handle = handle;
            _buffer = new byte[internalBufferSize];
            _blockSize = blockSize;
        }

        public static UnbufferedIOFileStream Create(string path,
            FileMode mode,
            FileAccess acc,
            FileShare share,
            bool sequential,
            int internalBufferSize,
            bool writeThrough,
            uint minBlockSize)
        {
            var blockSize = NativeFile.GetDriveSectorSize(path);
            blockSize = blockSize > minBlockSize ? blockSize : minBlockSize;
            if (internalBufferSize%blockSize != 0)
                throw new Exception("buffer size must be aligned to block size of " + blockSize + " bytes");
            var flags = ExtendedFileOptions.NoBuffering;
            if (writeThrough) flags = flags | ExtendedFileOptions.WriteThrough;

            var handle = NativeFile.Create(path, acc, share, mode, (int) flags);
            return new UnbufferedIOFileStream(handle, (int) blockSize, internalBufferSize);
        }

        public override void Flush()
        {
            if (!_needsFlush) return;
            var alignedbuffer = (int) GetLowestAlignment(_bufferedCount);
            var positionAligned = GetLowestAlignment(_lastPosition);
            if (!_aligned)
            {
                SeekInternal(positionAligned);
            }
            if (_bufferedCount == alignedbuffer)
            {
                InternalWrite(_buffer, (uint) _bufferedCount);
                _lastPosition = positionAligned + _bufferedCount;
                _bufferedCount = 0;
                _aligned = true;
            }
            else
            {
                var left = _bufferedCount - alignedbuffer;

                InternalWrite(_buffer, (uint) (alignedbuffer + _blockSize));
                _lastPosition = positionAligned + alignedbuffer + left;
                SetBuffer(alignedbuffer, left);
                _bufferedCount = left;
                _aligned = false;
            }
            _needsFlush = false;
        }

        private void SeekInternal(long positionAligned)
        {
            NativeFile.Seek(_handle, (int) positionAligned, SeekOrigin.Begin);
        }

        private void InternalWrite(byte[] buffer, uint count)
        {
            var written = 0;
            NativeFile.Write(_handle, buffer, count, ref written);
            //TODO check written
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Console.WriteLine("seeking to " + offset);
            if(origin != SeekOrigin.Begin) throw new NotImplementedException("only supports seek origin begin");
            var aligned = GetLowestAlignment(offset);
            var left = (int) (offset - aligned);
            Flush();
            _bufferedCount = left;
            _aligned = aligned == left; //ALIGNED IS WRONG
            _lastPosition = offset;
            return offset;
        }

        private long GetLowestAlignment(long offset)
        {
            return offset - (offset%_blockSize);
        }

        public override void SetLength(long value)
        {
            var aligned = GetLowestAlignment(value);
            aligned = aligned == value ? aligned : aligned + _blockSize;
            NativeFile.SetFileSize(_handle, aligned);
            Seek(0, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if(offset < 0 || buffer.Length < offset) throw new ArgumentException("offset");
            if (count < 0 || buffer.Length < count) throw new ArgumentException("offset");
            if(offset + count > buffer.Length) throw new ArgumentException("offset + count must be less than size of array");
            var position = (int)GetLowestAlignment(Position);
            var roffset = (int) (Position - position);
            var toread = (((roffset + count) / _blockSize) + 1) * _blockSize;
                        
            var readbuffer = new byte[toread];
            SeekInternal(position);
            var read = NativeFile.Read(_handle, readbuffer, 0, toread);
            Buffer.BlockCopy(readbuffer, roffset, buffer,offset,count);
            _bufferedCount += count;
            return count;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var done = false;
            var left = count;
            var current = offset;
            while (!done)
            {
                _needsFlush = true;
                if (_bufferedCount + left < _buffer.Length)
                {
                    CopyBuffer(buffer, current, left);
                    done = true;
                    current += left;
                }
                else
                {
                    var toFill = _buffer.Length - _bufferedCount;
                    CopyBuffer(buffer, current, toFill);
                    Flush();
                    left -= toFill;
                    current += toFill;
                    done = left == 0;
                }
            }
        }

        private void CopyBuffer(byte[] buffer, int offset, int count)
        {
            Buffer.BlockCopy(buffer, offset, _buffer, _bufferedCount, count);
            _bufferedCount += count;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { return NativeFile.GetFileSize(_handle); }
        }

        public override long Position
        {
            get
            {
                if (_aligned)
                    return _lastPosition + _bufferedCount;
                return GetLowestAlignment(_lastPosition) + _bufferedCount;
            }
            set { Seek(value, SeekOrigin.Begin); }
        }

        private void SetBuffer(int alignedbuffer, int left)
        {
            Buffer.BlockCopy(_buffer, alignedbuffer, _buffer, 0, left);
        }

        protected override void Dispose(bool disposing)
        {
            Flush();
            _handle.Close();
        }
    }
}