using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SignalDx.Cat;

public sealed record CatDevice(string Port, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class CatDeviceEnumerator
{
    public static IReadOnlyList<CatDevice> ListDevices()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var port in SerialPort.GetPortNames())
            map[Normalize(port)] = Normalize(port);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var (port, name) in QueryWmiFriendlyNames())
            {
                string key = Normalize(port);
                map[key] = string.IsNullOrWhiteSpace(name) || name.Equals(key, StringComparison.OrdinalIgnoreCase)
                    ? key
                    : $"{key} — {name}";
            }
        }

        // Useful Hamlib non-serial targets
        map.TryAdd("localhost", "localhost (network / NET rigctl)");
        map.TryAdd("127.0.0.1", "127.0.0.1 (network / NET rigctl)");

        return map
            .OrderBy(kv => PortSortKey(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new CatDevice(kv.Key, kv.Value))
            .ToList();
    }

    static IEnumerable<(string Port, string Name)> QueryWmiFriendlyNames()
    {
        var results = new List<(string, string)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, Description FROM Win32_SerialPort");
            foreach (ManagementObject obj in searcher.Get())
            {
                string id = obj["DeviceID"]?.ToString() ?? "";
                string name = obj["Name"]?.ToString()
                              ?? obj["Description"]?.ToString()
                              ?? id;
                if (!string.IsNullOrWhiteSpace(id))
                    results.Add((Normalize(id), CleanName(name, id)));
            }
        }
        catch
        {
            // WMI may be unavailable
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "";
                var m = Regex.Match(name, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string id = Normalize(m.Groups[1].Value);
                results.Add((id, CleanName(name, id)));
            }
        }
        catch
        {
            // ignore
        }

        return results;
    }

    static string CleanName(string name, string port)
    {
        string cleaned = Regex.Replace(name, @"\s*\(COM\d+\)\s*", " ", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        if (cleaned.Equals(port, StringComparison.OrdinalIgnoreCase)) return port;
        return cleaned;
    }

    static string Normalize(string port) => port.Trim().ToUpperInvariant();

    static int PortSortKey(string port)
    {
        var m = Regex.Match(port, @"COM(\d+)", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 10_000;
    }
}
