using System.Buffers;

namespace BoringAvatarsSharp.Tests;

internal sealed class RotatingBufferWriter : IBufferWriter<byte>
{
    private readonly ArrayBufferWriter<byte> _output = new();
    private byte[] _current = [];

    public ReadOnlySpan<byte> WrittenSpan => _output.WrittenSpan;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        _current = new byte[Math.Max(sizeHint, 1)];
        return _current;
    }

    public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _current.Length);
        _output.Write(_current.AsSpan(0, count));
        // Poison returned memory to detect use of a span after Advance.
        _current.AsSpan().Fill(0xFF);
        _current = [];
    }
}

internal sealed class FailingBufferWriter : IBufferWriter<byte>
{
    public Memory<byte> GetMemory(int sizeHint = 0) => throw new IOException("Destination failed.");
    public Span<byte> GetSpan(int sizeHint = 0) => throw new IOException("Destination failed.");
    public void Advance(int count) => throw new IOException("Destination failed.");
}
