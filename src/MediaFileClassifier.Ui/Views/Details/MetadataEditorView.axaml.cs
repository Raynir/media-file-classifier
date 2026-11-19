using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.ViewModels.Details;

namespace MediaFileClassifier.Ui.Views.Details;

/// <summary>
/// The metadata editor in the details panel (E07-S02). The tag input and the chips are driven from here for what a
/// binding can't do: moving focus between them and committing a suggestion.
/// </summary>
public sealed partial class MetadataEditorView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataEditorView"/> class.
    /// </summary>
    public MetadataEditorView()
    {
        InitializeComponent();
        TagInput.AddHandler(KeyDownEvent, OnTagInputKeyDown, RoutingStrategies.Tunnel);
        TagInput.SelectionChanged += OnSuggestionChosen;
        TagList.AddHandler(KeyDownEvent, OnChipKeyDown);
        TagList.AddHandler(Button.ClickEvent, OnChipButtonClick);
    }

    /// <summary>
    /// Moves focus to the first field (the title), for the Edit metadata command.
    /// </summary>
    public void FocusFirstField()
    {
        var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(candidate => !candidate.IsReadOnly);
        box?.Focus();
    }

    private MetadataEditorViewModel? Editor => DataContext as MetadataEditorViewModel;

    private IEnumerable<Border> Chips => TagList.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("Chip"));

    private void OnTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (Editor is not { } editor)
        {
            return;
        }

        if (e.Key == Key.Enter && (!TagInput.IsDropDownOpen || TagInput.SelectedItem is null))
        {
            editor.AddTypedTagCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Back && string.IsNullOrEmpty(editor.NewTagText) && e.KeyModifiers == KeyModifiers.None)
        {
            // Backspace on an empty input steps back into the chips.
            if (Chips.LastOrDefault() is { } last)
            {
                last.Focus();
                e.Handled = true;
            }
        }
    }

    private void OnSuggestionChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not string tag || Editor is not { } editor)
        {
            return;
        }

        // The box has just written the suggestion into its text; take it as a tag once that has settled.
        Dispatcher.UIThread.Post(() =>
        {
            editor.AddTag(tag);
            editor.NewTagText = string.Empty;
            TagInput.SelectedItem = null;
        });
    }

    private void OnChipKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Border { DataContext: TagChipViewModel chip } border || Editor is not { } editor)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                editor.RejectTag(chip);
                FocusAfter(border);
                break;
            case Key.Delete or Key.Back:
                if (chip.CanRemove)
                {
                    editor.RemoveTag(chip);
                }
                else if (!chip.IsUser)
                {
                    editor.RejectTag(chip);
                }

                FocusAfter(border);
                break;
            case Key.Enter or Key.Space when chip.IsRemoved || chip.IsRejected:
                editor.UndoTag(chip);
                break;
            case Key.Left:
                MoveFocus(border, -1);
                break;
            case Key.Right:
                MoveFocus(border, 1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnChipButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: string action, DataContext: TagChipViewModel chip } || Editor is not { } editor)
        {
            return;
        }

        switch (action)
        {
            case "Undo":
                editor.UndoTag(chip);
                break;
            case "Remove":
                editor.RemoveTag(chip);
                break;
            case "Reject":
                editor.RejectTag(chip);
                break;
        }
    }

    private void MoveFocus(Border from, int step)
    {
        var chips = Chips.ToList();
        var index = chips.IndexOf(from) + step;
        if (index >= chips.Count)
        {
            TagInput.Focus();
        }
        else if (index >= 0)
        {
            chips[index].Focus();
        }
    }

    // After a chip is removed or rejected, focus lands on a neighbour so the keyboard user can go on.
    private void FocusAfter(Border removed)
    {
        var chips = Chips.ToList();
        var index = chips.IndexOf(removed);
        Dispatcher.UIThread.Post(() =>
        {
            var now = Chips.ToList();
            if (now.Count == 0)
            {
                TagInput.Focus();
            }
            else
            {
                now[Math.Clamp(index, 0, now.Count - 1)].Focus();
            }
        });
    }
}
