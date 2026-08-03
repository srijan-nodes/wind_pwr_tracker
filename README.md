# PowerTracker

A lightweight, zero-dependency Windows desktop widget and system tray utility that displays real-time battery power telemetry (**Power In**, **Power Out**, and **Net Flow** in Watts).

Designed specifically for monitoring charger performance, USB-C PD negotiations, and dGPU power states without waking up discrete graphics cards.

---

## Features

* **Ultra Lightweight:** Natively compiled C# application for Windows .NET Framework.
* **Low Resource Usage:** Uses `< 15 MB` RAM and `0.0%` idle CPU with zero continuous RAM telemetry accumulation.
* **GPU-Safe Telemetry:** Queries Windows WMI counters directly—won't pull discrete GPUs out of low-power sleep ($D3cold$).
* **Modern Floating HUD:** Translucent, borderless, always-on-top widget featuring stacked **`● REC`** (toggle recording) and **`📈 GRAPH`** (open live vector graph) buttons.
* **Interactive Vector Graphing Window:** Built-in vector charting window with real-time streaming telemetry, point-and-read crosshairs, instant hover value readouts, box selection zoom & pan, series toggles (`Power IN`, `Power OUT`, `Net Flow`), and CSV export capabilities.
* **Standalone CSV Graph Viewer (`PowerGraphViewer.exe`):** Dedicated program for loading, viewing, and analyzing any saved `PowerData_*.csv` file as an interactive graph with drag-and-drop support.
* **Dynamic Local Storage:** Automatically creates and saves CSV session logs in `<App_Directory>\Charging Stats` relative to where the application is executed.
* **System Tray Integration:** Runs in the background tray icon area with real-time hover tooltips and quick context menu actions.

---

## Building & Installation

No Visual Studio installation required! PowerTracker uses the built-in C# compiler (`csc.exe`) included with Windows.

1. Clone or download `PowerTracker.cs` and `PowerGraphViewer.cs`.
2. Open **Command Prompt** in the folder containing the source files.
3. Run the compilation commands:

```cmd
:: 1. Compile Main PowerTracker Widget
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /r:System.Management.dll /r:System.Windows.Forms.DataVisualization.dll /out:PowerTracker.exe PowerTracker.cs

:: 2. Compile Standalone CSV Graph Viewer
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /r:System.Windows.Forms.DataVisualization.dll /out:PowerGraphViewer.exe PowerGraphViewer.cs
```