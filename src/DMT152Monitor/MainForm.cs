using System.Globalization;
using DMT152Monitor.Core.Communication;
using DMT152Monitor.Core.Measurement;

namespace DMT152Monitor;

public sealed class MainForm : Form
{
    private readonly Dmt152MonitorController _controller = new();

    private readonly ComboBox _portCombo = new();
    private readonly Button _refreshPortsButton = new();
    private readonly NumericUpDown _processPressure = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly Label _connectionStatusValue = new();
    private readonly Label _serialModeValue = new();

    private readonly TextBox _directoryText = new();
    private readonly TextBox _fileNameText = new();
    private readonly Button _browseButton = new();

    private readonly RadioButton _manualRadio = new();
    private readonly RadioButton _timerRadio = new();
    private readonly NumericUpDown _timerSeconds = new();

    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();

    private readonly Label _dewPointValue = new();
    private readonly Label _h2oValue = new();
    private readonly Label _sensorStatusValue = new();
    private readonly Label _statusLabel = new();
    private readonly Label _elapsedLabel = new();

    private readonly DualAxisPlotControl _plot = new();

    private bool _closing;
    private bool _allowClose;
    private bool _connectionTransitionActive;
    private int _lastDisplayedSecond = -1;
    private long _liveReplyCount;

    public MainForm()
    {
        Text = "Vaisala DMT152 Dew Point Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10.5F, FontStyle.Regular);
        AutoScaleMode = AutoScaleMode.Dpi;

        ConfigureInitialWindowBounds();
        BuildUi();
        InitializeDefaults();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshPortsAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _controller.Dispose();

        base.Dispose(disposing);
    }

    private void ConfigureInitialWindowBounds()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        int availableWidth = Math.Max(900, area.Width - 12);
        int availableHeight = Math.Max(600, area.Height - 12);

        int targetWidth = Math.Min(availableWidth, Math.Max(1120, (int)Math.Round(area.Width * 0.96)));
        int targetHeight = Math.Min(availableHeight, Math.Max(650, (int)Math.Round(area.Height * 0.98)));

