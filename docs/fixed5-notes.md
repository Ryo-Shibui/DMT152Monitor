# fixed5 communication compatibility update

Observed on the user's DMT152:

- 4800/7N1: no DMT152 response.
- 19200/8N1: DMT152 detected successfully.
- `FORM 8.2 TDF #t PPB #t STAT #t ERR #r #n`: no acknowledgement was returned.

This build therefore makes two compatibility changes:

1. A missing acknowledgement to `FORM` is no longer treated as a connection failure. Explicit `Error` or `Unknown` responses still fail configuration.
2. The measurement parser accepts both:
   - the preferred tab-separated TDF/PPB output; and
   - the documented default output such as `Tdf= -2.5 'C H2O= 4919 ppm`.

For the default output, H2O ppm is converted exactly to ppb by multiplying by 1000.

No artificial 10 Hz resampling or duplicate samples are introduced.
