from pathlib import Path

root = Path(__file__).resolve().parents[1]
protocol = (root / "src/DMT152Monitor.Core/Communication/Dmt152Protocol.cs").read_text(encoding="utf-8")
plot = (root / "src/DMT152Monitor/DualAxisPlotControl.cs").read_text(encoding="utf-8")
ui = (root / "src/DMT152Monitor/MainForm.cs").read_text(encoding="utf-8")
csv = (root / "src/DMT152Monitor.Core/Storage/CsvMeasurementWriter.cs").read_text(encoding="utf-8")

assert '"FORM /"' in protocol
assert 'TDF' in protocol and 'PPB' in protocol
assert 'FORM 8.2 TDF' not in protocol
assert '16.3 PPB' not in protocol
assert 'DefaultOutputRegex' in protocol
assert 'h2oValue * 1000.0' in protocol
session = (root / "src/DMT152Monitor.Core/Communication/Dmt152SerialSession.cs").read_text(encoding="utf-8")
assert 'WriteCommand("SEND")' in session
assert 'WriteCommand("R")' not in session
assert 'FastestIntervalCommand' not in session
assert 'Last raw serial lines received' in session
assert 'FirstMeasurementGracePeriod = TimeSpan.FromSeconds(90)' in session
assert 'EstablishedMeasurementGracePeriod = TimeSpan.FromSeconds(30)' in session
assert 'time,Dew point,H2O ppb' in csv
assert 'DewPointColor' in plot and 'H2OPpbColor' in plot
assert 'g.DrawLine(dewAxisPen' in plot and 'g.DrawLines(dewTracePen' in plot
assert 'g.DrawLine(ppbAxisPen' in plot and 'g.DrawLines(ppbTracePen' in plot
assert 'about 1 sample/s' in ui
assert '10 Hz is not available' in ui
print("Static source checks passed.")

# Regression: every live SEND poll must also retrieve STAT so frozen self-diagnostic
# output is distinguishable from software-side caching.
assert 'WriteCommand("STAT")' in session
assert 'TryParseStatusResponse' in protocol
assert 'reply #' in ui
assert 'IsNormalOrUnknownStatus' in (root / "src/DMT152Monitor.Core/Measurement/Dmt152MonitorController.cs").read_text(encoding="utf-8")
