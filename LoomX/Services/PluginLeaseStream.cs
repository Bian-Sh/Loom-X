namespace LoomX.Services;

/// <summary>在完成外层恢复后才释放代际；零字节读取不是 EOF。</summary>
internal sealed class PluginLeaseStream(Stream inner, IAsyncDisposable lease) : Stream
{
    private int disposed;
    private int released;
    public override bool CanRead => inner.CanRead && Volatile.Read(ref disposed) == 0;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        try
        {
            var read = inner.Read(buffer, offset, count);
            if (read == 0 && count > 0) Release();
            return read;
        }
        catch { Dispose(); throw; }
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        try
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0 && buffer.Length > 0) await ReleaseAsync().ConfigureAwait(false);
            return read;
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    private ValueTask ReleaseAsync() => Interlocked.Exchange(ref released, 1) == 0 ? lease.DisposeAsync() : ValueTask.CompletedTask;
    private void Release() => Task.Run(async () => await ReleaseAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0)
        {
            try { inner.Dispose(); }
            finally { Release(); }
        }
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { await inner.DisposeAsync().ConfigureAwait(false); }
        finally { await ReleaseAsync().ConfigureAwait(false); }
        GC.SuppressFinalize(this);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}