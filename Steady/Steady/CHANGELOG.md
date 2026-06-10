# Changelog

ทุกการเปลี่ยนแปลงที่สำคัญของ Steady จะถูกบันทึกไว้ที่นี่

ใช้ **Semantic Versioning** รูปแบบ `MAJOR.MINOR.PATCH`:
- **MAJOR** — เปลี่ยนใหญ่จนของเดิมใช้ต่อไม่ได้ (breaking)
- **MINOR** — เพิ่มฟีเจอร์ใหม่ แบบเข้ากันได้กับของเดิม
- **PATCH** — แก้บั๊ก/ปรับเล็กน้อย

> เวอร์ชันจริงกำหนดที่ `Steady.csproj` (`<Version>`) และแสดงในหน้า Settings + เมนู tray

---

## [1.0.0] — 2026-06-09

เวอร์ชันแรก (initial release)

**Core**
- Motion cue dots overlay แบบ click-through ที่ขอบจอ — density-based layout (จำนวนจุดตามความยาวขอบ)
- Tier 1: gyro/accelerometer (`Windows.Devices.Sensors`)
- Tier 2: webcam head-tracking (OpenCV) + optical flow + mic เป็น context
- เปิด/ปิดด้วย Ctrl+Alt+M และ system tray; single-instance

**Comfort**
- ปรับ dot spacing (100–400px, default 300), size (default 10px), opacity (default 60%), intensity
- Adaptive contrast (สีจุดตัดกับพื้นหลังอัตโนมัติ), edge fade, night mode
- จุดขอบล่างเว้นระยะมากขึ้นเพื่อเลี่ยงทาสก์บาร์

**Battery / Privacy**
- กล้องเปิดเฉพาะตอน enable เท่านั้น
- ROI face detection, adaptive FPS, CLAHE เฉพาะตอนมืด
- Deep-idle: ปล่อย VideoCapture เมื่อหยุดนิ่งนาน แล้วปลุกเป็นช่วงๆ

**Branding**
- โลโก้/ไอคอน blue→purple (`steady.ico` / `steady_off.ico`)

---

<!--
รูปแบบสำหรับเวอร์ชันถัดไป:

## [1.0.1] — YYYY-MM-DD   (แก้บั๊ก)
### Fixed
- ...

## [1.1.0] — YYYY-MM-DD   (เพิ่มฟีเจอร์)
### Added
- ...
-->
