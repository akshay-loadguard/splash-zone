# Splash Zone

A switch-accessible water balloon game. The screen is split into zones; one switch moves the hand across zones, the other throws a water balloon at the person. Hit them and they get soaked; miss and they laugh at you.

## Play

- **In the browser:** https://akshay-loadguard.github.io/splash-zone/
- **Windows app (for networks that block the website):** [Download SplashZone.exe](https://github.com/akshay-loadguard/splash-zone/releases/latest/download/SplashZone.exe) · [all releases](https://github.com/akshay-loadguard/splash-zone/releases)

## Controls
- **Switch 1 / ↑ / Space** – throw
- **Switch 2 / →** – move the hand to the next zone (← moves back)
- Click/tap a zone to throw there
- **F11** (Windows app) – full screen

## Switch interface (CSI)
Works with serial switch interfaces (USB or Bluetooth COM port).
Click **Connect CSI**, pick the port, then **Teach switches** and press each switch once.
The website needs Chrome or Edge (Web Serial). The Windows app talks to the COM port directly.

## About the Windows app
- One file, no installer, no admin rights. Runs as a normal user.
- Makes no internet connections: the game is built into the exe and the window is locked to it.
- Saves settings only in `%LOCALAPPDATA%\SplashZone`. To remove it, delete the exe and that folder.
- Needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11 and up-to-date Windows 10.
- Built from the source in [`desktop/`](desktop) with .NET 8 (`dotnet publish -c Release`). Every release lists the file's SHA-256 so you can check your download:
  ```powershell
  Get-FileHash .\SplashZone.exe -Algorithm SHA256
  ```

**"Windows protected your PC"?** The exe is not yet code-signed, so Microsoft SmartScreen shows this for new downloads. Click **More info → Run anyway**, or ask IT to allow it by its SHA-256 hash.
