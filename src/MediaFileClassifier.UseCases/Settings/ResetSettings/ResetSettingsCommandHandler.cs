using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;

namespace MediaFileClassifier.UseCases.Settings.ResetSettings;

/// <summary>
/// Handles <see cref="ResetSettingsCommand"/>.
/// </summary>
/// <param name="registry">The registered settings.</param>
/// <param name="store">The setting values.</param>
public sealed class ResetSettingsCommandHandler(SettingRegistry registry, ISettingsStore store) : IRequestHandler<ResetSettingsCommand>
{
    /// <inheritdoc />
    public Task Handle(ResetSettingsCommand request, CancellationToken cancellationToken)
    {
        store.Reset(request.Category is null ? registry.All : registry.InCategory(request.Category));
        return Task.CompletedTask;
    }
}
