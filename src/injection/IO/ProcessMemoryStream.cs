namespace Vezel.Ruptura.Injection.IO;

internal sealed unsafe class ProcessMemoryStream : Stream
{
    // TODO: Review some of the casts here.

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => true;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            Check.Range(value >= 0, value);

            _position = (nint)value;
        }
    }

    private readonly ProcessObject _process;

    private readonly void* _address;

    private readonly nint _length;

    private nint _position;

    private bool _wrote;

    public ProcessMemoryStream(ProcessObject process, nuint address, nint length)
    {
        _process = process;
        _address = (void*)address;
        _length = length;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var off = (nint)offset;

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

        return _position;
    }

    public override void Flush()
    {
        if (_wrote)
            _process.FlushInstructionCache(_address, _length);
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
        byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        ValidateBufferArguments(buffer, offset, count);

        return ReadAsync(buffer.AsMemory(offset..count), cancellationToken).AsTask();
    }

    public override int Read(Span<byte> buffer)
    {
        var len = (int)nint.Min(_length - _position, buffer.Length);

        if (len <= 0)
            return 0;

        try
        {
            fixed (byte* p = buffer)
                _process.ReadMemory((byte*)_address + (nuint)_position, p, len);
        }
        catch (Win32Exception ex)
        {
            throw new IOException(null, ex);
        }

        _position += len;

        return len;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override int ReadByte()
    {
        var span = (stackalloc byte[1]);

        return Read(span) == 1 ? span[0] : -1;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        Write(buffer.AsSpan(offset..count));
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        ValidateBufferArguments(buffer, offset, count);

        return WriteAsync(buffer.AsMemory(offset..count), cancellationToken).AsTask();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Check.OperationSupported(_position + buffer.Length <= _length);

        _wrote = true;

        try
        {
            fixed (byte* p = buffer)
                _process.WriteMemory((byte*)_address + (nuint)_position, p, buffer.Length);
        }
        catch (Win32Exception ex)
        {
            throw new IOException(null, ex);
        }

        _position += buffer.Length;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);

        return default;
    }

    public override void WriteByte(byte value)
    {
        Write([value]);
    }
}
