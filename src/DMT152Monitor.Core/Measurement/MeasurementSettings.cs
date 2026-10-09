namespace DMT152Monitor.Core.Measurement;

public sealed record MeasurementSettings
{
    public required string OutputDirectory { get; init; }
    public required string FileName { get; init; }
    public MeasurementStopMode StopMode { get; init; } = MeasurementStopMode.Manual;
    public double TimerDurationSeconds { get; init; } = 60.0;

    public string GetOutputPath()
    {
        ValidateFileNameOnly();
        string fileName = FileName.Trim();
        string extension = Path.GetExtension(fileName);
        if (extension.Length == 0)
            fileName += ".csv";
        else if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File name must use the .csv extension.");

        return Path.Combine(OutputDirectory.Trim(), fileName);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException("Select an output directory.");

        if (!Directory.Exists(OutputDirectory.Trim()))
            throw new DirectoryNotFoundException("The selected output directory does not exist.");

        ValidateFileNameOnly();

        if (StopMode == MeasurementStopMode.Timer &&
            (!double.IsFinite(TimerDurationSeconds) || TimerDurationSeconds <= 0.0))
        {
            throw new ArgumentException("Timer duration must be greater than 0 s.");
        }
    }

    private void ValidateFileNameOnly()
    {
        if (string.IsNullOrWhiteSpace(FileName))
            throw new ArgumentException("Enter a file name.");

        string trimmed = FileName.Trim();
        if (!string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal))
            throw new ArgumentException("Enter only a file name. Select the directory separately.");

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The file name contains invalid characters.");
    }
}
