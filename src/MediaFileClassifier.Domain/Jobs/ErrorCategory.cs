namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// What kind of failure stopped a work item (E05-S04-T01). It decides whether the work is retried automatically.
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    /// A passing problem, e.g. an endpoint that's unreachable, a timeout, a rate limit or a file that's locked. Retried
    /// automatically with backoff.
    /// </summary>
    Transient,

    /// <summary>
    /// The input itself is at fault, e.g. a damaged file, unsupported content or a model that rejects the input. Not
    /// retried automatically; the user can retry it.
    /// </summary>
    Permanent,

    /// <summary>
    /// Something isn't set up, e.g. a provider that isn't configured. Not retried automatically. A model that isn't
    /// installed never gets here: its jobs wait instead (D037).
    /// </summary>
    Configuration,
}
