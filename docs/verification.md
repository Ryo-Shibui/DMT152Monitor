# Verification record

## Completed in the construction environment

- Reviewed the supplied DMT152 user guide for RS-485/USB serial operation, `FORM /`, `SEND`, pressure compensation, default Tdf/H2O output, and the approximately one-second internal measurement cycle.
- Incorporated the real-hardware observations from the preceding diagnostic builds: this transmitter responds at 19200/8N1, while the custom FORM + R continuous-output path did not yield a valid live measurement.
- Source-level checks confirm:
  - monitoring uses `FORM /` plus `SEND` polling rather than custom FORM + `R` streaming;
  - the default `Tdf= ... H2O= ... ppm` response is parsed and ppm is converted to ppb by ×1000;
  - after serial setup the GUI enters a waiting state and the read loop allows up to 90 seconds for the first parseable measurement;
  - timeout diagnostics include the raw serial lines received;
  - CSV header is exactly `time,Dew point,H2O ppb`;
  - Dew-point left axis and trace share one color;
  - H2O right axis and trace share another color;
  - no artificial 10 Hz interpolation or duplication is performed.
- Included a hardware-independent self-test project for parser, CSV, overwrite protection, measurement settings, and persistent monitor/log controller behavior.

## Not executable in this construction environment

The construction container does not have the .NET SDK or Windows desktop targeting pack installed, and it has no Windows COM device or DMT152 attached. Therefore:

- a real Visual Studio/.NET WinForms compile was not executed here;
- native Win32 COM calls were not exercised here;
- real DMT152 hardware communication was not exercised here.

Run `run-self-tests.ps1` and a `Release | x64` build on the target Windows PC before experimental use, then perform the hardware acceptance checks in `README_JA.md`.
