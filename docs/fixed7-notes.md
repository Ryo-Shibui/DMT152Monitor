# fixed7

- Live acquisition still uses one fresh `SEND` request per poll.
- Each poll also queries `STAT` so DMT152 sensor-purge / auto-calibration states are visible.
- The GUI shows a monotonically increasing serial reply counter and last-reply time. This makes it clear when fresh serial replies are arriving even if the instrument intentionally freezes its numerical output during self-diagnostics.
- Explicit non-normal self-diagnostic readings are not written to CSV; acquisition resumes automatically when status returns to `0000`.
- No change to requested CSV columns: `time,Dew point,H2O ppb`.
