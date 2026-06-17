<div align="center">

<img src="Steady/Steady/Assets/steady_logo.png" width="120" alt="Steady logo" />

# Steady

### Focal Point for Windows

Reduce **motion sickness** while using a laptop/tablet in a moving vehicle — it shows *focal-point cues* at the edges of your screen that move with the vehicle's real motion.

Inspired by iOS 18 *Vehicle Motion Cues*, but redesigned to work using **on-device sensors only** — no phone or GPS required.

![version](https://img.shields.io/badge/version-1.0.0-2E63FF)
![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![lang](https://img.shields.io/badge/C%23-WPF-239120)

</div>

---

## Why Steady

Motion sickness comes from a **sensory conflict** — your inner ear knows the vehicle is moving, but your eyes fixed on a still screen tell your brain you're stationary. Steady adds *visible motion cues* in your peripheral vision that match what your body feels, without covering the main content on screen.

## ✨ Highlights

- **Edge-of-screen focal cues** — a transparent, click-through overlay that doesn't get in the way
- **Two-tier automatic sensor selection** — gyro (Tier 1) or a camera tracking head movement (Tier 2)
- **Customizable** — dot density, size, transparency, intensity + colors that auto-contrast against the background
- **Highly battery-friendly** — lowers FPS when still, releases the camera after long pauses, camera runs only while active
- **Private** — all image/audio processing happens on-device; nothing is recorded or sent out
- **Light and unobtrusive** — runs in the system tray, toggle with `Ctrl+Alt+M`, double-click to run instantly

## 🧭 How It Works

The app detects the vehicle's motion and shifts a "dot field" along the screen edges in sync with the real acceleration vector (accelerate → down, brake → up, turn left → right). Devices with a gyro use it directly, while ordinary laptops use the camera to track head movement instead (which reflects the actual G-forces your body feels).

## 🚀 Getting Started

Requires **Windows 10 22H2+**, **.NET 8 SDK**, **x64** architecture

```powershell
# build + run
dotnet build "Steady\Steady.sln" -c Release
dotnet run --project "Steady\Steady\Steady.csproj"
```

The app lives in the system tray — double-click the icon, or press `Ctrl+Alt+M` to toggle it on/off.

> 📖 Full details (architecture, portable publishing, settings, dot behavior): see the **[full README](Steady/Steady/README.md)**

## 📂 Project Links

| File | Description |
|------|-------------|
| [Full README](Steady/Steady/README.md) | build/run/publish guide + architecture |
| [CHANGELOG](Steady/Steady/CHANGELOG.md) | version history (starting at v1.0.0) |
| [Feature Spec](docs/Steady_Feature_Spec.docx) | detailed feature specification document |

## 🗺️ Status

**v1.0.0** — production-ready (overlay + 2-tier sensor + customization + battery saving)
See the plan for the next version in the [CHANGELOG](Steady/Steady/CHANGELOG.md)

---

<div align="center">
<sub>Steady — your focal point on the road · "Stay focused, skip the motion sickness"</sub>
</div>
