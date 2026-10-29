namespace MediaFileClassifier.Ui.Commands;

/// <summary>
/// Registers the commands of one feature (E13-S05): the Epic that owns a command registers it.
/// Add an implementation with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ICommandContributor, MyCommands&gt;())</c>.
/// </summary>
public interface ICommandContributor
{
    /// <summary>
    /// Adds the feature's commands.
    /// </summary>
    /// <param name="registry">The registry to add them to.</param>
    void Register(CommandRegistry registry);
}
