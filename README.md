# Signal DX

Windows FT8 operator console inspired by JTDX — live waterfall, band activity, TX macros, and a QSO log.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Run

```powershell
dotnet run --project src\SignalDx\SignalDx.csproj
```

## What you get

| Area | Behavior |
|------|----------|
| Waterfall | Live spectrum from the selected input device (mic / VAC / radio audio) |
| Band activity | Decode list (simulated FT8 traffic by default, synced to 15 s UTC slots) |
| TX macros | CQ / Tx1–Tx5 sequence builders + free text |
| TX audio | 8-FSK tone generator for monitoring through the selected output |
| QSO log | Persisted under `%LocalAppData%\SignalDx\qso-log.json` |
| Settings | Call, grid, band, dial, devices saved to `%LocalAppData%\SignalDx\settings.json` |

## Typical workflow

1. Set **My Call** / **Grid**, pick input/output devices.
2. Click **Monitor** to feed the waterfall.
3. Leave **Simulate decodes** on to practice the operator loop, or turn it off when you wire a real decoder later.
4. Click a CQ in Band Activity to load Dx Call / Rx / Tx Hz.
5. **Enable TX**, then use macros (CQ, Tx1…).
6. **Log QSO** when the exchange is complete.

## Notes

- This is an operator-console first cut, not a full FT8 LDPC decoder like WSJT-X/JTDX.
- Simulated decodes keep the UI usable without a radio. Real audio still drives the waterfall.
- TX tones are FT8-*like* 8-FSK for audio monitoring; they are not full channel-coded FT8.

## Tests

```powershell
dotnet test
```
