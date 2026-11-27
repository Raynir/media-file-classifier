using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Duplicates.Review;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Duplicates;

/// <summary>
/// The comparison of one duplicate group (E12-S03-T02): a card per member with its preview and its attributes side by
/// side, the keeper choice with the rule-based suggestion (T03), and the synchronised zoom compare (T04).
/// </summary>
public sealed partial class DuplicateComparisonViewModel : ViewModelBase, IDisposable
{
    private readonly ISender sender;
    private readonly ThumbnailLoader thumbnails;
    private readonly KeeperRuleSettings keeperSettings;
    private readonly Localizer localizer;
    private Id<DuplicateGroup>? groupId;
    private CancellationTokenSource? loading;
    private int generation;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateComparisonViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the queries.</param>
    /// <param name="thumbnails">Loads the previews.</param>
    /// <param name="keeperSettings">Keeps the keeper rule order.</param>
    /// <param name="localizer">UI strings.</param>
    public DuplicateComparisonViewModel(ISender sender, ThumbnailLoader thumbnails, KeeperRuleSettings keeperSettings, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        this.sender = sender;
        this.thumbnails = thumbnails;
        this.keeperSettings = keeperSettings;
        this.localizer = localizer;
        SuggestionText = string.Empty;
        ReloadRules();
    }

    /// <summary>
    /// The most thumbnails a list row shows.
    /// </summary>
    public const int StripSize = 4;

    /// <summary>
    /// Gets the group shown, or <see langword="null"/> when none is.
    /// </summary>
    public Id<DuplicateGroup>? GroupId => groupId;

    /// <summary>
    /// Gets the cards, one per member.
    /// </summary>
    public ObservableCollection<DuplicateMemberViewModel> Members { get; } = [];

    /// <summary>
    /// Gets the keeper rules in the user's order, for the popover.
    /// </summary>
    public ObservableCollection<KeeperRuleItem> Rules { get; } = [];

