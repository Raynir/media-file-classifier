using System.Globalization;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Tests.Localization;

/// <summary>
/// Tests for <see cref="Localizer"/>.
/// </summary>
public sealed class LocalizerTests
{
    private static Localizer CreateLocalizer() => new(
        new System.Resources.ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
    {
        Culture = CultureInfo.GetCultureInfo("en-US"),
    };

    [Fact]
    public void Get_ReturnsTheEnglishString()
    {
        Assert.Equal("Media File Classifier", CreateLocalizer().Get("App_Title"));
    }

    [Fact]
    public void Get_MarksMissingKeys()
    {
        Assert.Equal("!No_Such_Key!", CreateLocalizer().Get("No_Such_Key"));
    }

    [Fact]
    public void Observe_PushesTheValueAndEveryChange()
    {
        var localizer = CreateLocalizer();
        var values = new List<string>();

        using (localizer.Observe("App_Title").Subscribe(new Observer(values.Add)))
        {
            localizer.Culture = CultureInfo.GetCultureInfo("en-GB");
        }

        localizer.Culture = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal(["Media File Classifier", "Media File Classifier"], values);
    }

    private sealed class Observer(Action<string> onNext) : IObserver<string>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(string value) => onNext(value);
    }
}
