namespace AgentDesktop.Core.Adapter;

/// <summary>An owned platform accessibility payload. The payload is destroyed exactly once when the handle is disposed. It is thread-affine: a thread-bound platform object must not cross threads through the platform-neutral API, so callers keep a handle on the thread that made it.</summary>
public sealed class NativeHandle : IDisposable
{
    private object? _payload;

    private NativeHandle(object? payload) => _payload = payload;

    /// <summary>Owns a platform-specific payload until this handle is disposed.</summary>
    public static NativeHandle New<T>(T payload) => new(payload);

    /// <summary>An empty handle for adapters and tests without a native payload.</summary>
    public static NativeHandle Null() => new(null);

    public bool IsNull() => _payload is null;

    /// <summary>Borrows the payload when it has the requested platform-specific type.</summary>
    public T? Downcast<T>() where T : class => _payload as T;

    public void Dispose()
    {
        if (_payload is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _payload = null;
    }
}