    /// <summary>
    /// Gets the area every preview shows in zoom compare.
    /// </summary>
    public ZoomState Zoom { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the group is loading.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>
    /// Gets a value indicating whether zoom compare is offered: only visual groups have it, since exact copies look the same.
    /// </summary>
    [ObservableProperty]
    public partial bool CanZoom { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the large previews replace the attribute rows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsMode))]
    public partial bool IsZoomMode { get; set; }

    /// <summary>
    /// Gets a value indicating whether the attribute rows are shown.
    /// </summary>
    public bool IsDetailsMode => !IsZoomMode;

    /// <summary>
    /// Gets the text that names the suggested keeper and the rule that picked it.
    /// </summary>
    [ObservableProperty]
    public partial string SuggestionText { get; private set; }

    /// <summary>
    /// Gets the member the user chose to keep, which starts as the suggested one.
    /// </summary>
    [ObservableProperty]
    public partial DuplicateMemberViewModel? Keeper { get; private set; }

    /// <summary>
    /// Shows a group and its members, replacing the previous one.
    /// </summary>
    /// <param name="id">The group, or <see langword="null"/> to clear the comparison.</param>
    /// <returns>A task that completes when the cards are filled; the previews follow.</returns>
    public async Task ShowAsync(Id<DuplicateGroup>? id)
    {
        groupId = id;
        var mine = ++generation;
        CancelPreviews();
        if (id is not { } value)
        {
            Clear();
            return;
        }

        IsLoading = Members.Count == 0;
        DuplicateGroupDetail? detail;
        try
        {
            detail = await sender.Send(new GetDuplicateGroupQuery(value, [.. Rules.Select(rule => rule.Rule)])).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // E.g. the library closed meanwhile: nothing to compare.
            detail = null;
        }

        if (mine != generation)
        {
            return;
        }

        Clear();
        IsLoading = false;
        if (detail is null)
        {
            return;
        }

        CanZoom = detail.Type != DuplicateGroupType.Exact;
        foreach (var member in detail.Members)
        {
            Members.Add(new DuplicateMemberViewModel(member, localizer));
        }

        DuplicateMemberViewModel.FillCells(Members, localizer);
        OnFocusedIndexChanged(FocusedIndex);
        var suggested = detail.Suggestion is { } suggestion ? Members.FirstOrDefault(card => card.Detail.ItemId == suggestion.ItemId) : null;
        if (suggested is not null)
        {
            suggested.IsSuggested = true;
            suggested.IsKeeper = true;
            Keeper = suggested;
        }

        SuggestionText = detail.Suggestion switch
        {
            null => localizer.Get("Duplicates_NoKeeper"),
            { DecidingRule: { } rule } => localizer.Format("Duplicates_SuggestedBy", RuleName(rule)),
            _ => localizer.Get("Duplicates_SuggestedTie"),
        };

        loading = new CancellationTokenSource();
        var token = loading.Token;
        await Task.WhenAll(Members.Select(member => LoadPreviewAsync(member, token))).ConfigureAwait(true);
    }

    /// <summary>
    /// Loads the thumbnails of the first members of a group, for the strip on its list row (E12-S03-T01).
    /// </summary>
    /// <param name="id">The group.</param>
    /// <param name="cancellationToken">Cancels when the row scrolled away.</param>
    /// <returns>Leases on the thumbnails, which the caller disposes; members without a thumbnail are left out.</returns>
    public async Task<IReadOnlyList<ThumbnailLease>> LoadStripAsync(Id<DuplicateGroup> id, CancellationToken cancellationToken)
    {
        List<ThumbnailLease> leases = [];
        try
        {
            var detail = await sender.Send(new GetDuplicateGroupQuery(id, [.. Rules.Select(rule => rule.Rule)]), cancellationToken).ConfigureAwait(true);
            foreach (var member in detail?.Members.Take(StripSize) ?? [])
            {
                if (await thumbnails.LoadAsync(member.ItemId, member.FileModifiedAt, ThumbnailSize.Small, cancellationToken).ConfigureAwait(true) is { } lease)
                {
                    leases.Add(lease);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            // Scrolled away, or the library closed: the row shows no strip.
        }

        if (cancellationToken.IsCancellationRequested)
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }

            return [];
        }

        return leases;
    }

    /// <summary>
    /// Chooses the file to keep. A file that can't be kept is ignored.
    /// </summary>
    /// <param name="member">The card.</param>
    [RelayCommand]
    public void SetKeeper(DuplicateMemberViewModel? member)
    {
        if (member is null || !member.Detail.IsAvailable)
        {
            return;
        }

        foreach (var other in Members)
        {
            other.IsKeeper = ReferenceEquals(other, member);
        }

        Keeper = member;
    }

    /// <summary>
    /// Moves the keeper choice to the next card, as the right arrow does; offline cards are skipped.
    /// </summary>
    /// <param name="step">1 for the next card, -1 for the previous one.</param>
    public void MoveFocus(int step)
    {
        if (Members.Count == 0)
        {
            return;
        }

        // Cards that can't be kept are skipped; at the end the focus stays where it is.
        for (var index = FocusedIndex + step; index >= 0 && index < Members.Count; index += step)
        {
            if (Members[index].Detail.IsAvailable)
            {
                FocusedIndex = index;
                return;
            }
        }
    }

    /// <summary>
    /// Gets or sets the card the arrow keys are on.
    /// </summary>
    [ObservableProperty]
    public partial int FocusedIndex { get; set; }

    partial void OnFocusedIndexChanged(int value)
    {
        for (var index = 0; index < Members.Count; index++)
        {
            Members[index].IsCurrent = index == value;
        }
    }

    /// <summary>
    /// Makes the card the arrow keys are on the keeper (Space).
    /// </summary>
    [RelayCommand]
    public void SetFocusedKeeper()
    {
        if (FocusedIndex >= 0 && FocusedIndex < Members.Count)
        {
            SetKeeper(Members[FocusedIndex]);
        }
    }

    /// <summary>
    /// Turns zoom compare on or off. Without a visual group it stays off.
    /// </summary>
    [RelayCommand]
    public void ToggleZoom()
    {
        if (IsZoomMode)
        {
            IsZoomMode = false;
        }
        else if (CanZoom && Members.Count > 0)
        {
            Zoom.Reset();
            IsZoomMode = true;
        }
    }

    /// <summary>
    /// Leaves zoom compare and returns to the attribute rows.
    /// </summary>
    [RelayCommand]
    public void LeaveZoom() => IsZoomMode = false;

    /// <summary>
    /// Shows the whole image in every preview again.
    /// </summary>
    [RelayCommand]
    public void ResetZoom() => Zoom.Reset();

    /// <summary>
    /// Moves a keeper rule one place up in the order and applies the new order.
    /// </summary>
    /// <param name="item">The rule.</param>
    /// <returns>A task that completes when the suggestion is updated.</returns>
    [RelayCommand]
    public Task MoveRuleUpAsync(KeeperRuleItem? item) => MoveRuleAsync(item, -1);

    /// <summary>
    /// Moves a keeper rule one place down in the order and applies the new order.
    /// </summary>
    /// <param name="item">The rule.</param>
    /// <returns>A task that completes when the suggestion is updated.</returns>
    [RelayCommand]
    public Task MoveRuleDownAsync(KeeperRuleItem? item) => MoveRuleAsync(item, 1);

    /// <summary>
    /// Goes back to the default order of the keeper rules.
    /// </summary>
    /// <returns>A task that completes when the suggestion is updated.</returns>
    [RelayCommand]
    public async Task ResetRulesAsync()
    {
        keeperSettings.Save(KeeperRules.Default);
        ReloadRules();
        await ShowAsync(groupId).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the rule order of the open library again, e.g. after the library changed.
    /// </summary>
    public void ReloadRules()
    {
        Rules.Clear();
        var order = keeperSettings.Load();
        for (var index = 0; index < order.Count; index++)
        {
            Rules.Add(new KeeperRuleItem(order[index], RuleName(order[index]), index + 1));
        }
    }

    private async Task MoveRuleAsync(KeeperRuleItem? item, int step)
    {
        if (item is null)
        {
            return;
        }

        var from = Rules.IndexOf(item);
        var to = from + step;
        if (from < 0 || to < 0 || to >= Rules.Count)
        {
            return;
        }

        Rules.Move(from, to);
        for (var index = 0; index < Rules.Count; index++)
        {
            Rules[index].Position = index + 1;
        }

        keeperSettings.Save(Rules.Select(rule => rule.Rule));
        await ShowAsync(groupId).ConfigureAwait(true);
    }

    private string RuleName(KeeperRule rule) => localizer.Get("Duplicates_Rule_" + rule);

    private async Task LoadPreviewAsync(DuplicateMemberViewModel member, CancellationToken token)
    {
        try
        {
            var lease = await thumbnails.LoadAsync(member.Detail.ItemId, member.Detail.FileModifiedAt, ThumbnailSize.Large, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                lease?.Dispose();
                return;
            }

            member.SetPreview(lease);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // The group changed, or the file can't be read: the card keeps its placeholder.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelPreviews();
        Clear();
    }

    private void CancelPreviews()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
    }

    private void Clear()
    {
        foreach (var member in Members)
        {
            member.SetPreview(null);
        }

        Members.Clear();
        Keeper = null;
        SuggestionText = string.Empty;
        CanZoom = false;
        IsZoomMode = false;
        FocusedIndex = 0;
        Zoom.Reset();
        IsLoading = false;
    }
}

/// <summary>
/// A keeper rule in the order popover.
/// </summary>
public sealed partial class KeeperRuleItem : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeeperRuleItem"/> class.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="label">Its name.</param>
    /// <param name="position">Its place in the order, from 1.</param>
    public KeeperRuleItem(KeeperRule rule, string label, int position)
    {
        Rule = rule;
        Label = label;
        Position = position;
    }

    /// <summary>
    /// Gets the rule.
    /// </summary>
    public KeeperRule Rule { get; }

    /// <summary>
    /// Gets the rule's name.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets or sets the place in the order, from 1.
    /// </summary>
    [ObservableProperty]
    public partial int Position { get; set; }
}
