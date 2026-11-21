using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// The grid's rows (E14-S01-T01), virtualised by its <c>VirtualizingStackPanel</c>. Each row container is a
/// <see cref="ThumbnailRowPanel"/> that keeps its tile views when the panel recycles it for another row, and the row
/// it shows is attached and detached as that happens, so only rows on screen load pages and thumbnails.
/// </summary>
public sealed class ThumbnailRowsControl : ItemsControl
{
    /// <summary>
    /// Defines the <see cref="TileTemplate"/> property.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> TileTemplateProperty =
        AvaloniaProperty.Register<ThumbnailRowsControl, IDataTemplate?>(nameof(TileTemplate));

    /// <summary>
    /// Gets or sets the template a tile view is built from.
    /// </summary>
    public IDataTemplate? TileTemplate
    {
        get => GetValue(TileTemplateProperty);
        set => SetValue(TileTemplateProperty, value);
    }

    static ThumbnailRowsControl()
    {
        // Rows on screen take a new template at once; recycled ones take it when they're prepared again.
        TileTemplateProperty.Changed.AddClassHandler<ThumbnailRowsControl>((control, _) =>
        {
            foreach (var panel in control.GetRealizedContainers().OfType<ThumbnailRowPanel>())
            {
                panel.TileTemplate = control.TileTemplate;
            }
        });
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new ThumbnailRowPanel { TileTemplate = TileTemplate };

    /// <inheritdoc />
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (item is ThumbnailRowViewModel row && container is ThumbnailRowPanel panel)
        {
            panel.Row = row;
            panel.TileTemplate = TileTemplate;
            panel.Tiles = row.Tiles;
            row.Attach();
        }
    }

    /// <inheritdoc />
    protected override void ClearContainerForItemOverride(Control container)
    {
        if (container is ThumbnailRowPanel panel)
        {
            panel.Row?.Detach();
            panel.Row = null;
        }

        base.ClearContainerForItemOverride(container);
    }
}
