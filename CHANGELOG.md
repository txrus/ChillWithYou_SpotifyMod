# Changelog

รูปแบบอิงตาม [Keep a Changelog](https://keepachangelog.com/) และใช้ [Semantic Versioning](https://semver.org/)

## [1.1.1]

### Fixed
- แก้อาการ UI ค้างหลังกด **Connect Spotify** แล้ว approve: callback ของ OAuth
  (`OnLoginSuccess` / `OnLoginFailed`) ถูกเรียกจาก continuation ที่รันบน thread pool
  แล้วไปแตะ Unity UI ตรงๆ ทำให้ throw `"can only be called from the main thread"`
  และโดนกลืนเงียบ ๆ — เบราว์เซอร์ขึ้น "Login successful" แต่ panel ในเกมค้างที่ปุ่ม
  Connect ไม่ขยับ ตอนนี้ marshal กลับ main thread ด้วย `Plugin.RunOnMainThread(...)` แล้ว

## [1.1.0]

- เวอร์ชันเริ่มต้นที่มี in-game Spotify player: playback control, search และ playlist selection
