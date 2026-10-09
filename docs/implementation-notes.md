# Implementation notes

## Architecture

The original manometer application used separate NI-DAQ preview and logged acquisition tasks because a physical NI analog-input resource should not be owned by two DAQmx tasks simultaneously.

The DMT152 version intentionally changes that internal pattern while preserving the user-facing workflow:

```text
DMT152
  |
RS-485 / USB COM port
  |
Dmt152SerialSession  (one persistent connection)
  |
Dmt152MonitorController
  |-- always -> live numerical monitor
  |-- when measurement active -> CSV writer
  `-- when measurement active -> dual-axis graph
```

The serial connection remains open before, during and after CSV logging. Start/Stop therefore toggles logging rather than tearing down and recreating the hardware connection.

## Serial implementation

`Win32SerialPort` uses native Windows functions (`CreateFile`, `GetCommState`, `SetCommState`, `ReadFile`, `WriteFile`, `PurgeComm`) so the project does not depend on `System.IO.Ports` or a third-party package.

`SerialPortDiscovery` uses `QueryDosDevice` and filters `COM<number>` names.

## Connection probing

The session tries:

- 4800 / 7N1
- 19200 / 8N1

A probe first sends `S` to stop a possible continuous output, then queries `?`. If normal `?` does not identify a DMT152, `??` is attempted because the manual specifies that it overrides POLL mode. A limited POLL-mode OPEN fallback is included.

## DMT152 output line

The configured line is tab separated:

```text
<TDF>\t<PPB>\t<STAT>\t<ERR>\r\n
```

Example parser input:

```text
-62.34\t12.375\t0000\t00000
```

The GUI/CSV never depends on localized decimal separators; parsing and writing use `InvariantCulture`.

## Time base

The DMT152 does not provide a 10 Hz data stream. The application records each actual received measurement. CSV time is based on `Stopwatch.GetTimestamp()` at line parsing and the measurement-start timestamp. It is therefore not generated from a nominal sample index.

## Plot colors

`DualAxisPlotControl` defines one color for Dew Point and one for H2O ppb. The corresponding trace, tick labels, Y-axis line and Y-axis title all share that same color, making the left/right-axis mapping visually explicit.
