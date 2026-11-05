using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// A stage that needs a model. While the model isn't installed, the scheduler doesn't dispatch the stage's work, and
/// jobs left with only that work wait in <c>WaitingForModel</c> (D037, E06-S12-T03). They start again once the model
/// is installed.
/// </summary>
public interface IModelDependentStage : IStageHandler
{
    /// <summary>
    /// Gets what the stage needs a model for.
    /// </summary>
    ModelPurpose RequiredModel { get; }
}
