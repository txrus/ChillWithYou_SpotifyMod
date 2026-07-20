# ChillWithYou Spotify Mod

BepInEx mod สำหรับเกม **Chill with You: Lo-Fi Story** — เพิ่มเครื่องเล่น Spotify เข้าไปในเกม ควบคุมเพลง ค้นหา และเลือกเพลย์ลิสต์ได้โดยไม่ต้องสลับหน้าจอออกจากเกม

![ตัวอย่างมอดในเกม — เครื่องเล่น Spotify และรายการเพลย์ลิสต์ใน Chill with You](assets/screenshot.png)

> ⚠️ **จำเป็นต้องมีบัญชี Spotify Premium** — Spotify Web API อนุญาตให้สั่งควบคุมการเล่นเพลง (play/pause/skip) เฉพาะบัญชี Premium เท่านั้น บัญชีฟรีจะล็อกอินได้แต่สั่งเล่นเพลงไม่ได้

## ฟีเจอร์

- ล็อกอิน Spotify ด้วย OAuth 2.0 (Authorization Code + PKCE) — ไม่ต้องใช้ client secret
- จำ session ไว้ในเครื่อง (เข้ารหัสด้วย Windows DPAPI) เปิดเกมใหม่ไม่ต้องล็อกอินซ้ำ
- ควบคุมการเล่นเพลง เล่น/หยุด/ข้าม จาก UI ในเกม
- ค้นหาเพลงและเลือกเพลย์ลิสต์ของตัวเองได้
- มี rate limiter กันยิง API ถี่เกินไป

## การติดตั้ง (สำหรับผู้เล่น)

1. ติดตั้ง [BepInEx 5.x (x64)](https://github.com/BepInEx/BepInEx/releases) ลงในโฟลเดอร์เกม แล้วเปิดเกม 1 ครั้งเพื่อให้ BepInEx สร้างโฟลเดอร์
2. นำไฟล์ `ChillWithYou_SpotifyMod.dll` (build เองตามขั้นตอนด้านล่าง) ไปวางใน `<โฟลเดอร์เกม>\BepInEx\plugins`
3. เปิดเกม แล้วกดปุ่มล็อกอิน Spotify ในเกม — เบราว์เซอร์จะเปิดหน้าอนุญาตของ Spotify ให้กดยืนยัน

## การสร้าง Spotify App (จำเป็นก่อน build)

มอดต้องใช้ **Client ID** ของคุณเองจาก Spotify Developer Dashboard:

1. ไปที่ [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) แล้วล็อกอินด้วยบัญชี Spotify ของคุณ
2. กด **Create app** ตั้งชื่อ/คำอธิบายอะไรก็ได้
3. ในช่อง **Redirect URIs** ใส่ค่านี้ให้ตรงเป๊ะ (ห้ามลืม `/` ท้าย):
   ```
   http://127.0.0.1:8901/callback/
   ```
4. เลือก API ที่ใช้เป็น **Web API** แล้วกด Save
5. เข้าไปหน้า Settings ของแอป คัดลอก **Client ID** มาใช้กับสคริปต์ build ด้านล่าง

> ใช้แค่ Client ID เท่านั้น **ไม่ต้องใช้ Client Secret** เพราะมอดใช้ OAuth แบบ PKCE

## Build แบบง่าย (แนะนำ)

ต้องมี [.NET SDK](https://dotnet.microsoft.com/download) และตัวเกม (พร้อม BepInEx ติดตั้งแล้ว) จากนั้นเปิด PowerShell ในโฟลเดอร์โปรเจกต์แล้วรัน:

```powershell
.\build.ps1
```

สคริปต์จะถาม Client ID แล้ว build DLL ให้เสร็จสรรพ (ใส่ ID ผ่าน parameter ก็ได้):

```powershell
.\build.ps1 -ClientId "your32charclientid"

# ถ้าเกมไม่ได้อยู่ path เริ่มต้น ระบุเองได้:
.\build.ps1 -ClientId "..." -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story"
```

ได้ไฟล์ `bin\Release\netstandard2.1\ChillWithYou_SpotifyMod.dll` และถ้าเจอโฟลเดอร์เกม สคริปต์จะ copy เข้า `BepInEx\plugins` ให้อัตโนมัติ — Client ID จะถูกฝังใน DLL เท่านั้น ไฟล์ซอร์สโค้ดจะถูกคืนค่าเดิมหลัง build เสมอ

> ถ้ารันสคริปต์ไม่ได้เพราะ execution policy ให้รันด้วย `powershell -ExecutionPolicy Bypass -File .\build.ps1`

## การ build เอง (ไม่ใช้สคริปต์)

แก้ไฟล์ `SpotifyAuth.cs` แทนที่ `ENTER_YOUR_CLIENT_ID` ด้วย Client ID ของคุณ:

```csharp
private const string ClientId = "ENTER_YOUR_CLIENT_ID";
```

โปรเจกต์อ้างอิง DLL จากโฟลเดอร์เกมโดยตรง ค่าเริ่มต้นชี้ไปที่:

```
F:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story
```

ถ้าเกมอยู่ที่อื่น ให้สร้างไฟล์ `GameDir.props` (ไฟล์นี้ไม่ถูก commit) ไว้ข้าง ๆ `.csproj`:

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Chill with You Lo-Fi Story</GameDir>
  </PropertyGroup>
</Project>
```

จากนั้น:

```
dotnet build
```

หลัง build สำเร็จ DLL จะถูก copy เข้า `BepInEx\plugins` ของเกมให้อัตโนมัติ (ถ้าโฟลเดอร์มีอยู่)

## โครงสร้างโค้ดคร่าว ๆ

| ไฟล์ | หน้าที่ |
|---|---|
| `plugin.cs` | จุดเริ่มต้นปลั๊กอิน + MainThreadDispatcher |
| `SpotifyAuth.cs` | OAuth PKCE flow + local callback server |
| `SpotifyTokenStore.cs` | เก็บ token ลงเครื่องแบบเข้ารหัส (DPAPI) |
| `SpotifyWebApi.cs` / `SpotifyApi.cs` / `SpotifyApiClient.cs` | เรียก Spotify Web API |
| `SpotifySearchApi.cs` | ค้นหาเพลง |
| `SpotifyRateLimiter.cs` | จำกัดความถี่การเรียก API |
| `SpotifyButtonInjector.cs` | สร้าง/ฉีด UI เครื่องเล่นเข้าไปในเกม |
| `PlaylistSelectionUI.cs` | UI เลือกเพลย์ลิสต์ |
| `SpotifyPatches.cs` | Harmony patches |

## ข้อจำกัด / หมายเหตุ

- ต้องใช้บัญชี **Spotify Premium** ในการควบคุมการเล่นเพลง
- รองรับเฉพาะ Windows (การเก็บ token ใช้ DPAPI)
- ในโค้ดไม่มี secret ใด ๆ — ใช้แค่ Client ID (public client แบบ PKCE) ที่คุณสร้างเองตามขั้นตอนด้านบน
- มอดนี้ไม่มีส่วนเกี่ยวข้องกับผู้พัฒนาเกมหรือ Spotify

## License

[MIT](LICENSE) © pw_txr
