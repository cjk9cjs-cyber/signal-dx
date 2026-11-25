# Signal DX — How to get started

Operator guide for first-time setup and day-to-day use. Signal DX is a Windows FT8 / FT4 / WSPR-2 digi console with waterfall, decode, auto-sequence, CAT, and logging.

Latest Windows installer: [GitHub Releases](https://github.com/cjk9cjs-cyber/signal-dx/releases)

**PDF download:** [SignalDx-HOWTO.pdf](https://github.com/cjk9cjs-cyber/signal-dx/releases/latest/download/SignalDx-HOWTO.pdf)  
In the app: **How-to** (toolbar) or **Settings → About**.

---

## 1. Install

1. Download `SignalDx-<version>-Setup.exe` from the latest release.
2. Run the installer (admin). It places Signal DX under Program Files and can add a Start Menu / desktop shortcut.
3. The Setup is **self-contained** (includes .NET 8). It also bundles:
   - **JTDX** `jtdxjt9` — FT8/FT4 decode (`Tools\JTDX`)
   - **WSJT-X** `ft8code` — FT8/FT4 TX encode (`Tools\WSJT`)
   - **Hamlib** `rigctld` — CAT (`Tools\Hamlib`)
4. **Ham Radio Deluxe** is optional and not bundled. Use it only if you already run HRD Rig Control.

Settings and logs live under `%LocalAppData%\SignalDx\` (survives uninstall of the program files).

---

## 2. First-time setup (checklist)

Do these in order before you go on the air.

### A. Station identity

1. Open Signal DX.
2. On the main toolbar set **My Call** and **Grid** (4- or 6-character Maidenhead). Use the 6-character grid if you know it: a 4-character grid covers about 70 × 70 km and can span two states, so your own state label and TX rows can come out wrong.
3. Optionally open **Settings** → confirm call/grid, then **Save**.

### B. Audio (RX and TX)

1. Open **Settings → Audio**.
2. Choose **Input** = radio audio into the PC (USB codec, VAC, or mic).
3. Choose **Output** = audio back to the radio (same interface TX path). Use **Refresh** if a device was just plugged in.
4. Click **Monitor**. Watch the RX level bar — aim for roughly mid-scale (not pegged).
5. Adjust radio AF / VAC / Windows recording level until the bar is healthy.

### C. Decoder (FT8/FT4)

1. **Settings → Decoder**.
2. Use **Find** if the `jtdxjt9` path is empty — the installer usually points at `Tools\JTDX\jtdxjt9.exe`.
3. Depth `1`–`3` (higher = deeper / slower). Start with `3`.
4. Keep **Monitor** running on the main window.
5. After a few UTC slots, **Band Activity** should fill with decodes.

If Band Activity stays empty: confirm Monitor is on, Settings → Audio input is correct, and the `jtdxjt9` path is valid.

### D. CAT (radio control)

Pick one backend.

#### Hamlib (most radios)

1. **Settings → Hamlib / CAT**.
2. Backend **Hamlib**. **Find** `rigctld` (bundled under `Tools\Hamlib` or your own install).
3. **Reload models**, filter (e.g. `7300`, `FT-991`), select your radio.
4. Set **Device** (COM port with friendly name) and baud.
5. Check **Auto-start rigctld** (recommended) → **Start rigctld** → **Connect**.
6. Confirm status bar shows dial frequency and digi mode (**DATA-U** / similar). Signal DX prefers digi USB-DATA, not plain USB.

#### Ham Radio Deluxe

1. Start **HRD Rig Control** and connect the radio there first.
2. Settings → model filter `HRD` / `Deluxe` → select **Ham Radio Deluxe**.
3. Host `localhost`, HRD port `7809` → **Connect** (do not start rigctld).

Useful CAT options:

- **PTT on TX** — key the radio when Signal DX transmits.
- Main window **Drive %** — PC playback level into the radio. On a direct Hamlib connection it does not change the radio mic gain. Every TX uses the exact Drive value (no hidden minimum), and keying TX does not reset the radio's own USB/DATA level, so a level you set for clean ALC holds from one transmission to the next.

### E. Band and mode

1. Set **Mode** to **FT8** (or FT4 / WSPR2).
2. Pick a **band preset** from the dropdown (sets dial and pushes frequency/mode immediately when CAT is connected).
3. Click the waterfall to set **Rx** / **Tx** audio Hz (orange = Tx, cyan = Rx). **Lock Tx=Rx** keeps them together.

### F. Logging (optional but recommended)

**Settings → Logbooks:**

| Option | Purpose |
|--------|---------|
| **UDP logging** | WSJT-X-compatible UDP (default `127.0.0.1:2237`) for Log4OM, Logger32, Cloudlog, etc. |
| **ADIF append** | Append each logged QSO to a `.adi` file |
| **PSK Reporter** | Spot decoded stations (FT8/FT4/WSPR) |
| **WSPRnet** | Upload WSPR spots when in WSPR-2 (`Upload spots` on the WSPR toolbar) |
| **Club Log / QRZ** | Upload on **Log QSO**; QRZ can also look up DX when you select activity |

Local QSO history is stored under `%LocalAppData%\SignalDx\`. Use **Export ADIF** anytime for a full file.

### G. Wanted list (optional)

**Settings → Wanted:** add callsigns, US/CA states (e.g. `IL`), or countries (e.g. `Japan`). Matching rows highlight **red** in Band Activity / RX Frequency. Enable **Wanted alert** (toolbar + Settings) for audio when they CQ / decode.

### H. Ready to transmit

1. **Enable TX** starts **off** every launch (safe). Turn it on only when you intend to transmit.
2. Set **TX even** to match the period you want (even or odd 15 s FT8 slots).
3. Use **CQ** or double-click a station in Band Activity / RX Frequency.
4. **Auto seq** advances CQ → report → R+report → RR73.
5. **Halt Tx** stops audio/PTT immediately. **Tune** sends a tone at Tx Hz (max 60 s) for SWR/ALC.

---

## 3. Main window tour

| Area | What it does |
|------|----------------|
| **UTC / dial / levels** | Clock, RF = dial + audio Hz, RX level, ALC / Drive |
| **My Call / Grid / Mode / Band** | Station and operating context |
| **DX / Grid / Rpt / Phase** | Working station and auto-seq phase |
| **Monitor / Enable TX / Halt / Tune** | RX decode loop and TX arming |
| **Auto seq / Call alert / Wanted alert / TX even** | Behavior toggles |
| **Settings → Audio** | Input and output sound devices |
| **Waterfall** | Live spectrum; click to set Rx/Tx; right-click / Shift for Tx-only variants |
| **Band Activity** | All decodes (SNR, DT, Hz, distance, flag, country, state, message). **Clear** empties this list only |
| **RX Frequency** | Decodes near your Rx/Tx markers and traffic to you |
| **TX Macros** | Standard / custom macros and free-text; **Apply** loads a typed DX call |
| **Status bar** | CAT, TX/RX state, decode counts, Wanted hits |

Double-click a Band Activity or RX row to load that station and arm TX (Enable TX turns on).

Row colors in Band Activity and RX Frequency: **yellow** = answering or calling you, **green** = your own transmissions and the station you are in a QSO with right now (it goes back to normal when the QSO ends or you reset the sequencer), **red** = Wanted, muted and struck through = already logged.

---

## 4. Features by topic

### Modes

| Mode | Slot | Notes |
|------|------|--------|
| **FT8** | 15 s | Default DX / ragchew digi |
| **FT4** | 7.5 s | Faster contest-style slots |
| **WSPR2** | 120 s | Beacon mode; separate toolbar (Tx next, Tx %, power dBm, Upload spots) |

FT8/FT4 and WSPR keep **separate Tx/Rx audio markers**. Switching modes restores the markers for that mode; restart restores them from settings.

Leaving WSPR turns **Enable TX** off and clears beacon free-text so FT8 does not key CQ by itself.

### Waterfall

- FT8/FT4: ~200–3000 Hz, solid background with smoothed signal traces drawn on top, 15 s (or 7.5 s) time marks.
- The strip on the right matches the WSJT-X wide graph: **Start** (left edge, Hz), **Bins/Pixel** (4 = whole passband, 1 = narrowest zoom), **N Avg**, **Flatten**, **Gain** (0–80; higher brightens traces, 40 stays colored, 80 can reach white), and **Zero** (higher = hide weaker traces). Palette stays on the toolbar as **Colors**.
- WSPR-2: narrow 1400–1600 Hz passband, soft weak-signal traces, 2-minute marks.
- Palettes under **Colors** / Settings → Display.
- FT8/FT4 call signs appear along the bottom of the waterfall at each decode's frequency, for the last two decode slots (the older slot is dimmer). Wanted calls (call, state, or country) get a red background with white text. The station you are calling in the current QSO gets a green background with white text. A station that only called you, or a QSO that never started, stays the normal color. Turn off under Settings → Display → **Show call signs on the waterfall**.
- Markers persist across mode changes and app restarts.

### Auto sequence

With **Auto seq** on:

1. **CQ** (or Start CQ) holds calling until someone answers with a grid.
2. Replies advance macros automatically (report → R+report → 73/RR73).
3. **Log QSO** is offered on the first 73 / RR73.
4. **Reset seq** (in the RX Frequency header, FT8/FT4 only) clears the QSO state back to idle.

Double-click remains a manual override to work a specific station.

### Country and state

- **Country** comes from the callsign prefix (built in, offline).
- **US state** comes from the station's grid square. Signal DX uses the 6-character grid from the bundled `CALL3.TXT` database when that station has one, then the grid in the decoded message. Near state lines a grid can still be labeled as the neighboring state.
- Both data files are bundled with the app and updated by hand, so they only change when a new Signal DX build ships.

### Alerts

- **Call alert** — built-in 2-tone chime when someone calls *your* callsign.
- **Wanted alert** — distinct built-in 3-note bell chime when a Wanted call / state / country appears (CQ-only option in Settings); matching rows highlight **red**.
- Custom sound files (`.wav`, `.mp3`, `.wma`, `.m4a`) with **Browse…**, **Test**, and **Reset** in **Settings → Display** (Call alert) and **Settings → Wanted** (Wanted alert).

### Checking for updates

- Signal DX checks **GitHub Releases** automatically a few seconds after startup (toggleable under **Settings → About → Check for updates automatically on startup**).
- When a newer version is published, a green **Update &lt;version&gt;** button appears in the top toolbar and the release notes appear in **Settings → About**.
- Click the toolbar badge or **Download & install update** in **Settings → About** to download the Windows Setup installer, verify its SHA-256 checksum, close Signal DX cleanly (saving settings and unkeying CAT PTT), and launch the installer.
- Installing an update is blocked while transmitting or tuning so an active transmission is never interrupted. You can also check manually anytime with **Check for updates**.

### WSPR-2

1. Mode **WSPR2** → Apply a WSPR band preset.
2. Set **Tx %** (e.g. 20–25) and audio **Tx Hz** (avoid a crowded 1500 Hz if busy).
3. **Enable TX** to allow scheduled beacons; **Tx next** forces the next 2-minute slot once.
4. **Upload spots** / PSK Reporter send heard beacons when configured.
5. Return to FT8/FT4 when finished — beacon state is cleared and Enable TX is turned off.

### CAT and digi mode

Signal DX keeps the radio in a digi USB-DATA mode (e.g. DATA-U) when changing band/frequency so the rig does not fall back to plain USB. Use **Set mode** / **Set dial → rig** in Settings if you need a manual push.

### Logging bridges

On **Log QSO**, Signal DX can:

- Append ADIF
- Emit WSJT-X UDP (heartbeat + logged QSO / ADIF packets)
- Upload Club Log / QRZ when enabled
- Keep a local JSON log and worked-calls list for Band Activity highlighting

---

## 5. Typical on-air session

1. Start Signal DX → set call/grid if needed.
2. **Monitor** on → correct Input/Output under **Settings → Audio**.
3. CAT **Connect** (if used) → pick your **band preset**.
4. Confirm decodes in Band Activity.
5. Click waterfall for a clear Tx Hz.
6. **Enable TX** → click a **CQ** macro (in TX Macros) or double-click a caller.
7. Let **Auto seq** finish; log when prompted (or **Log QSO**).
8. **Enable TX** off (or close the app) when done — TX is never restored armed on next launch.

---

## 6. Troubleshooting

| Symptom | Try this |
|---------|----------|
| No decodes | Monitor on; correct Input device (Settings → Audio); JTDX path; wait for a full slot |
| TX but radio silent | Out device; Drive %; radio USB/DATA input; PTT on TX; CAT connected |
| Wrong sideband / no digi | CAT digi mode (DATA-U / PKTUSB); re-pick band preset; Settings → Set mode |
| Enable TX was on at start | Fixed in 2.12.1+ — TX is session-only and starts off |
| Markers jump after WSPR | FT8 and WSPR markers are stored separately (2.10+); set once per mode |
| State label looks wrong | Set your Grid to 6 characters; state lines can fall inside a 4-character square |
| WSPR then FT8 keys CQ alone | Leaving WSPR disables Enable TX (2.12+); turn Enable TX on only when ready |
| CAT won’t connect | Correct COM/baud; Start rigctld; or HRD Rig Control already running for HRD |
| UDP logger quiet | UDP enabled; logger listening on `127.0.0.1:2237`; fire a test **Log QSO** |

---

## 7. Developer notes (optional)

```powershell
dotnet run --project src\SignalDx\SignalDx.csproj
.\scripts\build-installer.ps1
```

See the repository [README](../README.md) for build requirements and Hamlib/HRD detail.

---

## Credits

Signal DX by **CJ Schmit (K9CJS)**. Decode/encode tooling builds on **JTDX Contest**, **WSJT-X**, and related open amateur-radio work — see **Settings → About**.
