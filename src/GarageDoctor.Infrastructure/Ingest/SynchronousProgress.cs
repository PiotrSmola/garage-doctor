namespace GarageDoctor.Infrastructure.Ingest;

public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Action<T> _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public void Report(T value) => _handler(value);
}
