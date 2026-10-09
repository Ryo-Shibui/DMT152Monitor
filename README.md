# Vaisala DMT152 Dew Point Monitor

This is a Windows desktop application for monitoring a Vaisala DMT152 dew point transmitter from a PC. It displays the live dew point or frost point, displays H2O concentration, plots both values, and saves measurement runs to CSV.

The goal of this README is that a person starting from a new Windows PC, without Visual Studio or device drivers installed yet, can understand what to prepare and how to run the program.

## What This Program Does

- Reads a Vaisala DMT152 through a Windows COM port
- Displays dew point / frost point temperature in degrees C
- Displays H2O concentration in ppb
- Plots dew point and H2O ppb on a two-axis graph
- Saves CSV files with manual stop or timer stop
- Detects DMT152 sensor purge and auto-calibration states when `STAT` is available
- Avoids overwriting existing CSV files

CSV output uses exactly these columns:

```text
time,Dew point,H2O ppb
0.998421,-62.34,12.375
2.004817,-62.36,12.250
```

`time` is elapsed PC time from the start of the logged measurement.

## Hardware Compatibility

The software was developed for a Vaisala DMT152 transmitter. It is not a generic dew point meter application.

It should work with a DMT152 connected through any Windows-visible serial port, provided that:

- the device understands the DMT152 serial commands used by this program;
- the serial connection appears in Windows as a COM port;
- the device can answer `SEND` in the documented DMT152 default format;
- the cable or adapter supports the required RS-485 wiring for the transmitter.

The code is not tied to one exact USB cable model. A Vaisala USB service cable, or another suitable RS-485-to-USB adapter, can be used if Windows exposes it as a COM port and the wiring is correct.

Prepare:

- Vaisala DMT152 transmitter
- USB serial service cable or suitable RS-485-to-USB adapter
- Operating power for the DMT152
- 64-bit Windows 10 or Windows 11 PC
- Folder for CSV data

For long measurements, use stable operating power for the DMT152 instead of relying only on USB service-cable power.

## Install on a Fresh PC

1. Run Windows Update.
2. Install Visual Studio (for now "10/09/2026", Visual Studio 2026).
3. In Visual Studio Installer, select the `.NET desktop development` workload.
4. Install the .NET 8 SDK if it was not installed with Visual Studio.
5. Install the driver for the Vaisala USB cable or your RS-485-to-USB adapter.
6. Connect the DMT152 and confirm that Windows Device Manager shows a COM port.

PuTTY and NI-DAQmx are not required for this program.

## Build and Run

1. Download the repository from GitHub with `Code` -> `Download ZIP`, or clone it with Git.
2. If you downloaded a ZIP file, extract it to a normal working folder.
3. Open `DMT152Monitor.sln` in Visual Studio.
4. Select `Release` and `x64`.
5. Confirm that `DMT152Monitor` is the startup project.
6. Run `Build` -> `Build Solution`.
7. Start the program with `Start` or `F5`.

You can also build from PowerShell:

```powershell
.\build-release.ps1
```

The built application is normally created at:

```text
src\DMT152Monitor\bin\x64\Release\net8.0-windows\DMT152Monitor.exe
```

## Serial Communication

The program talks directly to a Windows COM port. On connection it tries these serial modes:

1. 4800 baud / 7 data bits / no parity / 1 stop bit
2. 19200 baud / 8 data bits / no parity / 1 stop bit

After connecting, it sends commands equivalent to:

```text
S
UNIT M
XPRES <pressure>
FORM /
```

Measurements are requested with `SEND` approximately once per second. The DMT152 serial interface does not provide fresh true 10 Hz data, so this program does not duplicate or interpolate values to make artificial 10 Hz output.

## Basic Operation

1. Connect the DMT152 to the PC through the serial cable or adapter.
2. Power the DMT152.
3. Start the application.
4. Click `Detect / Refresh COM Ports`.
5. Select the COM port for the DMT152.
6. Enter the absolute pressure at the sensor in `Process Pressure (bara)`. For near-atmospheric work, the default `1.01325` is a reasonable starting point.
7. Click `Connect`.
8. Confirm that Dew Point and H2O ppb update.
9. Choose the CSV folder and file name.
10. Select `Manual` or `Timer (s)`.
11. Click `Start Measurement`.
12. In manual mode, click `Stop Measurement` when finished.

Stopping a measurement does not disconnect the DMT152. Live monitoring continues after CSV logging stops.

## Pressure Compensation for H2O ppb

Dew point itself does not require pressure compensation, but volume concentration values such as ppm or ppb depend on the absolute pressure at the measurement point.

The program sends `XPRES <pressure>` when connecting. `XPRES` is a temporary DMT152 setting, so the transmitter's stored `PRES` setting is not permanently changed.

Enter absolute pressure, not gauge pressure. The GUI accepts 0.1 to 50 bara.

## Before Using Experimental Data

Check the following on the actual setup:

- Windows shows the expected COM port.
- `Connect` reaches `Connected / Monitoring`.
- Dew Point and H2O ppb update continuously.
- Sensor purge or auto-calibration states are shown when the transmitter reports them.
- The entered pressure is the absolute pressure at the sensor.
- CSV time values increase monotonically.
- The displayed value is plausible against a reference condition or another display.

## Tests

Hardware-independent self-tests cover parsing, CSV output, overwrite protection, timer validation, and monitor/logging behavior.

```powershell
.\run-self-tests.ps1
```

or:

```powershell
dotnet run --project .\tests\DMT152Monitor.SelfTests\DMT152Monitor.SelfTests.csproj
```

If Python is installed, a static source check is also available:

```powershell
python .\tests\static_source_checks.py
```

## Troubleshooting

- No COM port appears: check the cable driver, USB connection, and Device Manager.
- No value appears immediately after connection: the DMT152 may need time after power-up for self-diagnostics or sensor purge.
- H2O ppb looks wrong: confirm that `Process Pressure (bara)` is absolute pressure.
- CSV cannot be created: check folder write permission and whether a file with the same name already exists.
- Long measurements are unstable: power the DMT152 from a stable external supply.
