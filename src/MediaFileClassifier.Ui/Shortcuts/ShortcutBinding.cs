namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// One row of the keymap: the keys that run a command in a scope.
/// </summary>
/// <param name="CommandId">The command's ID, e.g. <c>view.toggle-navigation</c>.</param>
/// <param name="Scope">One of <see cref="ShortcutScopes"/>.</param>
/// <param name="Chord">The physical keys.</param>
public sealed record ShortcutBinding(string CommandId, string Scope, KeyChord Chord);
