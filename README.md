# MonitorToggle

MonitorToggle is a small Windows console application that toggles your display topology between “show only on 1” and “extend these displays.”  When the application runs it checks how many display paths are currently active.  If two or more displays are active (an extended desktop), it attempts to switch to a single–monitor topology (“show only on 1”).  If only one display is active, it switches to an extended desktop.  After toggling, the program restores the secondary monitor’s last known registry mode and position, when available.

## Features

* **Toggle display topology** between single‑display (“show only on 1”) and extended desktop using the Windows **SetDisplayConfig** API.
* **Automatic fallback** to the built‑in `DisplaySwitch.exe` helper when direct API calls fail.
* **Registry mode restore:** after switching to extended mode, the application reads the secondary monitor’s registry‑stored resolution and position and re‑applies it using `ChangeDisplaySettingsEx`.
* **Logging:** each execution appends information about the active paths, actions taken and errors encountered to a log file (`MonitorToggle.log`) stored alongside the executable.

## Requirements

* Windows 10 or later.
* [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) installed to build the project.

## Building

1. Clone the repository or download the source.
2. Open a command prompt in the repository root.
3. Run `dotnet build` to compile the project.  The output executable will be placed in `bin/Release/net6.0‑windows` when built in Release configuration.

```
dotnet build -c Release
```

## Usage

After building the application, run the resulting executable from a command prompt.  When executed, the application will:

1. Query the number of active display paths.  If there are **two or more**, it attempts to switch to a single‑monitor topology (equivalent to choosing **Show only on 1** in the Windows display settings).
2. If there is **only one active display**, it attempts to switch to an extended desktop (equivalent to choosing **Extend these displays**).
3. When switching to an extended desktop, it reads the registry‑stored display mode for the secondary monitor and applies it to restore the previous resolution and position.

If the direct API call fails, the application falls back to invoking `DisplaySwitch.exe` with the appropriate argument.

Each run appends diagnostic output to `MonitorToggle.log`.  If you encounter errors, consult this log for details.

## Icon

The project includes an icon file (`MonitorToggleIcon.ico`) that is embedded into the executable.  When the project is built, the compiled binary will display this icon.

## License

This project is provided as‑is without warranty.  You are free to use, modify, and distribute it under the [MIT License](LICENSE).
