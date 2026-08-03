# PowerTracker

A lightweight, zero-dependency Windows desktop widget and system tray utility that displays real-time battery power telemetry (**Power In**, **Power Out**, and **Net Flow** in Watts).

Designed specifically for monitoring charger performance, USB-C PD negotiations, and dGPU power states without waking up discrete graphics cards.

---

## Features

* **Ultra Lightweight:** ~12 KB executable compiled natively against the Windows .NET Framework.
* **Low Resource Usage:** Uses `< 15 MB` RAM and `0.0%` idle CPU.
* **GPU-Safe Telemetry:** Queries Windows WMI counters directly—won't pull discrete GPUs out of low-power sleep ($D3cold$).
* **System Tray Integration:** Runs in the background/collapsed tray icons area with real-time hover tooltips.
* **Modern Floating HUD:** Translucent, borderless, always-on-top widget that you can click and drag anywhere on your screen.

---

## Building & Installation

No Visual Studio installation required! PowerTracker uses the built-in C# compiler (`csc.exe`) included with Windows.

1. Clone or download `PowerTracker.cs`.
2. Open **Command Prompt** as Administrator in the folder containing `PowerTracker.cs`.
3. Run the compilation command:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /r:System.Management.dll /out:PowerTracker.exe PowerTracker.cs