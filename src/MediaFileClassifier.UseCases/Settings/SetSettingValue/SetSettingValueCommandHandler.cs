using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;

namespace MediaFileClassifier.UseCases.Settings.SetSettingValue;

/// <summary>
/// Handles <see cref="SetSettingValueCommand"/>.
/// </summary>
/// <param name="store">The setting values.</param>
public sealed class SetSettingValueCommandHandler(ISettingsStore store) : IRequestHandler<SetSettingValueCommand, SetSettingValueResult>
{
    /// <inheritdoc />
    public Task<SetSettingValueResult> Handle(SetSettingValueCommand request, CancellationToken cancellationToken)
    {
        if (!store.IsAvailable(request.Setting.Scope))
        {
            return Task.FromResult(SetSettingValueResult.Unavailable);
        }

        if (request.Setting.ValidateValue(request.Value) is { } error)
        {
            return Task.FromResult(new SetSettingValueResult(error));
        }

        store.SetValue(request.Setting, request.Value);
        return Task.FromResult(SetSettingValueResult.Stored);
    }
}
