namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// A model store operation failed. The message says why, for the user.
/// </summary>
public sealed class ModelStoreException : Exception
{
    public ModelStoreException()
    {
    }

    public ModelStoreException(string message)
        : base(message)
    {
    }

    public ModelStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
