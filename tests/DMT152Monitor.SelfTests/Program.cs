using System.Collections.Concurrent;
using DMT152Monitor.Core.Communication;
using DMT152Monitor.Core.Measurement;
using DMT152Monitor.Core.Storage;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("DMT152 tab-separated TDF/PPB parser", TestParser),
            ("DMT152 status decoding", TestStatusDecoding),
            ("DMT152 STAT response parser", TestStatusResponseParser),
            ("Default FORM reset is used for robust SEND polling", TestPollingProtocol),
            ("CSV has exactly requested three columns", TestCsvFormat),
            ("CSV writer refuses overwrite", TestCsvWriterRefusesOverwrite),
            ("Measurement settings validate timer", TestMeasurementSettings),
            ("Persistent monitor logs fake readings", TestMonitorController)
        };

        int failures = 0;
        foreach ((string name, Action body) in tests)
        {
            try
            {
                body();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine(ex);
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "All self-tests passed." : $"{failures} self-test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void TestParser()
    {
        bool ok = Dmt152Protocol.TryParseMeasurementLine("  -62.34\t      12.375\t0000\t00000", out Dmt152Reading? reading);
        AssertTrue(ok, "Parser rejected a valid measurement line.");
        AssertTrue(reading is not null, "Parser returned null reading.");
        AssertNear(-62.34, reading!.DewPointC, 1e-12);
        AssertNear(12.375, reading.H2OPpb, 1e-12);
        AssertEqual("0000", reading.StatusCode);
        AssertEqual("00000", reading.ErrorBits);

        AssertTrue(!Dmt152Protocol.TryParseMeasurementLine("OK", out _), "Parser accepted a command acknowledgement.");
        AssertTrue(Dmt152Protocol.TryParseMeasurementLine("Tdf= -2.5 'C H2O= 4919 ppm", out Dmt152Reading? defaultReading), "Parser rejected the documented default DMT152 output.");
        AssertTrue(defaultReading is not null, "Default-format parser returned null reading.");
        AssertNear(-2.5, defaultReading!.DewPointC, 1e-12);
        AssertNear(4_919_000.0, defaultReading.H2OPpb, 1e-9);
    }

    private static void TestStatusDecoding()
    {
        AssertEqual("Normal measurement", Dmt152Protocol.DescribeStatus("0000", "00000"));
        AssertTrue(Dmt152Protocol.DescribeStatus("1000", "00000").Contains("output is intentionally frozen"), "Purge status should explain frozen output.");
        AssertEqual("Normal measurement", Dmt152Protocol.DescribeStatus("0000", "000000000"));
        AssertTrue(Dmt152Protocol.DescribeStatus("0001", "00100").Contains("ERR=00100"), "Error bits were not surfaced.");
    }


    private static void TestStatusResponseParser()
    {
        AssertTrue(Dmt152Protocol.TryParseStatusResponse("0000", out string normal), "STAT parser rejected normal status.");
        AssertEqual("0000", normal);
        AssertTrue(Dmt152Protocol.TryParseStatusResponse("STAT\r\n1000\r\n>", out string purge), "STAT parser rejected purge status.");
        AssertEqual("1000", purge);
        AssertTrue(!Dmt152Protocol.TryParseStatusResponse("Tdf= -60.0 'C", out _), "STAT parser accepted non-status data.");
    }

    private static void TestPollingProtocol()
    {
        AssertEqual("FORM /", Dmt152Protocol.RestoreDefaultFormatCommand);
    }

    private static void TestCsvFormat()
    {
        string dir = CreateTempDirectory();
        try
        {
            string path = Path.Combine(dir, "content.csv");
            using (var writer = new CsvMeasurementWriter(path))
            {
                writer.WriteSample(0.0, -60.25, 12.375);
                writer.WriteSample(1.004321, -60.30, 12.125);
            }

            string[] lines = File.ReadAllLines(path);
            AssertEqual("time,Dew point,H2O ppb", lines[0]);
            AssertEqual("0,-60.25,12.375", lines[1]);
            AssertEqual("1.004321,-60.3,12.125", lines[2]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void TestCsvWriterRefusesOverwrite()
    {
        string dir = CreateTempDirectory();
        try
        {
            string path = Path.Combine(dir, "collision.csv");
            File.WriteAllText(path, "already here");

            bool threw = false;
            try
            {
                using var _ = new CsvMeasurementWriter(path);
            }
            catch (IOException)
            {
                threw = true;
            }

            AssertTrue(threw, "Expected FileMode.CreateNew to reject overwrite.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void TestMeasurementSettings()
    {
        string dir = CreateTempDirectory();
        try
        {
            var valid = new MeasurementSettings
            {
                OutputDirectory = dir,
                FileName = "x.csv",
                StopMode = MeasurementStopMode.Timer,
                TimerDurationSeconds = 1.5
            };
            valid.Validate();

            bool threw = false;
            try
            {
                (valid with { TimerDurationSeconds = 0 }).Validate();
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            AssertTrue(threw, "Zero timer duration should fail validation.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void TestMonitorController()
    {
        string dir = CreateTempDirectory();
        var fake = new FakeDmt152Session();
        try
        {
            using var controller = new Dmt152MonitorController(() => fake);
            using var signal = new ManualResetEventSlim(false);
            int updates = 0;

            controller.MeasurementUpdated += (_, _) =>
            {
                if (Interlocked.Increment(ref updates) >= 3)
                    signal.Set();
            };

            controller.ConnectAsync(new Dmt152ConnectionOptions
            {
                PortName = "COM_TEST",
                ProcessPressureBarA = 1.01325
            }).GetAwaiter().GetResult();

            string path = Path.Combine(dir, "fake.csv");
            controller.StartMeasurement(new MeasurementSettings
            {
                OutputDirectory = dir,
                FileName = "fake.csv",
                StopMode = MeasurementStopMode.Manual,
                TimerDurationSeconds = 60
            });

            AssertTrue(signal.Wait(TimeSpan.FromSeconds(2)), "Did not receive three fake measurement updates.");
            controller.StopMeasurementAsync().GetAwaiter().GetResult();
            controller.DisconnectAsync().GetAwaiter().GetResult();

            AssertTrue(File.Exists(path), "CSV was not created.");
            string[] lines = File.ReadAllLines(path);
            AssertTrue(lines.Length >= 4, "CSV did not contain at least three data rows.");
            AssertEqual("time,Dew point,H2O ppb", lines[0]);
            AssertTrue(fake.Opened, "Fake session was not opened.");
            AssertTrue(fake.Disposed, "Fake session was not disposed.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "DMT152MonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertNear(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }

    private static void AssertEqual(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeDmt152Session : IDmt152Session
    {
        private long _index;
        private bool _disposed;

        public bool Opened { get; private set; }
        public bool Disposed => _disposed;

        public Dmt152ConnectionInfo ConnectionInfo { get; private set; } =
            new("COM_TEST", 4800, 7, "N", 1, 1.01325, "Fake DMT152");

        public void OpenAndStart(Dmt152ConnectionOptions options)
        {
            options.Validate();
            Opened = true;
            ConnectionInfo = ConnectionInfo with
            {
                PortName = options.PortName,
                ProcessPressureBarA = options.ProcessPressureBarA
            };
        }

        public Dmt152Reading ReadNext(CancellationToken cancellationToken)
        {
            cancellationToken.WaitHandle.WaitOne(15);
            cancellationToken.ThrowIfCancellationRequested();
            long i = Interlocked.Increment(ref _index);
            return new Dmt152Reading(
                -60.0 - i * 0.01,
                10.0 + i,
                "0000",
                "00000",
                DateTimeOffset.Now,
                System.Diagnostics.Stopwatch.GetTimestamp());
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
