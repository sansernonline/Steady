# Steady — Focal Point for Windows

ลดอาการเมารถขณะใช้แล็ปท็อปหรือแท็บเล็ตในยานพาหนะ โดยแสดง motion-cue dots ที่ขอบจอ ซิงก์กับการเคลื่อนไหวจริงของรถ

> แรงบันดาลใจจาก iOS 18 Vehicle Motion Cues — redesign ให้ทำงานด้วย sensor บนเครื่องเท่านั้น ไม่ต้องพึ่งโทรศัพท์หรือ GPS

---

## โครงสร้างโปรเจกต์

```
Steady - Focal Point for Windows/             ← repo root
├── doc/
│   └── Steady_Feature_Spec.docx              Feature spec (v0.6)
└── Steady/                                   ← .sln + README + โค้ดทั้งหมด
    ├── Steady.sln                            Solution — เปิดไฟล์นี้ใน Visual Studio
    ├── README.md                             ← เอกสารนี้
    ├── Steady.csproj                         WPF + .NET 8, x64
    ├── App.xaml / App.xaml.cs                Startup, orchestration, single-instance, enable/disable
    │
    ├── Models/
    │   ├── AppSettings.cs                    ทุก setting (spacing, size, opacity, intensity, tier ...)
    │   └── MotionVector.cs                   Motion data model (X/Y/Z + Lerp/Scale/Magnitude)
    │
    ├── Helpers/
    │   └── Win32Helper.cs                    P/Invoke: overlay click-through, RegisterHotKey
    │
    ├── Services/
    │   ├── ISensorService.cs                 Interface ของ sensor ทุกตัว
    │   ├── GyroSensorService.cs              Tier 1 — Windows.Devices.Sensors (IMU)
    │   ├── CameraHeadTrackingService.cs      Tier 2 — OpenCV head-tracking (+ ROI, adaptive FPS, deep-idle)
    │   ├── OpticalFlowService.cs             Tier 2 — optical flow (เสริม)
    │   ├── MicSensorService.cs               Context — ประเมินความเร็วจากเสียง
    │   ├── SensorManager.cs                  เลือก tier + fallback + auto-activation (+ allowCamera gate)
    │   ├── PowerMonitorService.cs            ตรวจสถานะแบต → สั่ง battery-saver
    │   ├── HotkeyService.cs                  Global hotkey Ctrl+Alt+M (Win32)
    │   ├── TrayService.cs                    System tray icon + context menu
    │   └── SettingsService.cs                Load/save JSON → %AppData%\Steady\settings.json
    │
    ├── Views/
    │   ├── OverlayWindow.xaml/.cs            Overlay โปร่งใส click-through, density-based dots
    │   └── SettingsWindow.xaml/.cs           Dark-theme settings UI
    │
    └── Assets/
        ├── steady.ico / steady_off.ico      Tray + application icon (active / disabled)
        ├── steady_logo.png                  โลโก้ 256px
        ├── Package.appxmanifest             MSIX manifest (webcam + microphone capabilities)
        └── haarcascade_frontalface_default.xml   Cascade สำหรับ Tier 2 (ดู Setup)
```

---

## สถาปัตยกรรม

```
App.xaml.cs (orchestrator)
    │
    ├── SensorManager ──► GyroSensorService          (Tier 1: IMU)
    │                 ├──► CameraHeadTrackingService  (Tier 2: OpenCV head-tracking)
    │                 ├──► OpticalFlowService         (Tier 2: optical flow)
    │                 └──► MicSensorService           (context: speed จากเสียง)
    │                          │
    │                     MotionVector event
    │                          │
    ├── OverlayWindow ◄────────┘   (WPF โปร่งใส, click-through ผ่าน WS_EX_TRANSPARENT)
    │
    ├── TrayService          (NotifyIcon + context menu, ไอคอนตามสถานะ)
    ├── HotkeyService        (RegisterHotKey → Ctrl+Alt+M)
    ├── PowerMonitorService  (แบต/ปลั๊ก → battery-saver)
    └── SettingsService      (JSON persistence)
```

**การเลือก sensor (Auto):** Gyro/Accelerometer → ถ้าไม่มี → Camera/Optical-flow → ถ้าไม่มี → Mic → ถ้าไม่มีเลย = visual-only

**Lifecycle ของกล้อง:** กล้อง/เซนเซอร์จะเริ่มทำงาน **เฉพาะตอน enable แอป** เท่านั้น ปิดอยู่ = ไม่เปิดกล้อง (ถ้า auto-activation เปิด จะใช้เฉพาะเซนเซอร์ที่ไม่ใช่กล้องคอยจับการเคลื่อนไหว)

---

## Requirements

| รายการ | เวอร์ชัน |
|--------|---------|
| Windows | 10 22H2 (build 22621) ขึ้นไป |
| .NET SDK | 8.0+ |
| Visual Studio | 2022 17.x+ (ออปชัน — ใช้ dotnet CLI ได้) |
| Architecture | x64 เท่านั้น (OpenCV native DLL) |

---

## Setup ก่อน build

ดาวน์โหลด cascade file สำหรับ Tier 2 (camera) — ถ้าไม่มี แอปยังรันได้ แค่ Tier 2 จะถูก disable อัตโนมัติ:

```powershell
Invoke-WebRequest `
  -Uri "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml" `
  -OutFile "Steady\Assets\haarcascade_frontalface_default.xml"
```

---

## Build

