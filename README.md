# Colorblind Assistant 2

A lightweight **Windows WPF overlay** that samples a small region of the screen near your cursor, identifies the most representative color(s), and shows readable color labels + contrast guidance. It also includes a **Protanopia (red-blindness) simulation** mode.

Repo: [`marcusreynolds2609-dot/colorblind-assistant2`](https://github.com/marcusreynolds2609-dot/colorblind-assistant2)

## What it does (high-level)
- **Tracks the mouse cursor** and keeps an always-on-top overlay positioned near it.
- **Captures a 5×5 pixel block** around the cursor (nearest-neighbor, no smoothing).
- Uses the **center 3×3 pixels** for analysis (current code uses a sampling radius of 1).
- Computes:
  - **Average color** (mean RGB over samples)
  - **Dominant color cluster(s)** (groups samples into named buckets; picks top 1–2)
  - A **human-friendly label** (e.g., *Dark Blue*, *Pastel Green*, *Brown*, *Gray*)
  - **Text contrast recommendation** (black vs white, with AA/AAA)
- Displays:
  - A color swatch (and optionally a second swatch when it detects two distinct colors)
  - A **5×5 magnifier grid** (when “details” mode is enabled)

## How the 5×5 scan works (implementation notes)
The overlay uses a `DispatcherTimer` (~16ms interval) to call `UpdateColorReadout()` when not frozen/hidden.

1. **Capture**  
   It grabs a **5×5** bitmap around the cursor using `Graphics.CopyFromScreen` into a reused `Bitmap`, then reads raw pixels via `LockBits` into a byte buffer.

2. **Core sampling**  
   The UI magnifier uses all **5×5** points, but the analysis uses only the **3×3 center**:
   - `MagnifierRadius = 2` → 5×5 grid
   - `SamplingRadius = 1` → 3×3 “core”

3. **Dominant colors**  
   Samples are grouped by a “cluster key” derived from HSL heuristics:
   - Very dark → *Black*
   - Very light → *White*
   - Low saturation → *Gray*
   - Special case for browns/tans
   - Otherwise maps to the **closest named color**
   The top two clusters by count are kept.

4. **One vs two color display**  
   If the sample variance is high enough and the top two clusters differ, it shows a combined label like `"<primary> & <secondary>"` and displays a second swatch.

5. **Stabilization**  
   The UI blends the displayed values to reduce flicker, and requires multiple frames before switching labels to a new one.

## Hotkeys
These are registered as **system hotkeys** (work globally while the app is running):

- **Freeze + copy summary**: `Ctrl + Space`  
  Freezes scanning and tries to copy the last scan summary to clipboard.
- **Toggle details**: `Ctrl + Shift + H`  
  Switches between full details vs “color only” mode.
- **Exit**: `Ctrl + Shift + Q`  
  Exits the app.

## Tray icon
The app creates a tray icon with a menu:
- Hide/Show overlay
- Freeze/Resume
- Color Only / Show Details
- Exit

## Vision simulation (Protanopia)
The UI includes a button labeled “Protanopia ✓”. The codebase contains an LMS-based protanopia simulator intended to show a simulated color for accessibility testing.

## Build & run (from source)
This project targets **.NET 10 (Windows)** and WPF:

1. Install **.NET SDK** that supports `net10.0-windows`.
2. From the repo root:

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

The built EXE will be under `bin\Release\<tfm>\`.

## Why it doesn’t appear on the taskbar
The overlay window is configured with `ShowInTaskbar="False"` in `MainWindow.xaml` so it behaves like a true overlay (taskbar-free) and is instead managed via the tray icon + hotkeys.

## Project layout
- `MainWindow.xaml` / `MainWindow.xaml.cs`: overlay UI, hotkeys, capture + analysis loop
- `VisionSimulationPage.xaml`: vision simulation UI
- `ViewModels/VisionSimulationViewModel.cs`: view model for simulation copy/strings and transformation logic