        MinimumSize = new Size(Math.Min(1040, availableWidth), Math.Min(600, availableHeight));
        Size = new Size(targetWidth, targetHeight);
    }

    private void BuildUi()
    {
        BackColor = Color.FromArgb(246, 248, 250);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Control left = BuildControlPanel();
        left.Margin = new Padding(0, 2, 7, 2);
        Control right = BuildMonitorPanel();
        right.Margin = new Padding(7, 2, 0, 2);

        content.Controls.Add(left, 0, 0);
        content.Controls.Add(right, 1, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(BuildStatusBar(), 0, 2);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Text = "Vaisala DMT152 Dew Point Monitor",
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
            Margin = new Padding(0)
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "USB-RS-485 serial | Dew point + H2O ppb | fastest DMT152 output (~1 Hz)",
            ForeColor = Color.DimGray,
            Margin = new Padding(2, 0, 0, 0)
        };

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        return header;
    }

    private Control BuildStatusBar()
    {
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "Ready";
        _statusLabel.ForeColor = Color.FromArgb(50, 70, 85);
        _statusLabel.AutoEllipsis = true;

        _elapsedLabel.AutoSize = true;
        _elapsedLabel.TextAlign = ContentAlignment.MiddleRight;
        _elapsedLabel.Anchor = AnchorStyles.Right;
        _elapsedLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _elapsedLabel.Text = "Elapsed: 0 s";

        bottom.Controls.Add(_statusLabel, 0, 0);
        bottom.Controls.Add(_elapsedLabel, 1, 0);
        return bottom;
    }

    private Control BuildControlPanel()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var settingsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 3, 0),
            Margin = new Padding(0)
        };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int i = 0; i < 3; i++)
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        settingsPanel.Controls.Add(BuildConnectionGroup(), 0, 0);
        settingsPanel.Controls.Add(BuildOutputGroup(), 0, 1);
        settingsPanel.Controls.Add(BuildStopModeGroup(), 0, 2);

        Control action = BuildActionGroup();
        action.Margin = new Padding(0, 3, 3, 0);

        outer.Controls.Add(settingsPanel, 0, 0);
        outer.Controls.Add(action, 0, 1);
        return outer;
    }

    private GroupBox BuildConnectionGroup()
    {
        var group = MakeGroup("DMT152 Connection");
        var table = MakeTwoColumnTable();

        _portCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _portCombo.Dock = DockStyle.Fill;
        _portCombo.MinimumSize = new Size(80, 0);
        AddRow(table, MakeLabel("COM Port"), _portCombo);

        _refreshPortsButton.Text = "Detect / Refresh COM Ports";
        _refreshPortsButton.Dock = DockStyle.Fill;
        _refreshPortsButton.AutoSize = true;
        _refreshPortsButton.MinimumSize = new Size(0, 30);
        AddSpanRow(table, _refreshPortsButton);

        ConfigureNumber(_processPressure, 0.1M, 50M, 5, 1.01325M, 0.001M);
        AddRow(table, MakeLabel("Process Pressure (bara)"), _processPressure);

        _serialModeValue.Text = "Auto: 4800/7N1 → 19200/8N1";
        _serialModeValue.AutoSize = true;
        _serialModeValue.ForeColor = Color.DimGray;
        AddRow(table, MakeLabel("Serial Settings"), _serialModeValue);

        var connectButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        connectButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        connectButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        _connectButton.Text = "Connect";
        _connectButton.Dock = DockStyle.Fill;
        _connectButton.MinimumSize = new Size(0, 30);
        _disconnectButton.Text = "Disconnect";
        _disconnectButton.Dock = DockStyle.Fill;
        _disconnectButton.MinimumSize = new Size(0, 30);
        _disconnectButton.Enabled = false;

        connectButtons.Controls.Add(_connectButton, 0, 0);
        connectButtons.Controls.Add(_disconnectButton, 1, 0);
        AddSpanRow(table, connectButtons);

        _connectionStatusValue.Text = "Not connected";
        _connectionStatusValue.AutoSize = true;
        _connectionStatusValue.ForeColor = Color.DimGray;
        AddRow(table, MakeLabel("Connection Status"), _connectionStatusValue);

        var rateNote = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "The monitor polls the DMT152 with SEND at about 1 sample/s. The DMT152 internal measurement cycle limits fresh data to about 1 Hz; true 10 Hz is not available from this serial interface.",
            MaximumSize = new Size(380, 0),
            Margin = new Padding(3, 1, 3, 1)
        };
        AddSpanRow(table, rateNote);

        group.Controls.Add(table);
        return group;
    }

    private GroupBox BuildOutputGroup()
    {
        var group = MakeGroup("CSV Output");
        var table = MakeTwoColumnTable();

        var dirPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        dirPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        dirPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _directoryText.Dock = DockStyle.Fill;
        _browseButton.Text = "...";
        _browseButton.AutoSize = true;
        _browseButton.MinimumSize = new Size(38, 0);
        dirPanel.Controls.Add(_directoryText, 0, 0);
        dirPanel.Controls.Add(_browseButton, 1, 0);

        _fileNameText.Dock = DockStyle.Fill;
        AddRow(table, MakeLabel("Directory"), dirPanel);
        AddRow(table, MakeLabel("File Name"), _fileNameText);

        var format = new Label
        {
            Text = "Columns: time, Dew point, H2O ppb\nExisting files are never overwritten.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(3, 1, 3, 1),
            Anchor = AnchorStyles.Left
        };
        AddSpanRow(table, format);

        group.Controls.Add(table);
        return group;
    }

    private GroupBox BuildStopModeGroup()
    {
        var group = MakeGroup("Measurement Stop Mode");
        var table = MakeTwoColumnTable();

        _manualRadio.Text = "Manual stop";
        _manualRadio.Checked = true;
        _manualRadio.AutoSize = true;
        _manualRadio.Anchor = AnchorStyles.Left;
        AddSpanRow(table, _manualRadio);

        _timerRadio.Text = "Timer (s)";
        _timerRadio.AutoSize = true;
        _timerRadio.Anchor = AnchorStyles.Left;
        ConfigureNumber(_timerSeconds, 0.001M, 8_640_000M, 3, 60M, 0.001M);
        AddRow(table, _timerRadio, _timerSeconds);

        group.Controls.Add(table);
        return group;
    }

    private GroupBox BuildActionGroup()
    {
        var group = MakeGroup("Measurement");
        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(1)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        _startButton.Text = "Start Measurement";
        _startButton.Dock = DockStyle.Fill;
        _startButton.MinimumSize = new Size(0, 34);
        _startButton.Enabled = false;

        _stopButton.Text = "Stop Measurement";
        _stopButton.Dock = DockStyle.Fill;
        _stopButton.MinimumSize = new Size(0, 34);
        _stopButton.Enabled = false;

        buttons.Controls.Add(_startButton, 0, 0);
        buttons.Controls.Add(_stopButton, 1, 0);
        group.Controls.Add(buttons);
        return group;
    }

    private Control BuildMonitorPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.White,
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var monitor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(2, 0, 2, 8)
        };
        monitor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        monitor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        monitor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        monitor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        monitor.Controls.Add(BuildValuePanel("Dew Point", _dewPointValue, "-- °C", DualAxisPlotControl.DewPointColor), 0, 0);
        monitor.Controls.Add(BuildValuePanel("H2O", _h2oValue, "-- ppb", DualAxisPlotControl.H2OPpbColor), 1, 0);

        _sensorStatusValue.AutoSize = true;
        _sensorStatusValue.Text = "Sensor status: --";
        _sensorStatusValue.ForeColor = Color.DimGray;
        _sensorStatusValue.Margin = new Padding(2, 2, 2, 0);
        monitor.Controls.Add(_sensorStatusValue, 0, 1);
        monitor.SetColumnSpan(_sensorStatusValue, 2);

        panel.Controls.Add(monitor, 0, 0);

        _plot.Dock = DockStyle.Fill;
        _plot.Margin = new Padding(0);
        panel.Controls.Add(_plot, 0, 1);
        return panel;
    }

    private static Control BuildValuePanel(string captionText, Label valueLabel, string initialText, Color color)
    {
        var valuePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(2, 0, 10, 0)
        };
        valuePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        valuePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = captionText,
            AutoSize = true,
            ForeColor = color,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Margin = new Padding(0, 2, 0, 0)
        };

        valueLabel.Text = initialText;
        valueLabel.AutoSize = true;
        valueLabel.Anchor = AnchorStyles.Left;
        valueLabel.Font = new Font("Segoe UI", 26F, FontStyle.Bold);
        valueLabel.ForeColor = color;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.Margin = new Padding(0);

        valuePanel.Controls.Add(caption, 0, 0);
        valuePanel.Controls.Add(valueLabel, 0, 1);
        return valuePanel;
    }

    private void InitializeDefaults()
    {
        string defaultDir = GetDefaultDataDirectory("DMT152Monitor.sln");

        try
        {
            Directory.CreateDirectory(defaultDir);
        }
        catch
        {
            defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        _directoryText.Text = defaultDir;
        _fileNameText.Text = $"dmt152_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        _timerSeconds.Enabled = false;
    }

    private static string GetDefaultDataDirectory(string solutionFileName)
    {
        string current = Path.GetFullPath(AppContext.BaseDirectory);

        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(current, solutionFileName)))
                return Path.Combine(current, "Data");

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
                break;

            current = parent.FullName;
        }

        return Path.Combine(AppContext.BaseDirectory, "Data");
    }

    private void WireEvents()
    {
        _refreshPortsButton.Click += async (_, _) => await RefreshPortsAsync();
        _connectButton.Click += async (_, _) => await ConnectAsync();
        _disconnectButton.Click += async (_, _) => await DisconnectAsync();
        _browseButton.Click += (_, _) => BrowseForDirectory();

        _manualRadio.CheckedChanged += (_, _) => UpdateTimerEnabledState();
        _timerRadio.CheckedChanged += (_, _) => UpdateTimerEnabledState();

        _startButton.Click += (_, _) => StartMeasurement();
        _stopButton.Click += async (_, _) => await StopMeasurementAsync();

        _controller.ReadingUpdated += ControllerOnReadingUpdated;
        _controller.MeasurementUpdated += ControllerOnMeasurementUpdated;
        _controller.MeasurementCompleted += ControllerOnMeasurementCompleted;
        _controller.ConnectionChanged += ControllerOnConnectionChanged;
        _controller.ConnectionFaulted += ControllerOnConnectionFaulted;

        FormClosing += MainForm_FormClosing;
    }

    private async Task RefreshPortsAsync()
    {
        if (_connectionTransitionActive || _controller.IsConnected || _controller.IsMeasurementRunning || _closing)
            return;

        _refreshPortsButton.Enabled = false;
        _portCombo.Enabled = false;
        _connectionStatusValue.Text = "Searching...";
        _connectionStatusValue.ForeColor = Color.DarkGoldenrod;
        _statusLabel.Text = "Searching for Windows COM ports...";

        try
        {
            IReadOnlyList<string> ports = await Task.Run(SerialPortDiscovery.GetPortNames);
            string? previous = _portCombo.SelectedItem as string;
            _portCombo.Items.Clear();
            foreach (string port in ports)
                _portCombo.Items.Add(port);

            if (previous is not null && _portCombo.Items.Contains(previous))
                _portCombo.SelectedItem = previous;
            else if (_portCombo.Items.Count > 0)
                _portCombo.SelectedIndex = 0;

            if (ports.Count == 0)
            {
                _connectionStatusValue.Text = "No COM port detected";
                _connectionStatusValue.ForeColor = Color.Firebrick;
                _statusLabel.Text = "No Windows COM ports were detected. Check the Vaisala USB cable driver and connection.";
            }
            else
            {
                _connectionStatusValue.Text = "Not connected";
                _connectionStatusValue.ForeColor = Color.DimGray;
                _statusLabel.Text = $"Detected {ports.Count} COM port(s). Select the DMT152 cable and connect.";
            }
        }
        catch (Exception ex)
        {
            _connectionStatusValue.Text = "COM detection failed";
            _connectionStatusValue.ForeColor = Color.Firebrick;
            _statusLabel.Text = "COM-port detection failed.";
            ShowError(ex.Message);
        }
        finally
        {
            _refreshPortsButton.Enabled = !_closing && !_controller.IsConnected;
            _portCombo.Enabled = !_closing && !_controller.IsConnected && _portCombo.Items.Count > 0;
            UpdateControlStates();
        }
    }

    private async Task ConnectAsync()
    {
        if (_controller.IsConnected || _connectionTransitionActive || _closing)
            return;

        string? portName = _portCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(portName))
        {
            ShowError("Select a COM port first.");
            return;
        }

        var options = new Dmt152ConnectionOptions
        {
            PortName = portName,
            ProcessPressureBarA = (double)_processPressure.Value
        };

        try
        {
            options.Validate();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return;
        }

        _connectionTransitionActive = true;
        _liveReplyCount = 0;
        UpdateControlStates();
        _connectionStatusValue.Text = "Connecting...";
        _connectionStatusValue.ForeColor = Color.DarkGoldenrod;
        _statusLabel.Text = $"Connecting to DMT152 on {portName}...";

        try
        {
            Dmt152ConnectionInfo info = await _controller.ConnectAsync(options);
            _connectionStatusValue.Text = "Connected / Waiting for measurement";
            _connectionStatusValue.ForeColor = Color.DarkGoldenrod;
            _serialModeValue.Text = info.SerialSummary;
            _statusLabel.Text = $"Connected | waiting for first DMT152 measurement | {info.PortName} | {info.SerialSummary} | XPRES={info.ProcessPressureBarA:0.#####} bara";
        }
        catch (Exception ex)
        {
            _connectionStatusValue.Text = "Connection failed";
            _connectionStatusValue.ForeColor = Color.Firebrick;
            _statusLabel.Text = "DMT152 connection failed.";
            ResetLiveValues();
            ShowError(FormatExceptionForUser(ex));
        }
        finally
        {
            _connectionTransitionActive = false;
            UpdateControlStates();
        }
    }

    private async Task DisconnectAsync()
    {
        if (!_controller.IsConnected || _connectionTransitionActive)
            return;

        _connectionTransitionActive = true;
        UpdateControlStates();
        _connectionStatusValue.Text = "Disconnecting...";
        _connectionStatusValue.ForeColor = Color.DarkGoldenrod;

        try
        {
            await _controller.DisconnectAsync();
            _connectionStatusValue.Text = "Not connected";
            _connectionStatusValue.ForeColor = Color.DimGray;
            _serialModeValue.Text = "Auto: 4800/7N1 → 19200/8N1";
            _statusLabel.Text = "Disconnected.";
            ResetLiveValues();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Disconnect failed.";
            ShowError(ex.Message);
        }
        finally
        {
            _connectionTransitionActive = false;
            UpdateControlStates();
        }
    }

    private void BrowseForDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder for DMT152 measurement CSV files",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_directoryText.Text) ? _directoryText.Text : string.Empty,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _directoryText.Text = dialog.SelectedPath;
    }

    private void StartMeasurement()
    {
        MeasurementSettings settings;
        try
        {
            settings = BuildMeasurementSettings();
            settings.Validate();
            string path = settings.GetOutputPath();
            if (File.Exists(path))
            {
                ShowError("The output file already exists. Measurement was not started.\n\n" + path);
                return;
            }

            _controller.StartMeasurement(settings);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return;
        }

        _plot.ClearData();
        _elapsedLabel.Text = "Elapsed: 0 s";
        _lastDisplayedSecond = -1;
        UpdateControlStates();

        _statusLabel.Text = settings.StopMode == MeasurementStopMode.Timer
            ? $"Measuring | Timer: {settings.TimerDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s"
            : "Measuring | Manual stop mode";
    }

    private async Task StopMeasurementAsync()
    {
        if (!_controller.IsMeasurementRunning)
            return;

        _stopButton.Enabled = false;
        _statusLabel.Text = "Stopping measurement and closing CSV file...";

        try
        {
            await _controller.StopMeasurementAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private MeasurementSettings BuildMeasurementSettings()
    {
        return new MeasurementSettings
        {
            OutputDirectory = _directoryText.Text,
            FileName = _fileNameText.Text,
            StopMode = _timerRadio.Checked ? MeasurementStopMode.Timer : MeasurementStopMode.Manual,
            TimerDurationSeconds = (double)_timerSeconds.Value
        };
    }

    private void ControllerOnReadingUpdated(object? sender, Dmt152Reading reading)
    {
        if (IsDisposed || !IsHandleCreated || _closing)
            return;

        BeginInvoke(new Action(() =>
        {
            if (_closing)
                return;

            _liveReplyCount++;
            _dewPointValue.Text = $"{reading.DewPointC:N2} °C";
            _h2oValue.Text = $"{reading.H2OPpb:N3} ppb";

            bool normalOrUnknown = string.IsNullOrWhiteSpace(reading.StatusCode) || reading.StatusCode == "0000";
            _connectionStatusValue.Text = normalOrUnknown
                ? "Connected / Monitoring"
                : "Connected / Self-diagnostic";
            _connectionStatusValue.ForeColor = normalOrUnknown ? Color.DarkGreen : Color.DarkGoldenrod;

            _sensorStatusValue.Text =
                $"Sensor status: {reading.StatusDescription} | reply #{_liveReplyCount} | {DateTime.Now:HH:mm:ss}";
            _sensorStatusValue.ForeColor = reading.StatusCode switch
            {
                "0001" => Color.Firebrick,
                "0000" or "" => Color.DimGray,
                _ => Color.DarkGoldenrod
            };

            if (!normalOrUnknown && _controller.IsMeasurementRunning)
            {
                _statusLabel.Text =
                    "Measurement armed | DMT152 self-diagnostic is active; frozen values are displayed but not written to CSV.";
            }
        }));
    }

    private void ControllerOnMeasurementUpdated(object? sender, MeasurementUpdate update)
    {
        if (IsDisposed || !IsHandleCreated || _closing)
            return;

        BeginInvoke(new Action(() =>
        {
            _plot.AddPoint(update.ElapsedSeconds, update.DewPointC, update.H2OPpb);

            int wholeSeconds = (int)Math.Floor(update.ElapsedSeconds + 1e-9);
            if (wholeSeconds != _lastDisplayedSecond)
            {
                _lastDisplayedSecond = wholeSeconds;
                _elapsedLabel.Text = $"Elapsed: {wholeSeconds} s";
            }
        }));
    }

    private void ControllerOnMeasurementCompleted(object? sender, MeasurementCompletedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated || _closing)
            return;

        BeginInvoke(new Action(() =>
        {
            int wholeSeconds = (int)Math.Floor(e.ElapsedSeconds + 1e-9);
            _elapsedLabel.Text = $"Elapsed: {wholeSeconds} s";
            UpdateControlStates();

            switch (e.Reason)
            {
                case MeasurementStopReason.TimerCompleted:
                    _statusLabel.Text = $"Timer completed | CSV saved: {e.OutputPath}";
                    break;
                case MeasurementStopReason.ConnectionLost:
                    _statusLabel.Text = $"Connection lost. Partial CSV preserved: {e.OutputPath}";
                    break;
                case MeasurementStopReason.Faulted:
                    _statusLabel.Text = $"Measurement stopped by error. Partial CSV preserved: {e.OutputPath}";
                    if (e.Exception is not null)
                        ShowError(e.Exception.Message);
                    break;
                default:
                    _statusLabel.Text = $"Measurement stopped | CSV saved: {e.OutputPath}";
                    break;
            }
        }));
    }

    private void ControllerOnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated || _closing)
            return;

        BeginInvoke(new Action(UpdateControlStates));
    }

    private void ControllerOnConnectionFaulted(object? sender, ConnectionFaultedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated || _closing)
            return;

        BeginInvoke(new Action(() =>
        {
            _connectionStatusValue.Text = "Disconnected / Error";
            _connectionStatusValue.ForeColor = Color.Firebrick;
            _serialModeValue.Text = "Auto: 4800/7N1 → 19200/8N1";
            _statusLabel.Text = "DMT152 communication stopped. Check the USB cable and reconnect.";
            ResetLiveValues();
            UpdateControlStates();
            ShowError(e.Exception.Message);
        }));
    }

    private void ResetLiveValues()
    {
        _liveReplyCount = 0;
        _dewPointValue.Text = "-- °C";
        _h2oValue.Text = "-- ppb";
        _sensorStatusValue.Text = "Sensor status: --";
        _sensorStatusValue.ForeColor = Color.DimGray;
    }

    private void UpdateTimerEnabledState()
    {
        _timerSeconds.Enabled = _timerRadio.Checked &&
                                !_controller.IsMeasurementRunning &&
                                !_connectionTransitionActive &&
                                !_closing;
    }

    private void UpdateControlStates()
    {
        bool connected = _controller.IsConnected;
        bool measuring = _controller.IsMeasurementRunning;
        bool idle = !_connectionTransitionActive && !_closing;

        _portCombo.Enabled = idle && !connected && _portCombo.Items.Count > 0;
        _refreshPortsButton.Enabled = idle && !connected;
        _processPressure.Enabled = idle && !connected;
        _connectButton.Enabled = idle && !connected && _portCombo.SelectedIndex >= 0;
        _disconnectButton.Enabled = idle && connected && !measuring;

        _directoryText.Enabled = idle && !measuring;
        _fileNameText.Enabled = idle && !measuring;
        _browseButton.Enabled = idle && !measuring;
        _manualRadio.Enabled = idle && !measuring;
        _timerRadio.Enabled = idle && !measuring;
        _timerSeconds.Enabled = idle && !measuring && _timerRadio.Checked;

        _startButton.Enabled = idle && connected && !measuring;
        _stopButton.Enabled = idle && connected && measuring;
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
            return;

        if (!_controller.IsMeasurementRunning)
        {
            _closing = true;
            return;
        }

        e.Cancel = true;
        DialogResult result = MessageBox.Show(
            this,
            "A measurement is running. Stop the measurement, save the CSV file, and close the application?",
            "Measurement in progress",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
            return;

        _closing = true;
        Enabled = false;

        try
        {
            await _controller.StopMeasurementAsync();
            if (_controller.IsConnected)
                await _controller.DisconnectAsync();

            _allowClose = true;
            Close();
        }
        finally
        {
            if (!_allowClose)
            {
                _closing = false;
                Enabled = true;
            }
        }
    }

    private static GroupBox MakeGroup(string text)
    {
        return new GroupBox
        {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 3),
            Padding = new Padding(7, 3, 7, 4)
        };
    }

    private static TableLayoutPanel MakeTwoColumnTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Margin = new Padding(0),
            Padding = new Padding(1)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
        return table;
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 4, 3, 3)
        };
    }

    private static void AddRow(TableLayoutPanel table, Control left, Control right)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Margin = new Padding(left.Margin.Left, 2, left.Margin.Right, 2);
        right.Margin = new Padding(right.Margin.Left, 2, right.Margin.Right, 2);
        right.Anchor |= AnchorStyles.Left | AnchorStyles.Right;
        table.Controls.Add(left, 0, row);
        table.Controls.Add(right, 1, row);
    }

    private static void AddSpanRow(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(control.Margin.Left, 2, control.Margin.Right, 2);
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private static void ConfigureNumber(
        NumericUpDown control,
        decimal minimum,
        decimal maximum,
        int decimals,
        decimal value,
        decimal increment)
    {
        control.Minimum = minimum;
        control.Maximum = maximum;
        control.DecimalPlaces = decimals;
        control.Value = Math.Clamp(value, minimum, maximum);
        control.Increment = increment;
        control.ThousandsSeparator = true;
        control.Dock = DockStyle.Fill;
    }

    private static string FormatExceptionForUser(Exception exception)
    {
        var messages = new List<string>();
        Exception? current = exception;
        while (current is not null)
        {
            string message = current.Message.Trim();
            if (message.Length > 0 && !messages.Contains(message, StringComparer.Ordinal))
                messages.Add(message);
            current = current.InnerException;
        }

        return string.Join(Environment.NewLine + Environment.NewLine, messages);
    }

    private void ShowError(string message)
    {
        if (_closing || IsDisposed)
            return;

        MessageBox.Show(
            this,
            message,
            "DMT152 Monitor",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
