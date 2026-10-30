using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Ui.Commands;

/// <summary>
/// All application commands, by ID (E13-S05-T01). Surfaces bind to a command with
/// <c>{Binding Commands[help.open-log-folder]}</c> on a view model that exposes the registry.
/// Labels and enablement refresh when the language or a capability changes.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, AppCommand> commands = new(StringComparer.Ordinal);
    private readonly Localizer localizer;
    private readonly ICapabilityGate capabilities;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandRegistry"/> class and collects the commands.
    /// </summary>
    /// <param name="contributors">The features that register commands.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="capabilities">Capability flags.</param>
    public CommandRegistry(IEnumerable<ICommandContributor> contributors, Localizer localizer, ICapabilityGate capabilities)
    {
        this.localizer = localizer;
        this.capabilities = capabilities;
        foreach (var contributor in contributors)
        {
            contributor.Register(this);
        }

        localizer.Changed += (_, _) => Refresh();
        capabilities.Changed += (_, _) => Refresh();
    }

    /// <summary>
    /// Gets every command.
    /// </summary>
    public IReadOnlyCollection<AppCommand> All => commands.Values;

    /// <summary>
    /// Gets a command by ID.
    /// </summary>
    /// <param name="id">Command ID.</param>
    /// <returns>The command.</returns>
    /// <exception cref="KeyNotFoundException">No command has the ID.</exception>
    public AppCommand this[string id] => commands.TryGetValue(id, out var command)
        ? command
        : throw new KeyNotFoundException($"No command is registered with the ID '{id}'.");

    /// <summary>
    /// Adds a command.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The same command.</returns>
    /// <exception cref="InvalidOperationException">Another command has the same ID.</exception>
    public AppCommand Add(AppCommand command)
    {
        if (!commands.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"A command with the ID '{command.Id}' is already registered.");
        }

        command.Attach(localizer, capabilities);
        return command;
    }

    /// <summary>
    /// Gets the visible commands of a main menu group, in order.
    /// </summary>
    /// <param name="group">One of <see cref="CommandGroups"/>.</param>
    /// <returns>The commands.</returns>
    public IReadOnlyList<AppCommand> InGroup(string group)
        => [.. commands.Values.Where(command => command.Group == group && command.IsVisible).OrderBy(command => command.Order)];

    /// <summary>
    /// Re-evaluates every command, after a change that may affect any of them.
    /// </summary>
    public void Refresh()
    {
        foreach (var command in commands.Values)
        {
            command.Refresh();
        }
    }
}
