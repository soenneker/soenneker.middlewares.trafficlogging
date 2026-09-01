using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Middlewares.TrafficLogging;

internal sealed class PrefixCaptureStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _captured;
    private readonly object _lock = new();
    private int _capturedLength;
    private long _totalBytesWritten;

    public PrefixCaptureStream(Stream inner, int captureLimit)
    {
        _inner = inner;
        _captured = new byte[captureLimit];
    }

    public ReadOnlySpan<byte> Captured => _captured.AsSpan(0, _capturedLength);
    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Interlocked.Add(ref _totalBytesWritten, count);
        Capture(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        Interlocked.Add(ref _totalBytesWritten, buffer.Length);
        Capture(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).NoSync();
        Interlocked.Add(ref _totalBytesWritten, buffer.Length);
        Capture(buffer.Span);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer, offset, count, cancellationToken).NoSync();
        Interlocked.Add(ref _totalBytesWritten, count);
        Capture(buffer.AsSpan(offset, count));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CryptographicOperations.ZeroMemory(_captured);

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void Capture(ReadOnlySpan<byte> source)
    {
        lock (_lock)
        {
            int remaining = _captured.Length - _capturedLength;
            if (remaining <= 0)
                return;

            int count = Math.Min(remaining, source.Length);
            source[..count].CopyTo(_captured.AsSpan(_capturedLength));
            _capturedLength += count;
        }
    }
}
