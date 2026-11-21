# Signal DX

Windows digi operator console inspired by JTDX — waterfall, FT8/FT4 slot timing, auto-sequence, CAT control, and external logbook bridges.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Hamlib](https://github.com/Hamlib/Hamlib) `rigctld` for TCP CAT
- Optional: [JTDX](https://sourceforge.net/projects/jtdx/) (`jtdxjt9.exe`) for real FT8/FT4 decode — contest/newer builds that support `-8`/`-4` file mode (e.g. `C:\JTDX_contest\bin\jtdxjt9.exe`)
- Optional: Log4OM / Logger32 / HRD / Cloudlog bridge listening for WSJT-X UDP

## Run

```powershell
dotnet run --project src\SignalDx\SignalDx.csproj
```

## Windows installer

Build a self-contained `Setup.exe` (bundles .NET 8, no separate runtime install):

```powershell
.\scripts\build-installer.ps1
```

Output: `artifacts\installer\SignalDx-<version>-Setup.exe`

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install --id JRSoftware.InnoSetup -e`). Use `-FrameworkDependent` for a smaller package that needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## Features

| Area | Behavior |
|------|----------|
| Waterfall | Live spectrum from mic / VAC / radio audio |
| Decode | JTDX `jtdxjt9` engine on each UTC slot (or Simulate) |
| Modes | FT8 (15 s) and FT4 (7.5 s) slot clock |
| Band presets | Common FT8/FT4 dial frequencies |
| Auto sequence | CQ → report → R+report → RR73 workflow |
| Band / Rx panes | Full activity plus Rx-frequency filter |
| CAT | Hamlib TCP (`rigctld`), Kenwood serial, Yaesu serial; dial + PTT |
| Local log | JSON under `%LocalAppData%\SignalDx\` |
| ADIF | Append file + full export dialog |
| WSJT-X UDP | Heartbeat, QSO Logged (5), Logged ADIF (12) for external loggers |
| Club Log / QRZ | Optional realtime / API upload on Log QSO |

## Settings

Open **Settings** from the main window for:

- **Hamlib / CAT** — model, device, `rigctld`, connect/PTT/mode/freq
- **Logbooks** — WSJT-X UDP, ADIF path, Club Log, QRZ
- **Decoder** — Simulate vs JTDX, path to `jtdxjt9.exe`, decode depth

The main console keeps a compact CAT status (freq / PTT) in the status bar. Uncheck **Simulate** (or set Engine to JTDX) after **Find** locates `jtdxjt9.exe`, then **Start Monitor** so each slot buffers audio for the decoder.

### Hamlib

Signal DX loads the **full** Hamlib radio list from your installed `rigctl -l` (300+ models), with a bundled catalog as fallback. COM ports show Windows friendly names (e.g. Silicon Labs USB bridges).

1. Install [Hamlib](https://github.com/Hamlib/Hamlib/releases) (e.g. `hamlib-w32-4.x`)
2. Settings → **Find** `rigctld` → **Reload models**
3. Filter models (e.g. `7300`, `Icom`, `FT-991`), pick device, baud
4. **Start rigctld** (or **Auto-start**) → **Connect**

```powershell
rigctld -m 3073 -r COM7 -s 19200 -t 4532
```

### Ham Radio Deluxe

HRD is **not** a Hamlib radio — Signal DX adds it as virtual model **9999** (`Ham Radio Deluxe HRD Rig Control`).

1. Start **HRD Rig Control** and connect your radio there  
2. Settings → model filter `Deluxe` / `HRD` → select it  
3. Host `localhost`, HRD port `7809` → **Connect** (do not start rigctld)

### External logbooks

On **Log QSO**, Signal DX can append ADIF, broadcast WSJT-X UDP (`127.0.0.1:2237`), and optionally upload to Club Log / QRZ.

## Notes

- Simulated decodes keep the operator loop usable without RF or JTDX.
- Real decode shells out to JTDX `jtdxjt9` with a 12 kHz WAV of the last slot (working directory = JTDX `bin` for DLLs).
- TX audio is FT8-like 8-FSK for monitoring — not a full LDPC channel encoder/decoder.
- Serial CAT dialects vary by radio; Hamlib is the most reliable path for uncommon models.

## Tests

```powershell
dotnet test
```
