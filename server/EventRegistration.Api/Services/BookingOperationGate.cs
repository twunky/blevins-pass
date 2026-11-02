namespace EventRegistration.Api.Services;

/// <summary>
/// Single-process coordination between the development demo reset and normal booking requests.
/// Many booking requests may run together; the reset waits for them to finish and blocks new ones
/// until it is done. This is a convenience for the one-process local demo only. Database guarded
/// updates and unique indexes remain the real concurrency protection; this gate does not protect
/// several API processes sharing one database.
/// </summary>
public sealed class BookingOperationGate
{
    private readonly SemaphoreSlim resetLock = new(1, 1);
    private int activeBookingOperations;

    /// <summary>Entered by booking, checkout, confirmation and availability (cleanup) requests.</summary>
    public async Task<IDisposable> EnterBookingOperationAsync(CancellationToken cancellationToken = default)
    {
        // Waiting on the reset lock makes new requests queue behind a running reset.
        await resetLock.WaitAsync(cancellationToken);
        Interlocked.Increment(ref activeBookingOperations);
        resetLock.Release();
        return new Releaser(() => Interlocked.Decrement(ref activeBookingOperations));
    }

    /// <summary>Exclusive: blocks new booking operations and waits for running ones to drain.</summary>
    public async Task<IDisposable> EnterResetAsync(CancellationToken cancellationToken = default)
    {
        await resetLock.WaitAsync(cancellationToken);
        try
        {
            while (Volatile.Read(ref activeBookingOperations) > 0)
            {
                await Task.Delay(25, cancellationToken);
            }
        }
        catch
        {
            resetLock.Release();
            throw;
        }
        return new Releaser(() => resetLock.Release());
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                release();
            }
        }
    }
}
