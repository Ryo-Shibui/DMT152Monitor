using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DMT152Monitor.Core.Communication;

public static class Dmt152Protocol
{
    // Restore the documented default output before monitoring. The default SEND response is
    // e.g. "Tdf= -2.0 'C H2O= 5152 ppm". The application converts ppm to ppb exactly by
    // multiplying by 1000. This avoids firmware-dependent custom FORM parsing.
    public const string RestoreDefaultFormatCommand = "FORM /";

    private static readonly Regex DefaultOutputRegex = new(
        @"Tdf\s*=\s*(?<tdf>[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?)\s*(?:'|°)?C\b.*?H2O\s*=\s*(?<h2o>[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?)\s*(?<unit>ppm[v]?|ppb[v]?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StatusRegex = new(
        @"(?<![01])(?<status>[01]{4})(?![01])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParseStatusResponse(string? text, out string statusCode)
    {
        statusCode = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        Match match = StatusRegex.Match(text);
        if (!match.Success)
            return false;

        statusCode = match.Groups["status"].Value;
        return true;
    }

    public static bool TryParseMeasurementLine(string? line, out Dmt152Reading? reading)
    {
        reading = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        // Backward-compatible parser for an earlier custom format that may still be active
        // on a transmitter: TDF<TAB>PPB<TAB>STAT<TAB>ERR.
        string[] parts = line.Split('\t');
        if (parts.Length >= 2 &&
            double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double dewPointC) &&
            double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double h2oPpb) &&
            double.IsFinite(dewPointC) &&
            double.IsFinite(h2oPpb))
        {
            string status = parts.Length >= 3 ? parts[2].Trim() : string.Empty;
            string errors = parts.Length >= 4 ? parts[3].Trim() : string.Empty;

            reading = CreateReading(dewPointC, h2oPpb, status, errors);
            return true;
        }

        // Documented factory/default output, for example:
        //   Tdf= -2.5 'C H2O= 4919 ppm
        // ppm and ppb are dimensionless volume-fraction scales that differ by exactly 1000.
        Match match = DefaultOutputRegex.Match(line);
        if (!match.Success)
            return false;

        if (!double.TryParse(match.Groups["tdf"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out dewPointC) ||
            !double.TryParse(match.Groups["h2o"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double h2oValue) ||
            !double.IsFinite(dewPointC) ||
            !double.IsFinite(h2oValue))
        {
            return false;
        }

        string unit = match.Groups["unit"].Value;
        h2oPpb = unit.StartsWith("ppm", StringComparison.OrdinalIgnoreCase)
            ? h2oValue * 1000.0
            : h2oValue;

        reading = CreateReading(dewPointC, h2oPpb, string.Empty, string.Empty);
        return true;
    }

    private static Dmt152Reading CreateReading(double dewPointC, double h2oPpb, string status, string errors)
    {
        return new Dmt152Reading(
            dewPointC,
            h2oPpb,
            status,
            errors,
            DateTimeOffset.Now,
            Stopwatch.GetTimestamp());
    }

    public static string DescribeStatus(string? statusCode, string? errorBits)
    {
        string description = statusCode?.Trim() switch
        {
            "0000" => "Normal measurement",
            "1000" => "Sensor purge (DMT152 output is intentionally frozen)",
            "1010" => "Settling after sensor purge (self-diagnostic output may remain frozen)",
            "1100" => "Auto-calibration after sensor purge (DMT152 output is intentionally frozen)",
            "1110" => "Settling after purge + auto-calibration (self-diagnostic output may remain frozen)",
            "0100" => "Auto-calibration (DMT152 output is intentionally frozen)",
            "0110" => "Settling after auto-calibration (self-diagnostic output may remain frozen)",
            "0001" => "Error state",
            "" or null => "Live measurement",
            _ => $"Status {statusCode.Trim()}"
        };

        string trimmedErrors = errorBits?.Trim() ?? string.Empty;
        bool hasActiveErrorBits = trimmedErrors.Length > 0 && trimmedErrors.Any(c => c != '0');
        if (hasActiveErrorBits)
            description += $" | ERR={trimmedErrors}";

        return description;
    }
}
