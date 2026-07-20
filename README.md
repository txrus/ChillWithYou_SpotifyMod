# ChillWithYou Spotify Mod

BepInEx mod สำหรับเกม **Chill with You: Lo-Fi Story** — เพิ่มเครื่องเล่น Spotify เข้าไปในเกม ควบคุมเพลง ค้นหา และเลือกเพลย์ลิสต์ได้โดยไม่ต้องสลับหน้าจอออกจากเกม

> ต้องมีบัญชี **Spotify Premium** เพื่อสั่งควบคุมการเล่นเพลงผ่าน Web API

## ฟีเจอร์

- ล็อกอิน Spotify ด้วย OAuth 2.0 (Authorization Code + PKCE) — ไม่ต้องใช้ client secret
- จำ session ไว้ในเครื่อง (เข้ารหัสด้วย Windows DPAPI) เปิดเกมใหม่ไม่ต้องล็อกอินซ้ำ
- ควบคุมการเล่นเพลง เล่น/หยุด/ข้าม จาก UI ในเกม
- ค้นหาเพลงและเลือกเพลย์ลิสต์ของตัวเองได้
- มี rate limiter กันยิง API ถี่เกินไป

## การติดตั้ง (สำหรับผู้เล่น)

1. ติดตั้ง [BepInEx 5.x (x64)](https://github.com/BepInEx/BepInEx/releases) ลงในโฟลเดอร์เกม แล้วเปิดเกม 1 ครั้งเพื่อให้ BepInEx สร้างโฟลเดอร์
2. นำไฟล์ `ChillWithYou_SpotifyMod.dll` ไปวางใน `<โฟลเดอร์เกม>\BepInEx\plugins`
3. เปิดเกม แล้วกดปุ่มล็อกอิน Spotify ในเกม — เบราว์เซอร์จะเปิดหน้าอนุญาตของ Spotify ให้กดยืนยัน

## การ build (สำหรับนักพัฒนา)

ต้องมี [.NET SDK](https://dotnet.microsoft.com/download) และตัวเกม (พร้อม BepInEx ติดตั้งแล้ว)

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

- รองรับเฉพาะ Windows (การเก็บ token ใช้ DPAPI)
- Client ID ของ Spotify ที่อยู่ในโค้ดเป็น public client (PKCE) ตามที่ Spotify ออกแบบไว้ ไม่มี secret ใด ๆ ในโค้ด
- มอดนี้ไม่มีส่วนเกี่ยวข้องกับผู้พัฒนาเกมหรือ Spotify

## License

[MIT](LICENSE) © pw_txr
