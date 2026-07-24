# Changelog

รูปแบบอิงตาม [Keep a Changelog](https://keepachangelog.com/) และใช้ [Semantic Versioning](https://semver.org/)

## [1.1.2]

### Fixed
- แก้ชื่อเพลงในคิว "หายไป" (เหลือแต่ชื่อศิลปิน) พร้อมกับไฮไลต์เขียวของเพลงที่กำลังเล่น:
  Unity `Text` ซ่อนทั้งบรรทัดเมื่อ rect เตี้ยเกินความสูงฟอนต์ (`verticalOverflow` เป็น
  `Truncate` โดยดีฟอลต์) พอ restyle เปลี่ยนไปใช้ฟอนต์ IBM Plex ของเกมซึ่งสูงกว่า Arial
  บรรทัดชื่อเพลง 12pt ในแถวสูง 30px เลยตกเกณฑ์แล้วหายไป ตอนนี้ `CreateText` ตั้ง
  `verticalOverflow = Overflow` ข้อความจึงวาดเสมอ
- แก้ชื่อเพลงยาวตัดบรรทัดแล้วล้นไปทับแถวอื่นและทับพื้นที่ search/My Lists ด้านล่าง:
  แถวคิวและแถวผลค้นหา/เพลย์ลิสต์เรนเดอร์ชื่อเพลงกับศิลปินเป็นบรรทัดเดียว
  (`horizontalOverflow = Overflow`) แล้ว clip ด้วย `RectMask2D` ชื่อยาวจึงถูกตัดที่ขอบคอลัมน์
  แทนที่จะขึ้นบรรทัดใหม่ และเพิ่มความสูงแถว 30/32 → 36
- แก้คิวแสดงเพลงซ้ำสองรอบเมื่อเปิด repeat: ถ้าเพลย์ลิสต์สั้นกว่าหน้าต่างคิว (~20 เพลง)
  `/me/player/queue` จะวนกลับไปต้น context เพลงเดิมจึงกลับมาอีกรอบ (เพลย์ลิสต์ 7 เพลง
  แสดง 14+ แถว) ตอนนี้ `GetQueueTracksAsync` กันซ้ำด้วย `HashSet` ของ track id แล้ว
  (ไฟล์ local ที่ไม่มี id ยังผ่านตามปกติเพราะกันซ้ำไม่ได้)
- แก้ใช้งานครั้งแรกแล้ว UI ไม่อัพเดทเอง (ต้องกด ↻) หลังไปเปิดเพลงใน Spotify แล้วสลับกลับเข้าเกม:
  ตัว resync ตอน alt-tab (`Application.focusChanged`) ถูก subscribe ใน `ApplyNowPlaying`
  หลัง null check เคส connect ตอนยังไม่มีเพลงเล่นเลยไม่เคยถูกติดตั้ง ตอนนี้ subscribe
  ตั้งแต่ inject แทน
- แก้แถวเพลงของเกมวาดทับ playlist header กับแถบ search ทันทีหลังกด Connect สำเร็จ:
  `OnLoginSuccess` เปิดแถวพวกนี้ด้วย `SetActive(true)` โดยไม่ rebuild scroll content
  ชั้นนอก section สูงขึ้นแต่แถวของเกมไม่เลื่อนลงตาม (root cause เดียวกับข้อถัดไป)
- แก้รายการเพลงของเกม (Original & Special) ทับผลค้นหา Spotify: `BuildSearchResults`
  rebuild เฉพาะลิสต์ผลค้นหา ไม่ได้ rebuild scroll content ด้านนอก section ของม็อดจึงไม่ขยาย
  ตามผลลัพธ์ ตอนนี้ `ForceRebuildLayoutImmediate` ที่ `_cachedScrollRect.content` ด้วย
  แถวของเกมจึงไหลลงไปอยู่ใต้ผลค้นหา

### Changed
- รวม request envelope ของ Spotify (HttpClient, bearer header, 429 → rate limiter,
  error logging, retry 401/403) ที่เดิมเขียนซ้ำใน `SpotifyApi` / `SpotifyWebApi` /
  `SpotifySearchApi` ให้เหลือ `SpotifyGateway` ตัวเดียว
- แยก state machine ของ now-playing (นาฬิกา interpolate ความคืบหน้า + สถานะ play/pause)
  ออกจาก `SpotifyButtonInjector` เป็น `NowPlayingSession` ที่เป็น logic ล้วน ไม่พึ่ง Unity
  พร้อม unit test 13 เคสที่รันด้วย .NET SDK ปกติได้โดยไม่ต้องเปิดเกม
- ลบโค้ด playlist-selection ที่ไม่ถูกเรียกใช้แล้วออก
- เก็บ HTTP ที่หลุด envelope ใน `SpotifyButtonInjector` เข้า API layer: เดิม injector
  ประกอบ body ของ `me/player/play` เอง 3 ชุด และมี `HttpClient` ตัวที่สี่ยิง
  `/albums/{id}/tracks` ตรงๆ (ไม่ผ่าน retry/429/logging ของ gateway) ตอนนี้คำสั่งเล่น
  ทั้งหมดอยู่ใน `SpotifyApi.Play*` และการโหลดอัลบั้มอยู่ใน `SpotifyWebApi.GetAlbumTracksAsync`
- แยกชุดประกอบ widget สไตล์เกม (โทนสี, หาฟอนต์ของเกม, ปุ่มวงกลม/pill, progress slider,
  ช่องค้นหา) ออกเป็น `SpotifyUiKit` - โมดูลที่รู้แค่ "หน้าตา" ไม่รู้จัก Spotify เลย
- แยก logic หน้าจอของ panel ทั้งหมด (แถวไหนโผล่/ซ่อน, ตอนไหนต้อง reflow layout,
  พื้นที่ผลลัพธ์โชว์อะไร, toggle My Lists) ออกเป็น `PanelViewModel` - state machine ล้วน
  ป้อน event เข้าแล้วคืน `PanelState` snapshot เต็มชุด ฝั่ง Unity เหลือ `Apply(state)`
  จุดเดียวแบบ idempotent ทำให้บั๊กตระกูล "ลืม SetActive/ลืม rebuild" (ต้นเหตุ 3 ใน 6
  บั๊กของเวอร์ชันนี้) เกิดไม่ได้เชิงโครงสร้าง (#15, #16)
- แยกกฎ orchestration ของการ refresh (โหลด context เมื่อไหร่/ผ่านทางไหน, กฎ
  commit-เฉพาะ-ตอนสำเร็จที่กันคิวว่างค้างถาวร, จังหวะ retry หลังสั่งเล่น, cooldown ของ
  focus-resync) ออกเป็น `RefreshCoordinator` - injector เหลือแค่ยิง API ตามแผน (#17, #18)
- test bench รวมทั้งหมด 57 เคส (`dotnet test` ไม่ต้องมีเกม/Unity/บัญชี Spotify) ครอบ
  `NowPlayingSession`, `PanelViewModel`, `RefreshCoordinator` รวม regression ของบั๊กจริง
  ทุกตัวในเวอร์ชันนี้ที่เดิมต้องเปิดเกม + login + เปิดเพลงจริงถึงจะเจอ

## [1.1.1]

### Fixed
- แก้อาการ UI ค้างหลังกด **Connect Spotify** แล้ว approve: callback ของ OAuth
  (`OnLoginSuccess` / `OnLoginFailed`) ถูกเรียกจาก continuation ที่รันบน thread pool
  แล้วไปแตะ Unity UI ตรงๆ ทำให้ throw `"can only be called from the main thread"`
  และโดนกลืนเงียบ ๆ — เบราว์เซอร์ขึ้น "Login successful" แต่ panel ในเกมค้างที่ปุ่ม
  Connect ไม่ขยับ ตอนนี้ marshal กลับ main thread ด้วย `Plugin.RunOnMainThread(...)` แล้ว

## [1.1.0]

- เวอร์ชันเริ่มต้นที่มี in-game Spotify player: playback control, search และ playlist selection
