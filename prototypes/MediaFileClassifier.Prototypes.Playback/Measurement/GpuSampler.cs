using System.Diagnostics;
using System.Globalization;

namespace MediaFileClassifier.Prototypes.Playback.Measurement;

/// <summary>
/// Samples the Windows "GPU Engine" counters (what Task Manager shows) once a second with <c>typeperf</c>,
/// and sums the utilisation of the given processes by engine type (3D, VideoDecode, Copy …).
/// </summary>
internal sealed class GpuSampler
{
    private readonly Process process;

    private GpuSampler(Process process) => this.process = process;

    public static GpuSampler Start(int seconds)
    {
        var info = new ProcessStartInfo("typeperf")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(@"\GPU Engine(*)\Utilization Percentage");
        info.ArgumentList.Add("-si");
        info.ArgumentList.Add("1");
        info.ArgumentList.Add("-sc");
        info.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
        return new GpuSampler(Process.Start(info) ?? throw new InvalidOperationException("typeperf did not start."));
    }

    /// <summary>
    /// Waits for the samples and returns the average utilisation per engine type, in percent.
    /// </summary>
    public IReadOnlyDictionary<string, double> Finish(IReadOnlyCollection<int> processIds)
    {
        var lines = process.StandardOutput.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('"'))
            .Select(line => line.Split("\",\"").Select(cell => cell.Trim('"')).ToArray())
            .ToList();
        process.WaitForExit();
        process.Dispose();
        if (lines.Count < 2)
        {
            return new Dictionary<string, double>();
        }

        var header = lines[0];
        var samples = lines.Skip(1).ToList();
        var totals = new Dictionary<string, double>();
        for (var column = 1; column < header.Length; column++)
        {
            var name = header[column];
            if (!processIds.Any(id => name.Contains($"pid_{id}_", StringComparison.Ordinal)))
            {
                continue;
            }

            var engineType = name[(name.IndexOf("engtype_", StringComparison.Ordinal) + "engtype_".Length)..].Split(')')[0];
            var average = samples
                .Select(row => column < row.Length && double.TryParse(row[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0)
                .Average();
            totals[engineType] = totals.GetValueOrDefault(engineType) + average;
        }

        return totals;
    }
}
