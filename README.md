# Signal DX

Windows digi operator console inspired by JTDX — waterfall, FT8/FT4/WSPR-2 slot timing, auto-sequence, CAT control, and external logbook bridges.

**[Download Signal DX for Windows (SignalDx-Setup.exe)](https://github.com/cjk9cjs-cyber/signal-dx/releases/latest/download/SignalDx-Setup.exe)** — self-contained installer, no .NET install needed. [All releases](https://github.com/cjk9cjs-cyber/signal-dx/releases)

**Operator guide (first-time setup + features):** [docs/HOWTO.md](docs/HOWTO.md) · [PDF download](https://github.com/cjk9cjs-cyber/signal-dx/releases/latest/download/SignalDx-HOWTO.pdf)

In the app: toolbar **How-to**, or **Settings → About**.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (developers only — the installer is self-contained)
- **Installed build** ships JTDX decode, WSJT-X `ft8code`, and Hamlib CAT tools under `Tools\`
- Optional: [Ham Radio Deluxe](https://www.hamradiodeluxe.com/) Rig Control if you prefer HRD CAT instead of Hamlib
- Optional: Log4OM / Logger32 / Cloudlog bridge listening for WSJT-X UDP

## Run

```powershell
dotnet run --project src\SignalDx\SignalDx.csproj
```

## Windows installer

Build a self-contained `Setup.exe` that includes:

- Signal DX + .NET 8 runtime
- **JTDX** `jtdxjt9` (decode) under `Tools\JTDX`
- **WSJT-X** `ft8code` (TX encode) under `Tools\WSJT`
- **Hamlib** `rigctld` (CAT) under `Tools\Hamlib`

HRD is **not** bundled (commercial). Use Settings → CAT → Ham Radio Deluxe if you already have HRD Rig Control.

```powershell
.\scripts\build-installer.ps1
```

Build machine needs local JTDX Contest, WSJT-X, and Hamlib installs so `scripts\stage-bundled-tools.ps1` can copy them. Also requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install --id JRSoftware.InnoSetup -e`).

Output: `artifacts\installer\SignalDx-<version>-Setup.exe`

## Features

| Area | Behavior |
|------|----------|
| Waterfall | Live spectrum from mic / VAC / radio audio |
| Decode | JTDX `jtdxjt9` engine on each UTC slot |
| Modes | FT8 (15 s) and FT4 (7.5 s) slot clock |
| Band presets | Common FT8/FT4 dial frequencies |
| Auto sequence | CQ → report → R+report → RR73 workflow |
| Band / Rx panes | Full activity plus Rx-frequency filter |
| Wanted | Callsign, US/CA state, or country watch list; matches play a distinct audio alert (or custom sound) and highlight the row in red in Band Activity / RX Frequency |
| Updates | Automatic GitHub Releases check on startup (toggleable), toolbar update badge, and SHA-256 verified one-click Windows installer in **Settings → About** |
| CAT | Hamlib TCP (`rigctld`), Kenwood serial, Yaesu serial; dial + PTT |
| Local log | JSON under `%LocalAppData%\SignalDx\` |
| ADIF | Append file + full export dialog |
| WSJT-X UDP | Heartbeat, QSO Logged (5), Logged ADIF (12) for external loggers |
| Club Log / QRZ | Optional realtime / API upload on Log QSO |

## Settings

Open **Settings** from the main window for:

- **Hamlib / CAT** — model, device, `rigctld`, connect/PTT/mode/freq
- **Logbooks** — WSJT-X UDP, ADIF path, Club Log, QRZ
- **Decoder** — path to `jtdxjt9.exe` and decode depth (1–3)
- **Wanted** — callsigns, US/CA states (e.g. `IL`), or countries (e.g. `Japan`). Wanted alert plays a distinct chime (or custom audio file via Browse/Test/Reset; CQ-only by default). Matching decode rows are highlighted in red in Band Activity and RX Frequency.
- **About** — version info, bundled how-to guide, and **Updates** (startup check toggle, **Check for updates**, release notes, and one-click verified installer download).

The main console keeps a compact CAT status (freq / PTT) in the status bar. Use **Find** in Settings → Decoder if `jtdxjt9.exe` isn't located automatically, then **Monitor** so each slot buffers audio for the decoder.

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

- Real decode shells out to JTDX `jtdxjt9` with a 12 kHz WAV of the last slot (working directory = JTDX `bin` for DLLs).
- TX audio is FT8-like 8-FSK for monitoring — not a full LDPC channel encoder/decoder.
- Serial CAT dialects vary by radio; Hamlib is the most reliable path for uncommon models.

## Tests

```powershell
dotnet test
```
