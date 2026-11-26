namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// What one <see cref="IDuplicateGroupRepository.ReconcileAsync"/> call did.
/// </summary>
/// <param name="Hashes">The content hashes it took from the queue; 0 when the queue was empty.</param>
/// <param name="Created">Groups created.</param>
/// <param name="Changed">Groups whose members changed, the reopened ones included.</param>
/// <param name="Reopened">Resolved or Ignored groups that a new member opened again.</param>
/// <param name="Deleted">Groups deleted because fewer than two members were left.</param>
public sealed record DuplicateReconcileResult(int Hashes, int Created, int Changed, int Reopened, int Deleted)
{
    /// <summary>
    /// Gets the result of a call that found the queue empty.
    /// </summary>
    public static DuplicateReconcileResult Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>
    /// Gets a value indicating whether any group was created, changed or deleted.
    /// </summary>
    public bool ChangedGroups => Created + Changed + Deleted > 0;
}
