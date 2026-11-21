using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Ui.Models;

namespace MediaFileClassifier.Ui.Tests.Models;

/// <summary>
/// Tests for the folder scan of the model import (<see cref="ModelDialogs.FindModelFiles"/>).
/// </summary>
public sealed class ModelDialogsTests
{
    [Fact]
    public void FindModelFiles_ReturnsOnlyOnnxAndModelFilesDirectlyInTheFolder()
    {
        using var folder = new TemporaryFolder();
        foreach (var name in new[] { "vision.onnx", "TEXT.ONNX", "tokenizer.model", "readme.txt", "sub/inner.onnx" })
        {
            var path = folder.Combine(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }

        var found = ModelDialogs.FindModelFiles(folder.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["TEXT.ONNX", "tokenizer.model", "vision.onnx"], found);
    }

    [Fact]
    public void FindModelFiles_ReturnsNothingForAMissingFolder()
    {
        using var folder = new TemporaryFolder();

        Assert.Empty(ModelDialogs.FindModelFiles(folder.Combine("missing")));
    }
}