รันคำสั่งจาก **repo root** (`Steady - Focal Point for Windows\`):

```powershell
# Restore + Build ผ่าน solution (แนะนำ)
dotnet build "Steady\Steady.sln" -c Release

# หรือ build เฉพาะ project
dotnet build "Steady\Steady.csproj" -c Release
```

## Run

```powershell
# รันตรงจาก source
dotnet run --project "Steady\Steady.csproj"

# หรือรัน exe หลัง build
".\Steady\bin\Release\net8.0-windows10.0.22621.0\Steady.exe"
```

เมื่อแอปเริ่มทำงาน: ไอคอนจะอยู่ใน **System Tray** (ไม่มีหน้าต่างหลัก) — ดับเบิลคลิก tray icon หรือกด **Ctrl+Alt+M** เพื่อเปิด/ปิด overlay

## Publish (Portable, self-contained)

ได้โฟลเดอร์พร้อมแจก ไม่ต้องให้ผู้ใช้ลง .NET runtime:

```powershell
dotnet publish "Steady\Steady.csproj" -c Release -r win-x64 --self-contained true
```

ผลลัพธ์อยู่ที่ `Steady\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\` (มี `Steady.exe` + DLL + `Assets\` ครบ) → zip แจกได้เลย

> สำหรับการแจกวงกว้าง/ให้มี Start Menu + uninstall + แก้ปัญหา path ของ "เริ่มพร้อม Windows" แนะนำทำ installer ด้วย Inno Setup หรือ MSIX (manifest อยู่ใน `Assets\Package.appxmanifest`)

---

## การใช้งาน

| การกระทำ | ผล |
|---------|-----|
| Double-click tray icon | Toggle เปิด/ปิด Steady |
| Ctrl+Alt+M | Toggle เปิด/ปิด Steady |
| Right-click tray → Settings | เปิดหน้าตั้งค่า |
| Right-click tray → Exit | ปิดแอป |

**หน้าตั้งค่า:**

- **Dot spacing** — ระยะห่างจุด (100–400px, default 300; ค่าน้อย = จุดหนาแน่น)
- **Dot size** — ขนาดจุด (default 10px)
- **Opacity** — ความโปร่งใส (default 60%)
- **Intensity** — ความแรงของ motion cue
- **Sensor source** — Auto / Gyro / Camera / ...
- **เปิด/ปิดการใช้กล้อง**, **Battery saver**, **Auto-activation**, **Run at Windows startup**

---

## พฤติกรรมของจุด (Dot behavior)

- **Density-based layout** — จำนวนจุดต่อขอบคำนวณจากความยาวขอบ ÷ spacing จึงสม่ำเสมอทุกขนาดจอ ขอบล่างเว้นระยะ (`BottomMargin`) มากกว่าขอบอื่นเพื่อเลี่ยงทาสก์บาร์
- **Coherent motion** — จุดทุกจุดเลื่อนพร้อมกันตามเวกเตอร์ความเร่งจริง (ไม่สุ่ม): เร่ง→ลง, เบรก→ขึ้น, เลี้ยวซ้าย→ขวา, เลี้ยวขวา→ซ้าย
- **Edge fade** — จุดจาง/เล็กลงเมื่อเข้าหากลางจอ
- **Adaptive contrast** — sample สีพื้นหลัง แล้วเลือกจุดสว่าง/เข้มให้ตัดกัน (พื้นมืด=ขาว, พื้นสว่าง=เข้ม) มี hysteresis กัน flicker

## การประหยัดแบต (Tier 2 camera)

- กล้องเปิดเฉพาะตอน enable เท่านั้น
- ROI detection (ค้นเฉพาะรอบใบหน้าเดิม), adaptive FPS (ลดเหลือ 2–4fps เมื่อนิ่ง), CLAHE เฉพาะตอนมืด
- **Deep idle** — หยุดนิ่งนาน ~8 วิ ปล่อย VideoCapture (ปิดเซนเซอร์กล้องจริง) แล้วปลุกเป็นช่วงๆ มาเช็กการเคลื่อนไหว

---

## Versioning

ใช้ **Semantic Versioning** `MAJOR.MINOR.PATCH` (เลข 3 ส่วน เริ่มที่ `1.0.0`):

- **MAJOR** — เปลี่ยนใหญ่จนของเดิมใช้ต่อไม่ได้
- **MINOR** — เพิ่มฟีเจอร์ใหม่ แบบเข้ากันได้
- **PATCH** — แก้บั๊ก/ปรับเล็กน้อย

แหล่งความจริงเดียว: `<Version>` ใน `Steady.csproj` — แก้ที่นี่ที่เดียว แล้วแอปจะอ่านมาแสดงผ่าน `Helpers/AppInfo.cs` ที่ **หน้า Settings (มุมล่างซ้าย)** และ **เมนู system tray** บันทึกการเปลี่ยนแปลงแต่ละเวอร์ชันใน [`CHANGELOG.md`](CHANGELOG.md)

> เวอร์ชันปัจจุบัน: **1.0.0** (หมายเหตุ: `Steady_Feature_Spec.docx` มีเลขเวอร์ชันของ "เอกสาร" แยกต่างหาก ไม่เกี่ยวกับเวอร์ชันแอป)

---

## Tray Icon

| State | ลักษณะ |
|-------|--------|
| Enabled | Gradient น้ำเงิน→ม่วง + screen frame + motion-cue dots |
| Disabled | Gray tile |

ใช้ไฟล์ `Assets/steady.ico` / `Assets/steady_off.ico` ก่อน ถ้าไม่มีจะ draw จาก code อัตโนมัติ

---

## ตำแหน่งไฟล์ตั้งค่า

`%AppData%\Steady\settings.json` — ลบไฟล์นี้เพื่อรีเซ็ตเป็นค่า default
