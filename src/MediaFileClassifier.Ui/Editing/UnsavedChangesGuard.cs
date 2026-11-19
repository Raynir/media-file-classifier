using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Editing;

/// <summary>
/// Keeps staged changes from being lost (E07-S12-T01). Whatever leaves the edited item, such as selecting another
/// item, closing the panel, switching library or closing the app, asks <see cref="ConfirmLeaveAsync"/> first. It shows
/// a Save / Discard / Stay prompt for every registered editor that has changes. Use it on the UI thread.
/// </summary>
/// <param name="localizer">UI strings.</param>
public sealed class UnsavedChangesGuard(Localizer localizer)
{
    private readonly List<IStagedEditor> editors = [];
    private bool asking;

    /// <summary>
    /// Gets or sets how the prompt is shown. Tests replace it.
    /// </summary>
    public Func<UnsavedChangesViewModel, Task<UnsavedChangesChoice>> Ask { get; set; } = UnsavedChangesPrompt.ShowAsync;

    /// <summary>
    /// Gets a value indicating whether any registered editor has unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges => editors.Any(editor => editor.HasUnsavedChanges);

    /// <summary>
    /// Registers an editor.
    /// </summary>
    /// <param name="editor">The editor.</param>
    /// <returns>An object that unregisters the editor when disposed.</returns>
    public IDisposable Register(IStagedEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        editors.Add(editor);
        return new Registration(() => editors.Remove(editor));
    }

    /// <summary>
    /// Asks whether the user may leave what they are editing. Nothing is asked while nothing is staged.
    /// </summary>
    /// <returns><see langword="true"/> if nothing is staged any more and the user can go on; <see langword="false"/>
    /// if the user chose Stay or a save failed.</returns>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (asking)
        {
            // A prompt is open already, e.g. a second key press before it took focus.
            return false;
        }

        foreach (var editor in editors.ToArray())
        {
            if (!editor.HasUnsavedChanges)
            {
                continue;
            }

            // The prompt and the answer's action are one step: another leave during a save must not ask again.
            asking = true;
            try
            {
                var choice = await Ask(new UnsavedChangesViewModel(editor.Description, localizer));
                switch (choice)
                {
                    case UnsavedChangesChoice.Save:
                        if (!await editor.SaveAsync())
                        {
                            // The editor shows why, and its changes stay.
                            return false;
                        }

                        break;
                    case UnsavedChangesChoice.Discard:
                        editor.Discard();
                        break;
                    default:
                        return false;
                }
            }
            finally
            {
                asking = false;
            }
        }

        return true;
    }

    private sealed class Registration(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
