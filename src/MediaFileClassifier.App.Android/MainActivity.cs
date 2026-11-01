using Android.Content.PM;
using Avalonia.Android;

namespace MediaFileClassifier.App.Android;

/// <summary>
/// The single activity. It hosts the shared main view (E20).
/// </summary>
[Activity(
    Label = "Media File Classifier",
    Theme = "@style/MfcTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// Intent extra with the smoke-test checks to run on launch, e.g. <c>sqlite;onnx:e5:cpu</c> (MFC-18).
    /// </summary>
    public const string SpikeChecksExtra = "spike";

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Intent?.GetStringExtra(SpikeChecksExtra) is { Length: > 0 } script)
        {
            AndroidApp.Current.RunSpikeScript(script);
        }
    }
}
