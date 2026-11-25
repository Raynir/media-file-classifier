namespace MediaFileClassifier.UseCases.Import;

/// <summary>
/// Keeps a rescan's reconcile apart from the work that registers files, so the two never insert the same folder and
/// path (MFC-1040). Registering (an import's discovery, a selected-files import) takes the gate shared, so imports of
/// different folders still run side by side; a reconcile takes it alone, and waits for the registrations to finish.
/// </summary>
/// <remarks>
/// A reconcile that's waiting goes first: registrations that ask after it wait for it, so it isn't starved. It isn't
/// disposable: a hold let go while the app shuts down must still release it.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "Lives as long as the app; a hold let go while it shuts down must still release it.")]
public sealed class FolderScanGate
{
    // Taken by both kinds while they queue up, so a waiting reconcile holds new registrations back.
    private readonly SemaphoreSlim turnstile = new(1, 1);

    // Free when nothing holds the gate.
    private readonly SemaphoreSlim empty = new(1, 1);

    // Guards the count of shared holders.
    private readonly SemaphoreSlim counter = new(1, 1);

    private int shared;

    /// <summary>
    /// Waits until no reconcile runs, and holds the gate shared until the result is disposed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hold; dispose it to release the gate.</returns>
    public async Task<IDisposable> EnterSharedAsync(CancellationToken cancellationToken)
    {
        await turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        turnstile.Release();

        await counter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (shared == 0)
            {
                await empty.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            shared++;
        }
        finally
        {
            counter.Release();
        }

        return new Hold(ExitShared);
    }

    /// <summary>
    /// Waits until nothing else holds the gate, and holds it alone until the result is disposed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hold; dispose it to release the gate.</returns>
    public async Task<IDisposable> EnterExclusiveAsync(CancellationToken cancellationToken)
    {
        await turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await empty.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            turnstile.Release();
            throw;
        }

        return new Hold(() =>
        {
            empty.Release();
            turnstile.Release();
        });
    }

    private void ExitShared()
    {
        // Never cancelled: a hold that's let go must always release the gate.
        counter.Wait();
        try
        {
            if (--shared == 0)
            {
                empty.Release();
            }
        }
        finally
        {
            counter.Release();
        }
    }

    private sealed class Hold(Action release) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                release();
            }
        }
    }
}
