# CapDock

A minimalist, open-source dock for Windows. Free for everyone.

CapDock is a lightweight capsule-style dock that sits on your desktop. It does not install anything, does not require payment, and stays out of your way. Just put your shortcuts in a folder and use them from the dock.

## Features

- Minimalist design: one capsule, no visual clutter.
- Live background: blur or acrylic effect, similar to Windows 10.
- Auto-hide: slides off the screen edge and comes back when you need it.
- Drag and drop: rearrange shortcuts, drop files directly into the dock.
- Flexible settings: size, colours, shortcut folder, animations, and more.
- Multiple profiles: each profile is a separate dock with its own settings and folder.

## Requirements

- Windows 10 version 2004 or newer (for live background features).
- .NET SDK (for building from source). The published executable is self-contained and does not require .NET to be installed on the target machine.

## How to run

1. Download the latest `CapDock.exe` from the [Releases](https://github.com/johnhancharou/CapDock/releases) page.
2. Run the executable. The program will appear in the system tray.
3. Right-click the tray icon, open Settings, and choose the folder with your shortcuts.
4. The dock will appear on your screen.

## How to build from source

You need the source files, including the project file (`.csproj`). Place all source files in a single folder.

1. Open a command prompt in that folder.
2. Run the following command:

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish
```

3. After the command finishes, the `publish` folder will contain `CapDock.exe`. This is a single, self-contained executable that you can run on any compatible Windows machine.

The folder itself stays on your disk.

## License

MIT No Attribution (MIT-0). Do whatever you want. No attribution required.
