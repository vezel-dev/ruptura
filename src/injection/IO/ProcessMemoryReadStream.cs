namespace Vezel.Ruptura.Injection.IO;

sealed class ProcessMemoryReadStream : Stream
{
    // TODO: Review some of the casts here.

    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override bool CanSeek => true;

    public override long Length => (nint)_length;

    public override long Position
    {
        get => (nint)_position;
        set
        {
            _ = value >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(value));

            _position = (nuint)value;
        }
    }

    readonly TargetProcess _process;

    readonly nuint _address;

    readonly nuint _length;

    nuint _position;

    public ProcessMemoryReadStream(TargetProcess process, nuint address, nuint length)
    {
        _process = process;
        _address = address;
        _length = length;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var off = (nuint)offset;

        switch (origin)
        {
            case SeekOrigin.Begin:
                if (off < 0)
                    throw new IOException();

                _position = off;
                break;
            case SeekOrigin.Current:
                if (_position + off < 0)
                    throw new IOException();

                _position += off;
                break;
            case SeekOrigin.End:
                if (_length + off < 0)
                    throw new IOException();

                _position = _length + off;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }

        return (nint)_position;
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        return Read(buffer.AsSpan(offset..count));
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken = default)
    {
        ValidateBufferArguments(buffer, offset, count);

        return ReadAsync(buffer.AsMemory(offset..count), cancellationToken).AsTask();
    }

    public override int Read(Span<byte> buffer)
    {
        var len = (int)nuint.Min(_length - _position, (uint)buffer.Length);

        if (len <= 0)
            return 0;

        try
        {
            _process.ReadMemory(_address + _position, buffer[..len]);
        }
        catch (Win32Exception ex)
        {
            throw new IOException(null, ex);
        }

        _position += (uint)len;

        return len;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override unsafe int ReadByte()
    {
        byte value;

        return Read(new Span<byte>(&value, 1)) == 1 ? value : -1;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        throw new NotSupportedException();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public override void WriteByte(byte value)
    {
        throw new NotSupportedException();
    }
}
