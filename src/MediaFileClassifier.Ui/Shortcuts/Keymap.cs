using MediaFileClassifier.Ui.Commands;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// The active keymap (E16-S01-T01): finds the command for a key press in the current focus context, and gives
/// every command its shortcut for menus and tooltips. Bindings to commands that this platform head doesn't
/// register are left out. Debug builds log conflicts at startup.
/// </summary>
public sealed partial class Keymap
{
    private readonly CommandRegistry commands;

    /// <summary>
    /// Initializes a new instance of the <see cref="Keymap"/> class with the <see cref="DefaultKeymap"/>.
    /// </summary>
    /// <param name="commands">The command registry.</param>
    /// <param name="logger">Logs keymap conflicts in Debug builds.</param>
    public Keymap(CommandRegistry commands, ILogger<Keymap>? logger = null)
        : this(commands, DefaultKeymap.Bindings, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Keymap"/> class.
    /// </summary>
    /// <param name="commands">The command registry.</param>
    /// <param name="bindings">The bindings. A list, not an enumerable, so dependency injection always picks
    /// the other constructor.</param>
    /// <param name="logger">Logs keymap conflicts in Debug builds.</param>
    public Keymap(CommandRegistry commands, IReadOnlyList<ShortcutBinding> bindings, ILogger<Keymap>? logger = null)
    {
        this.commands = commands;
        var all = bindings;
#if DEBUG
        if (logger is not null)
        {
            foreach (var problem in FindProblems(all, commands))
            {
                LogProblem(logger, problem);
            }
        }
#endif
        Bindings = [.. all.Where(binding => commands.TryGet(binding.CommandId, out _))];
        foreach (var group in Bindings.GroupBy(binding => binding.CommandId))
        {
            var chord = group.First().Chord;
            commands[group.Key].SetShortcut(chord.ToKeyGesture(), chord.ToString());
        }
    }

    /// <summary>
    /// Gets the bindings of registered commands, in keymap order.
    /// </summary>
    public IReadOnlyList<ShortcutBinding> Bindings { get; }

    /// <summary>
    /// Finds the command that a key press runs.
    /// </summary>
    /// <param name="chord">The keys pressed.</param>
    /// <param name="scopes">The scopes around the focused element, innermost first. <see cref="ShortcutScopes.Global"/>
    /// is tried after them.</param>
    /// <returns>The visible command bound to the keys in the innermost matching scope, or <c>null</c>.</returns>
    public AppCommand? Find(KeyChord chord, IEnumerable<string> scopes)
    {
        foreach (var scope in scopes.Append(ShortcutScopes.Global))
        {
            var binding = Bindings.FirstOrDefault(candidate => candidate.Scope == scope && candidate.Chord == chord);
            if (binding is not null && commands[binding.CommandId] is { IsVisible: true } command)
            {
                return command;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a keymap: the same keys bound twice in one scope, and bindings to commands that aren't registered.
    /// </summary>
    /// <param name="bindings">The bindings.</param>
    /// <param name="commands">The registered commands.</param>
    /// <returns>A description of each problem; empty when there are none.</returns>
    public static IReadOnlyList<string> FindProblems(IEnumerable<ShortcutBinding> bindings, CommandRegistry commands)
    {
        var list = bindings.ToList();
        var conflicts = list
            .GroupBy(binding => (binding.Scope, binding.Chord))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Chord} in scope '{group.Key.Scope}' is bound to {string.Join(", ", group.Select(binding => binding.CommandId))}.");
        var unknown = list
            .Where(binding => !commands.TryGet(binding.CommandId, out _))
            .Select(binding => $"{binding.Chord} is bound to '{binding.CommandId}', which isn't registered.");
        return [.. conflicts, .. unknown];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keymap problem: {Problem}")]
    private static partial void LogProblem(ILogger logger, string problem);
}
