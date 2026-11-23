using System.Reflection;

namespace SignalDx.Data;

/// <summary>Reads text files compiled into SignalDx.Core as embedded resources.</summary>
public static class BundledData
{
    public static IEnumerable<string> Lines(string fileName)
    {
        using var stream = typeof(BundledData).Assembly.GetManifestResourceStream("SignalDx.Data." + fileName);
        if (stream is null) yield break;

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
