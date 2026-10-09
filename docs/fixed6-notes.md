# fixed6: SEND polling after real-hardware diagnosis

Observed on the user's DMT152:

- 4800/7N1: no response.
- 19200/8N1: DMT152 identification and setup commands were accepted.
- A custom FORM command did not provide a reliable acknowledgement.
- The previous R-based continuous-monitor path then produced no parseable measurement for six seconds.

To remove both uncertain layers, fixed6 uses the documented default output path:

1. `S`
2. `UNIT M`
3. `XPRES <absolute pressure>`
4. `FORM /`
5. enter a connected/waiting state
6. poll with `SEND` at approximately one-second intervals, allowing up to 90 seconds for the first valid reading

The documented default output is parsed as `Tdf= ... H2O= ... ppm`; H2O is converted to ppb by an exact factor of 1000. The GUI explicitly distinguishes serial connection from measurement readiness: it shows a waiting state until the first valid reading arrives. Timeout errors include the raw serial lines received, which makes any remaining firmware-format mismatch directly visible.
