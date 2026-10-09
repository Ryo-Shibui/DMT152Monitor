using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DMT152Monitor.Core.Communication;

public static partial class SerialPortDiscovery
{
    public static IReadOnlyList<string> GetPortNames()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("COM-port discovery is supported only on Windows.");

        int capacity = 16 * 1024;
        char[] buffer;
        uint chars;

        while (true)
        {
            buffer = new char[capacity];
            chars = QueryDosDevice(null, buffer, buffer.Length);
            if (chars != 0)
                break;

            const int ErrorInsufficientBuffer = 122;
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorInsufficientBuffer || capacity >= 1024 * 1024)
                throw new Win32Exception(error, "Unable to enumerate Windows DOS devices.");

            capacity *= 2;
        }

        string joined = new(buffer, 0, checked((int)chars));
        string[] names = joined.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        return names
            .Where(name => ComPortRegex().IsMatch(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetPortNumber)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetPortNumber(string name)
    {
        return int.TryParse(name.AsSpan(3), out int number) ? number : int.MaxValue;
    }

    [GeneratedRegex("^COM[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComPortRegex();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string? lpDeviceName, char[] lpTargetPath, int ucchMax);
}
