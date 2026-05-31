# Vintage Story Mod Updater

Vintage Story Mod Updater is a desktop app for keeping your Vintage Story mods current. It checks the mods you already have installed, compares them with the official [Vintage Story ModDB](https://mods.vintagestory.at/), and helps you update only to versions that are compatible with your installed Vintage Story version.

The app is built for Windows, Linux, and macOS.

## What It Does

- Finds your Vintage Story installation when it can, then reads the installed game version.
- Finds your Vintage Story `Mods` directory, or lets you choose it manually.
- Shows installed mods, their current versions, and whether a compatible update is available.
- Checks the official Vintage Story ModDB only.
- Creates a backup before every mod update.
- Lets you restore a previously backed-up mod version.

## Download And Install

When releases are available, download the latest version from the project's GitHub Releases page:

[https://github.com/aaymont/vintage-mod-updater/releases](https://github.com/aaymont/vintage-mod-updater/releases)

1. Open the GitHub repository.
2. Select **Releases**.
3. Download the file for your operating system:
   - Windows: `vintage-mod-updater-win-x64.zip`
   - Linux: `vintage-mod-updater-linux-x64.tar.gz`
   - macOS Intel: `vintage-mod-updater-macos-x64.tar.gz`
   - macOS Apple Silicon: `vintage-mod-updater-macos-arm64.tar.gz`
4. Extract the downloaded file.
5. Run the app from the extracted folder:
   - Windows: open `VintageModUpdater.App.exe`.
   - Linux: run `./VintageModUpdater.App`.
   - macOS: run `./VintageModUpdater.App`.

On first launch, the app will try to find Vintage Story and your mods folder. If it cannot find them, choose:

- **Game Installation Directory**: the folder where Vintage Story is installed.
- **Mods Directory**: the folder where your Vintage Story mods are stored.

## Using The App

1. Open Vintage Story Mod Updater.
2. Confirm the game installation and mods directories.
3. Select **Scan** to load installed mods.
4. Select **Check Updates** to compare your mods with the official ModDB.
5. Select **Update** for one mod, or **Update All** for every compatible update.
6. Use the **Backups** tab to restore a previous mod version.

The updater creates backups automatically before replacing files. Backup files are stored inside the mods folder under `.vintage-mod-updater/backups`.

## Where Vintage Story Stores Mods

The default mods folder depends on your operating system. These paths follow the [Vintage Story wiki](https://wiki.vintagestory.at/index.php?title=Adding_mods).

| System | Default mods directory |
| --- | --- |
| Windows | `%APPDATA%\VintagestoryData\Mods` |
| Linux | `~/.config/VintagestoryData/Mods` |
| macOS | `~/Library/Application Support/VintagestoryData/Mods` |

## Build From Source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Clone the repository:

```powershell
git clone https://github.com/aaymont/vintage-mod-updater.git
cd vintage-mod-updater
```

Restore packages:

```powershell
dotnet restore vintage-mod-updater.sln --configfile NuGet.config
```

Run the app:

```powershell
dotnet run --project src\VintageModUpdater.App\VintageModUpdater.App.csproj
```

On Linux or macOS shells, use forward slashes:

```bash
dotnet run --project src/VintageModUpdater.App/VintageModUpdater.App.csproj
```

## Compile For Windows, Linux, Or macOS

These commands produce self-contained builds. A self-contained build includes the .NET runtime, so users do not need to install .NET separately.

Windows x64:

```powershell
dotnet publish src\VintageModUpdater.App\VintageModUpdater.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
```

Linux x64:

```bash
dotnet publish src/VintageModUpdater.App/VintageModUpdater.App.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64
```

macOS Intel:

```bash
dotnet publish src/VintageModUpdater.App/VintageModUpdater.App.csproj -c Release -r osx-x64 --self-contained true -o publish/osx-x64
```

macOS Apple Silicon:

```bash
dotnet publish src/VintageModUpdater.App/VintageModUpdater.App.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
```

The executable is inside the output folder. On Linux and macOS, you may need to mark it executable:

```bash
chmod +x VintageModUpdater.App
```

macOS may warn that the app is from an unidentified developer until the project has signed and notarized builds.

## Security

Security controls, known limitations, and vulnerability reporting guidance are documented in [`Security.md`](Security.md).

## License

Copyright (C) 2026 Adrian Aymont

This project is licensed under the [MIT License](LICENSE).

## Project Notes

- The updater talks to the official [Vintage Story ModDB API](https://github.com/anegostudios/vsmoddb).
- The desktop UI is built with [Avalonia](https://avaloniaui.net/).
- The core update logic is separate from the desktop app so a command-line tool can be added later.
