using HarmonyLib;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillWithYou_SpotifyMod
{
    internal static class SpotifyButtonsInjector
    {
        private static bool _injected;
        private static GameObject _spotifySection;
        private static ScrollRect _cachedScrollRect;
        private static Font _arialFont;

        private static Image _coverImage;
        private static Text _trackTitleText;
        private static Text _artistText;
        private static Text _statusText;
        private static Text _posText;
        private static Text _durText;
        private static Slider _progressSlider;
        private static Text _playPauseLabel;

        private static GameObject _controlsRow;
        private static GameObject _connectRow;
        private static GameObject _queueList;
        private static GameObject _playlistHeader;
        private static Image _playlistImage;
        private static Text _playlistNameText;
        private static Text _playlistSubText; // "PLAYING FROM PLAYLIST" - ซ่อนเมื่อไม่ได้เล่นจาก playlist

        private static InputField _searchInput;
        private static GameObject _searchResultsList;
        private static GameObject _searchRow;

        // แถวใน queue list ทั้งหมด (trackId -> Text ชื่อเพลงของแถวนั้น) เก็บไว้ highlight เพลงที่กำลังเล่นอยู่
        private static readonly System.Collections.Generic.List<(string trackId, Text title)> _queueRowTitles =
            new System.Collections.Generic.List<(string, Text)>();
        private static string _currentTrackId;

        private static bool _lastKnownIsPlaying;

        // จำ bytes ของปกที่แสดงอยู่ (reference เดิมจาก cache ฝั่ง SpotifyApi) กันสร้าง Texture2D ซ้ำทุกรอบ refresh
        private static byte[] _lastAppliedCoverBytes;

        // ใช้ตัวเดียวร่วมกันทั้ง class - สร้าง HttpClient ใหม่ทุก request จะสะสม socket ค้าง
        private static readonly System.Net.Http.HttpClient Http = new System.Net.Http.HttpClient();

        // สำหรับ interpolate progress bar เองระหว่างรอบ poll จริง (ไม่ต้องยิง API ถี่ขึ้น)
        private static TimeSpan _syncedPosition;
        private static TimeSpan _syncedDuration;
        private static DateTime _syncedAtUtc;
        private static bool _isInterpolating;
        private static bool _subscribedToTick;
        private static bool _songEndTriggerFired;

        // === โทนสีตามภาษาดีไซน์ของเกม: แผงดำโปร่งแสง + ขอบ/เส้นขาว + ตัวหนังสือขาวสามระดับ ===
        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.055f, 0.55f);
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.85f);     // ขอบหลัก (ปุ่ม/กรอบปก)
        private static readonly Color LineSoft = new Color(1f, 1f, 1f, 0.32f);      // เส้นคั่น/ขอบรอง
        private static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.65f);
        private static readonly Color TextFaint = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color HoverFill = new Color(1f, 1f, 1f, 0.08f);     // fill ขาวจางตอนชี้
        private static readonly Color PressFill = new Color(1f, 1f, 1f, 0.18f);     // fill ขาวตอนกด
        // เขียว Spotify ใช้เชิงความหมายเท่านั้น: progress fill, เพลงที่กำลังเล่น, ปุ่ม Connect
        private static readonly Color ButtonActive = new Color(0.11f, 0.73f, 0.33f, 1f);

        public static void Inject(ScrollRect scrollRect, GameObject buttonsParent)
        {
            // เดิมเช็คแค่ _injected เฉยๆ ทำให้ถ้าปิด-เปิดเมนู Playlist ใหม่ (Unity destroy object เดิมทิ้ง
            // แล้วสร้างชุดใหม่) โค้ดจะไม่ re-inject เข้า object ใหม่เลย -> field เก่าๆ (_trackTitleText ฯลฯ)
            // เลยชี้ไปที่ object ที่ตายไปแล้ว อัปเดตอะไรก็ไม่ขึ้นจอจริง
            // เช็ค _spotifySection == null เพิ่ม (Unity overload ทำให้ destroyed object เทียบเท่ากับ null)
            if (_injected && _spotifySection != null) return;
            _injected = true;

            try
            {
                _cachedScrollRect = scrollRect;
                _arialFont = FindUiFont();
                Transform parentTransform = buttonsParent.transform;

                VerticalLayoutGroup contentVlg = parentTransform.GetComponent<VerticalLayoutGroup>();
                if (contentVlg != null) contentVlg.childControlWidth = false;

                _spotifySection = new GameObject("SpotifySection");
                _spotifySection.transform.SetParent(parentTransform, worldPositionStays: false);
                _spotifySection.transform.SetAsFirstSibling();

                RectTransform rootRt = _spotifySection.AddComponent<RectTransform>();
                rootRt.anchorMin = new Vector2(0f, 1f);
                rootRt.anchorMax = new Vector2(1f, 1f);
                rootRt.pivot = new Vector2(0.5f, 1f);
                rootRt.sizeDelta = new Vector2(0f, 0f);

                Image bg = _spotifySection.AddComponent<Image>();
                bg.color = PanelColor;

                VerticalLayoutGroup vlg = _spotifySection.AddComponent<VerticalLayoutGroup>();
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.spacing = 6f;
                vlg.padding = new RectOffset(10, 10, 8, 12);

                ContentSizeFitter fitter = _spotifySection.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // --- หัวเรื่อง "SPOTIFY" ตัวเล็กเว้นช่องไฟ + เส้นบางลากไปสุดขวา แบบ header ของเกม ---
                GameObject eyebrowRow = new GameObject("EyebrowRow");
                eyebrowRow.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                eyebrowRow.AddComponent<RectTransform>();
                LayoutElement eyebrowLe = eyebrowRow.AddComponent<LayoutElement>();
                eyebrowLe.preferredHeight = 18f;
                eyebrowLe.minHeight = 18f;
                HorizontalLayoutGroup eyebrowHlg = eyebrowRow.AddComponent<HorizontalLayoutGroup>();
                eyebrowHlg.childForceExpandWidth = false;
                eyebrowHlg.childForceExpandHeight = false;
                eyebrowHlg.childControlWidth = true;
                eyebrowHlg.childControlHeight = true;
                eyebrowHlg.spacing = 10f;
                eyebrowHlg.childAlignment = TextAnchor.MiddleLeft;

                Text eyebrow = CreateText(eyebrowRow.transform, "S P O T I F Y", 11, TextAnchor.MiddleLeft);
                eyebrow.fontStyle = FontStyle.Bold;
                eyebrow.color = TextSecondary;
                eyebrow.GetComponent<LayoutElement>().preferredWidth = 96f;

                GameObject rule = new GameObject("Rule");
                rule.transform.SetParent(eyebrowRow.transform, worldPositionStays: false);
                rule.AddComponent<RectTransform>();
                LayoutElement ruleLe = rule.AddComponent<LayoutElement>();
                ruleLe.flexibleWidth = 1f;
                ruleLe.preferredHeight = 1f;
                ruleLe.minHeight = 1f;
                Image ruleImg = rule.AddComponent<Image>();
                ruleImg.color = LineSoft;
                ruleImg.raycastTarget = false;

                // --- แถวบน: cover art + ชื่อเพลง/ศิลปิน ---
                GameObject headerRow = new GameObject("HeaderRow");
                headerRow.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                headerRow.AddComponent<RectTransform>();
                LayoutElement headerLe = headerRow.AddComponent<LayoutElement>();
                headerLe.preferredHeight = 64f;
                headerLe.minHeight = 64f;

                HorizontalLayoutGroup headerHlg = headerRow.AddComponent<HorizontalLayoutGroup>();
                headerHlg.childForceExpandWidth = false;
                headerHlg.childForceExpandHeight = true;
                headerHlg.childControlWidth = true;
                headerHlg.childControlHeight = true;
                headerHlg.spacing = 8f;
                headerHlg.childAlignment = TextAnchor.MiddleLeft;

                GameObject coverGo = new GameObject("Cover");
                coverGo.transform.SetParent(headerRow.transform, worldPositionStays: false);
                coverGo.AddComponent<RectTransform>();
                LayoutElement coverLe = coverGo.AddComponent<LayoutElement>();
                coverLe.preferredWidth = 64f;
                coverLe.preferredHeight = 64f;
                _coverImage = coverGo.AddComponent<Image>();
                _coverImage.color = new Color(0.16f, 0.15f, 0.20f, 1f); // placeholder เข้มๆ จนกว่าจะโหลดปกอัลบั้มจริง

                GameObject textCol = new GameObject("TextCol");
                textCol.transform.SetParent(headerRow.transform, worldPositionStays: false);
                textCol.AddComponent<RectTransform>();
                LayoutElement textColLe = textCol.AddComponent<LayoutElement>();
                textColLe.flexibleWidth = 1f;
                textColLe.preferredHeight = 64f;
                VerticalLayoutGroup textColVlg = textCol.AddComponent<VerticalLayoutGroup>();
                textColVlg.childControlWidth = true;
                textColVlg.childControlHeight = true;
                textColVlg.childForceExpandWidth = true;
                textColVlg.childAlignment = TextAnchor.MiddleLeft;
                textColVlg.spacing = 2f;

                _trackTitleText = CreateText(textCol.transform, "Connect Spotify and play a song on any device to see controls", 14, TextAnchor.MiddleLeft);
                _trackTitleText.fontStyle = FontStyle.Bold;
                _artistText = CreateText(textCol.transform, "", 12, TextAnchor.MiddleLeft);
                _artistText.color = TextSecondary;

                // --- Progress bar + เวลา ---
                GameObject progressRow = new GameObject("ProgressRow");
                progressRow.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                progressRow.AddComponent<RectTransform>();
                LayoutElement progressLe = progressRow.AddComponent<LayoutElement>();
                progressLe.preferredHeight = 20f;
                progressLe.minHeight = 20f;

                HorizontalLayoutGroup progressHlg = progressRow.AddComponent<HorizontalLayoutGroup>();
                progressHlg.childForceExpandWidth = false;
                progressHlg.childForceExpandHeight = true;
                progressHlg.childControlWidth = true;
                progressHlg.childControlHeight = true;
                progressHlg.spacing = 6f;
                progressHlg.childAlignment = TextAnchor.MiddleCenter;

                _posText = CreateInlineText(progressRow.transform, "0:00", 30f);
                _progressSlider = CreateProgressSlider(progressRow.transform);
                _durText = CreateInlineText(progressRow.transform, "0:00", 30f);

                // --- Controls row: prev / play-pause / next ---
                _controlsRow = new GameObject("ControlsRow");
                _controlsRow.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                RectTransform crRt = _controlsRow.AddComponent<RectTransform>();
                crRt.sizeDelta = new Vector2(0f, 50f);
                LayoutElement crLe = _controlsRow.AddComponent<LayoutElement>();
                crLe.preferredHeight = 50f;
                crLe.minHeight = 50f;

                HorizontalLayoutGroup hlg = _controlsRow.AddComponent<HorizontalLayoutGroup>();
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.spacing = 22f;
                hlg.childAlignment = TextAnchor.MiddleCenter;

                // motif เดียวกับ transport ของเกม: ปุ่มข้างเป็นวงแหวน ปุ่มกลาง (play/pause) วงกลมขาวทึบ
                Button prevBtn = CreateCircleButton(_controlsRow.transform, "<<", 36f, solid: false);
                Button playPauseBtn = CreateCircleButton(_controlsRow.transform, "||", 46f, solid: true);
                _playPauseLabel = playPauseBtn.GetComponentInChildren<Text>();
                Button nextBtn = CreateCircleButton(_controlsRow.transform, ">>", 36f, solid: false);

                prevBtn.onClick.AddListener(() => SafeFireAndForget(OnPrevClicked()));
                playPauseBtn.onClick.AddListener(() => SafeFireAndForget(OnPlayPauseClicked()));
                nextBtn.onClick.AddListener(() => SafeFireAndForget(OnNextClicked()));

                _statusText = CreateText(_spotifySection.transform, "", 11, TextAnchor.MiddleCenter);
                _statusText.color = new Color(1f, 0.6f, 0.4f, 1f);
                _statusText.raycastTarget = false;

                // --- Connect row: เฉพาะสำหรับดึง queue (Web API ต้อง login) ---
                // playback (SMTC) ไม่เกี่ยวกับส่วนนี้ ใช้ได้แม้ยังไม่กด Connect
                _connectRow = new GameObject("ConnectRow");
                _connectRow.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                _connectRow.AddComponent<RectTransform>();
                LayoutElement connectLe = _connectRow.AddComponent<LayoutElement>();
                connectLe.preferredHeight = 38f;
                connectLe.minHeight = 38f;
                VerticalLayoutGroup connectVlg = _connectRow.AddComponent<VerticalLayoutGroup>();
                connectVlg.childForceExpandWidth = true;
                connectVlg.childControlWidth = true;
                connectVlg.childControlHeight = true;

                // ปุ่มเขียว pill ตัวหนังสือเข้ม = ภาษาปุ่ม login มาตรฐานของ Spotify ที่ user จำได้
                Button connectBtn = CreatePillButton(_connectRow.transform, "Connect Spotify", filled: true, ButtonActive, height: 34f);
                connectBtn.onClick.AddListener(OnConnectClicked);

                // --- Playlist header: ปก + ชื่อ playlist ที่กำลังเล่นอยู่ (ต้อง login แล้วเท่านั้น) ---
                _playlistHeader = new GameObject("PlaylistHeader");
                _playlistHeader.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                _playlistHeader.AddComponent<RectTransform>();
                LayoutElement plHeaderLe = _playlistHeader.AddComponent<LayoutElement>();
                plHeaderLe.preferredHeight = 48f;
                plHeaderLe.minHeight = 48f;

                HorizontalLayoutGroup plHeaderHlg = _playlistHeader.AddComponent<HorizontalLayoutGroup>();
                plHeaderHlg.childForceExpandWidth = false;
                plHeaderHlg.childControlWidth = true;
                plHeaderHlg.childControlHeight = true;
                plHeaderHlg.spacing = 8f;
                plHeaderHlg.childAlignment = TextAnchor.MiddleLeft;

                GameObject plCoverGo = new GameObject("PlaylistCover");
                plCoverGo.transform.SetParent(_playlistHeader.transform, worldPositionStays: false);
                plCoverGo.AddComponent<RectTransform>();
                LayoutElement plCoverLe = plCoverGo.AddComponent<LayoutElement>();
                plCoverLe.preferredWidth = 48f;
                plCoverLe.preferredHeight = 48f;
                _playlistImage = plCoverGo.AddComponent<Image>();
                _playlistImage.color = new Color(0.16f, 0.15f, 0.20f, 1f);

                GameObject plNameCol = new GameObject("NameCol");
                plNameCol.transform.SetParent(_playlistHeader.transform, worldPositionStays: false);
                plNameCol.AddComponent<RectTransform>();
                LayoutElement plNameColLe = plNameCol.AddComponent<LayoutElement>();
                plNameColLe.flexibleWidth = 1f;
                VerticalLayoutGroup plNameVlg = plNameCol.AddComponent<VerticalLayoutGroup>();
                plNameVlg.childControlWidth = true;
                plNameVlg.childControlHeight = true;
                plNameVlg.childForceExpandWidth = true;
                plNameVlg.childAlignment = TextAnchor.MiddleLeft;
                plNameVlg.spacing = 1f;

                _playlistNameText = CreateText(plNameCol.transform, "-", 13, TextAnchor.MiddleLeft);
                _playlistNameText.fontStyle = FontStyle.Bold;
                _playlistSubText = CreateText(plNameCol.transform, "PLAYING FROM PLAYLIST", 9, TextAnchor.MiddleLeft);
                _playlistSubText.color = TextFaint;

                // ปุ่ม refresh คิวเพลง (คิวเดินหน้าไปเรื่อยๆ ระหว่างฟัง กดนี้เพื่อดึง snapshot ล่าสุด)
                Button refreshBtn = CreateCircleButton(_playlistHeader.transform, "↻", 30f, solid: false, ringColor: LineSoft);
                Text refreshLabel = refreshBtn.GetComponentInChildren<Text>();
                if (refreshLabel != null) { refreshLabel.fontSize = 15; refreshLabel.color = TextSecondary; }
                refreshBtn.onClick.AddListener(() => SafeFireAndForget(ForceRefreshQueue()));

                // --- Playlist track list ---
                _queueList = new GameObject("PlaylistTrackList");
                _queueList.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                _queueList.AddComponent<RectTransform>();
                VerticalLayoutGroup queueVlg = _queueList.AddComponent<VerticalLayoutGroup>();
                queueVlg.childForceExpandWidth = true;
                queueVlg.childControlWidth = true;
                queueVlg.childControlHeight = true;
                queueVlg.spacing = 2f;
                ContentSizeFitter queueFitter = _queueList.AddComponent<ContentSizeFitter>();
                queueFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                _searchRow = new GameObject("SearchRow");
                _searchRow.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                _searchRow.AddComponent<RectTransform>();
                LayoutElement searchRowLe = _searchRow.AddComponent<LayoutElement>();
                searchRowLe.preferredHeight = 32f;
                searchRowLe.minHeight = 32f;

                HorizontalLayoutGroup searchRowHlg = _searchRow.AddComponent<HorizontalLayoutGroup>();
                searchRowHlg.childForceExpandWidth = false;
                searchRowHlg.childForceExpandHeight = true;
                searchRowHlg.childControlWidth = true;
                searchRowHlg.childControlHeight = true;
                searchRowHlg.spacing = 6f;

                _searchInput = CreateSearchInputField(_searchRow.transform); // flexibleWidth=1 อยู่แล้ว -> ขยายเต็มพื้นที่ที่เหลือ

                // ปุ่มต้องมี preferredWidth ตายตัว ไม่งั้น HLG จะบีบจนหายไป
                Button searchBtn = CreatePillButton(_searchRow.transform, "Search", filled: false, ButtonActive);
                LayoutElement searchBtnLe = searchBtn.GetComponent<LayoutElement>();
                searchBtnLe.preferredWidth = 64f;
                searchBtnLe.minWidth = 64f;
                searchBtn.onClick.AddListener(() => SafeFireAndForget(OnSearchClicked()));

                // ปุ่มเรียกดู playlist ของตัวเอง - แสดงผลในพื้นที่เดียวกับผลค้นหา
                Button myListsBtn = CreatePillButton(_searchRow.transform, "My Lists", filled: false, ButtonActive);
                LayoutElement myListsBtnLe = myListsBtn.GetComponent<LayoutElement>();
                myListsBtnLe.preferredWidth = 72f;
                myListsBtnLe.minWidth = 72f;
                myListsBtn.onClick.AddListener(() => SafeFireAndForget(OnMyPlaylistsClicked()));
                _showingMyPlaylists = false; // UI ชุดใหม่เริ่มจาก list ว่างเสมอ กัน toggle ค้างจากรอบก่อน

                // --- Search results list (แยก 4 หมวด: Tracks/Artists/Albums/Playlists) ---
                _searchResultsList = new GameObject("SearchResultsList");
                _searchResultsList.transform.SetParent(_spotifySection.transform, worldPositionStays: false);
                _searchResultsList.AddComponent<RectTransform>();
                VerticalLayoutGroup searchResultsVlg = _searchResultsList.AddComponent<VerticalLayoutGroup>();
                searchResultsVlg.childForceExpandWidth = true;
                searchResultsVlg.childControlWidth = true;
                searchResultsVlg.childControlHeight = true;
                searchResultsVlg.spacing = 2f;
                ContentSizeFitter searchResultsFitter = _searchResultsList.AddComponent<ContentSizeFitter>();
                searchResultsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                bool alreadyLoggedIn = SpotifyAuth.IsLoggedIn;
                _connectRow.SetActive(!alreadyLoggedIn);
                _playlistHeader.SetActive(alreadyLoggedIn);
                _queueList.SetActive(alreadyLoggedIn);
                _controlsRow.SetActive(alreadyLoggedIn);
                _searchRow.SetActive(alreadyLoggedIn);

                _spotifySection.SetActive(true);

                // UI เพิ่งถูกสร้างใหม่ทั้งชุด (เกม destroy panel เก่าทิ้งตอนปิดเมนู) -> _queueList ตัวใหม่ยังว่างอยู่
                // ต้อง reset ตัวจำ playlist เพื่อบังคับให้รอบ poll ถัดไปเรียก RefreshPlaylist มาเติมใหม่
                // (ฝั่ง SpotifyWebApi ยัง cache ข้อมูลไว้อยู่ เลยได้ของจาก cache ทันทีโดยไม่เปลือง API call เพิ่ม)
                _lastSeenPlaylistContextId = null;

                Plugin.Log.LogInfo("[SpotifyPatches] Spotify section injected.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyPatches] Inject failed: {ex}");
            }
        }

        public static void OnActivate()
        {
            if (_spotifySection == null || _cachedScrollRect == null)
            {
                Plugin.Log.LogWarning($"[SpotifyPatches] OnActivate skipped: _spotifySection={(_spotifySection == null ? "null" : "ok")}, _cachedScrollRect={(_cachedScrollRect == null ? "null" : "ok")}");
                return;
            }
            try
            {
                _spotifySection.SetActive(true);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_cachedScrollRect.content);

                RectTransform rt = _spotifySection.GetComponent<RectTransform>();
                Transform parent = _spotifySection.transform.parent;

                RectTransform referenceRow = null;
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child != _spotifySection.transform) { referenceRow = child as RectTransform; break; }
                }

                if (referenceRow != null)
                {
                    rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
                    rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
                    rt.offsetMin = new Vector2(referenceRow.offsetMin.x, rt.offsetMin.y);
                    rt.offsetMax = new Vector2(referenceRow.offsetMax.x, rt.offsetMax.y);
                }

                // ไม่ poll ตามเวลาแล้ว ยิงแค่ครั้งแรกตอนเปิดเมนู จากนั้นจะยิงอีกทีก็ต่อเมื่อ:
                // (1) user กดปุ่ม prev/play-pause/next เอง, หรือ (2) เพลงจบตามที่ interpolate ในเครื่องคำนวณไว้
                SafeFireAndForget(RefreshNowPlaying());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyPatches] OnActivate failed: {ex}");
            }
        }

        // === ปุ่มควบคุม ===
        private static async Task OnPrevClicked()
        {
            await SpotifyApi.Previous();
            await Task.Delay(300);
            await RefreshNowPlaying();
        }

        private static async Task OnPlayPauseClicked()
        {
            bool targetIsPlaying = !_lastKnownIsPlaying;
            bool ok = await SpotifyApi.PlayPause(_lastKnownIsPlaying);

            // สั่งสำเร็จ + มีข้อมูลเพลงอยู่แล้ว -> ไม่ต้องยิง GET ตามหลัง เพราะสิ่งเดียวที่เปลี่ยนคือ is_playing
            // ซึ่งรู้ผลอยู่แล้ว แค่สลับสถานะในเครื่องพอ (ถ้าสั่งพลาด เช่น state ไม่ตรงกับเครื่องอื่น ค่อย resync)
            if (ok && _isInterpolating)
            {
                Plugin.RunOnMainThread(() => ApplyLocalPlayPause(targetIsPlaying));
                return;
            }

            await Task.Delay(300);
            await RefreshNowPlaying();
        }

        // สลับ play/pause ในเครื่อง: ตรึงตำแหน่งเพลง ณ ตอนนี้เป็นจุด anchor ใหม่ให้ Tick() นับต่อ/หยุดนับ
        private static void ApplyLocalPlayPause(bool nowPlaying)
        {
            if (_lastKnownIsPlaying)
            {
                TimeSpan pos = _syncedPosition + (DateTime.UtcNow - _syncedAtUtc);
                if (_syncedDuration > TimeSpan.Zero && pos > _syncedDuration) pos = _syncedDuration;
                _syncedPosition = pos;
            }
            _syncedAtUtc = DateTime.UtcNow;
            _lastKnownIsPlaying = nowPlaying;
            if (_playPauseLabel != null) _playPauseLabel.text = nowPlaying ? "||" : ">";
        }

        private static async Task OnNextClicked()
        {
            await SpotifyApi.Next();
            await Task.Delay(300);
            await RefreshNowPlaying();
        }

        private static void OnConnectClicked()
        {
            _statusText.text = "";
            SpotifyAuth.StartLogin(OnLoginSuccess, OnLoginFailed);
        }

        private static void OnLoginSuccess()
        {
            _connectRow.SetActive(false);
            _controlsRow.SetActive(true);
            _playlistHeader.SetActive(true);
            _queueList.SetActive(true);
            if (_searchRow != null) _searchRow.SetActive(true);
            SafeFireAndForget(RefreshNowPlaying());
        }

        private static void OnLoginFailed(string error)
        {
            _statusText.text = "Connect failed: " + error;
        }

        // เรียกจาก RefreshNowPlaying เท่านั้น เมื่อ context playlist เปลี่ยนไปจากที่จำไว้
        // ไม่มี timer แยกสำหรับ playlist อีกต่อไป - ใช้ playlistId ที่ parse มาจาก /me/player
        // call เดียวกันกับ now-playing เลย ไม่ต้องยิง endpoint แยก
        private static string _lastSeenPlaylistContextId;

        // กดปุ่ม ↻ ที่ header: ล้าง cache แล้วดึงคิวล่าสุดของ playlist ปัจจุบันใหม่ทั้งชุด
        private static async Task ForceRefreshQueue()
        {
            if (!SpotifyAuth.IsLoggedIn) return;
            SpotifyWebApi.InvalidateCache();
            _lastSeenPlaylistContextId = null; // บังคับให้ RefreshNowPlaying โหลด playlist ใหม่รอบนี้เลย
            await RefreshNowPlaying();
        }

        // คืน true เมื่อได้ playlist พร้อม track list มาจริงๆ (ให้ผู้เรียกตัดสินใจว่าจะ commit ว่าโหลดแล้วหรือรอ retry)
        private static async Task<bool> RefreshPlaylist(string playlistContextId)
        {
            // 21 = เพลงปัจจุบัน + คิวอีก 20 ซึ่งเป็นเพดานสูงสุดที่ /me/player/queue ให้มา (ไม่มี pagination ต่อ)
            PlaylistInfo playlist = await SpotifyWebApi.GetCurrentPlaylistAsync(playlistContextId, maxTracks: 21);
            Plugin.RunOnMainThread(() =>
            {
                ApplyPlaylist(playlist);
                // ต้องบังคับ rebuild ไม่งั้น ScrollRect content ไม่ขยายตามแถวที่เพิ่งเพิ่ม เพลงเลยดูเหมือนไม่ขึ้น
                if (_cachedScrollRect != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_cachedScrollRect.content);
            });
            return playlist != null && playlist.Id == playlistContextId && playlist.Tracks != null;
        }

        private static void ApplyPlaylist(PlaylistInfo playlist)
        {
            if (_queueList == null) return;

            if (playlist == null)
            {
                // ไม่ได้เล่นจาก playlist อยู่ตอนนี้ (เช่น เล่นจาก album/liked songs) -> เคลียร์ของเก่าทิ้ง
                if (_playlistNameText != null) _playlistNameText.text = "Not playing from a playlist";
                if (_playlistSubText != null) _playlistSubText.gameObject.SetActive(false);
                ClearChildren(_queueList.transform);
                _queueRowTitles.Clear();
                return;
            }

            if (_playlistNameText != null) _playlistNameText.text = playlist.Name ?? "-";
            if (_playlistSubText != null) _playlistSubText.gameObject.SetActive(true);

            if (playlist.CoverImageBytes != null && playlist.CoverImageBytes.Length > 0)
            {
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(playlist.CoverImageBytes) && _playlistImage != null)
                {
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    _playlistImage.sprite = sprite;
                    _playlistImage.color = Color.white; // ล้าง tint เทาของ placeholder ไม่งั้นรูปโดนคูณสีจนคล้ำ
                }
            }
            else if (_playlistImage != null)
            {
                // ไม่มีปกมาด้วย -> รีเซ็ตกลับ placeholder กันภาพปกของ playlist ก่อนหน้าค้างแสดงผิดอัน
                _playlistImage.sprite = null;
                _playlistImage.color = new Color(0.16f, 0.15f, 0.20f, 1f);
            }

            ClearChildren(_queueList.transform);
            _queueRowTitles.Clear();

            Plugin.Log.LogInfo($"[SpotifyPatches] ApplyPlaylist: '{playlist.Name}' tracks={playlist.Tracks?.Count ?? -1}, " +
                $"queueActive={_queueList.activeInHierarchy}, sectionActive={(_spotifySection != null && _spotifySection.activeInHierarchy)}");

            if (playlist.Tracks == null || playlist.Tracks.Count == 0)
            {
                // โหลดรายชื่อเพลงไม่ได้ (เช่น Daily Mix / Discover Weekly ที่ Spotify ปิด API access ไปแล้ว)
                Text msg = CreateText(_queueList.transform, "Track list not available for this playlist", 11, TextAnchor.MiddleLeft);
                msg.color = TextFaint;
                return;
            }

            for (int i = 0; i < playlist.Tracks.Count; i++)
            {
                PlaylistTrackInfo t = playlist.Tracks[i];
                string capturedTrackId = t.Id;
                string capturedContextUri = playlist.ContextUri;
                GameObject row = new GameObject("PlaylistRow_" + i);
                row.transform.SetParent(_queueList.transform, worldPositionStays: false);
                row.AddComponent<RectTransform>();
                LayoutElement rowLe = row.AddComponent<LayoutElement>();
                rowLe.preferredHeight = 30f;

                HorizontalLayoutGroup rowHlg = row.AddComponent<HorizontalLayoutGroup>();
                rowHlg.childForceExpandWidth = false;
                rowHlg.childControlWidth = true;
                rowHlg.childControlHeight = true;
                rowHlg.spacing = 6f;
                rowHlg.childAlignment = TextAnchor.MiddleLeft;

                if (!string.IsNullOrEmpty(capturedTrackId))
                {
                    Image rowBg = row.AddComponent<Image>();
                    rowBg.sprite = UiSprites.Rounded; // hover เป็นแถบมุมโค้งแบบแถวรายการของเกม
                    rowBg.type = Image.Type.Sliced;
                    rowBg.color = Color.white; // alpha จริงมาจาก ColorBlock - ถ้าตั้ง alpha 0 ตรงนี้ tint จะโดนคูณจนมองไม่เห็น
                    Button rowBtn = row.AddComponent<Button>();
                    ColorBlock cb = rowBtn.colors;
                    cb.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
                    cb.pressedColor = new Color(1f, 1f, 1f, 0.15f);
                    cb.normalColor = new Color(1f, 1f, 1f, 0f);
                    cb.selectedColor = new Color(1f, 1f, 1f, 0f); // กันแถวติดสว่างค้างหลังกด (default 0.96)
                    rowBtn.colors = cb;
                    rowBtn.targetGraphic = rowBg;
                    rowBtn.onClick.AddListener(() => SafeFireAndForget(PlayTrackInPlaylist(capturedContextUri, capturedTrackId)));
                }

                CreateInlineText(row.transform, (i + 1).ToString(), 18f);

                GameObject nameCol = new GameObject("NameCol");
                nameCol.transform.SetParent(row.transform, worldPositionStays: false);
                nameCol.AddComponent<RectTransform>();
                LayoutElement nameColLe = nameCol.AddComponent<LayoutElement>();
                nameColLe.flexibleWidth = 1f;
                VerticalLayoutGroup nameColVlg = nameCol.AddComponent<VerticalLayoutGroup>();
                nameColVlg.childControlWidth = true;
                nameColVlg.childControlHeight = true;
                nameColVlg.childForceExpandWidth = true;

                Text nameText = CreateText(nameCol.transform, t.Title ?? "-", 12, TextAnchor.MiddleLeft);
                Text artistText = CreateText(nameCol.transform, t.Artist ?? "-", 10, TextAnchor.MiddleLeft);
                artistText.color = TextFaint;

                if (!string.IsNullOrEmpty(capturedTrackId))
                    _queueRowTitles.Add((capturedTrackId, nameText));

                TimeSpan dur = TimeSpan.FromMilliseconds(t.DurationMs);
                CreateInlineText(row.transform, FormatTime(dur), 40f);
            }

            UpdateQueueHighlight();
        }

        // ทาสีชื่อเพลงที่ trackId ตรงกับเพลงที่กำลังเล่นอยู่ (_currentTrackId) เป็นเขียว Spotify ส่วนเพลงอื่นคืนสีขาวปกติ
        // แยกออกมาต่างหากจาก ApplyPlaylist เพราะเพลงเปลี่ยนบ่อยกว่ารายชื่อ queue มาก ไม่ต้อง rebuild ทั้ง list ทุกครั้ง
        private static void UpdateQueueHighlight()
        {
            foreach (var (trackId, title) in _queueRowTitles)
            {
                if (title == null) continue; // แถวโดน destroy ไปแล้ว (Unity เทียบ null ได้กับ destroyed object)
                title.color = (!string.IsNullOrEmpty(_currentTrackId) && trackId == _currentTrackId)
                    ? ButtonActive // เขียว Spotify ตัวเดียวกับ progress bar
                    : Color.white;
            }
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        public static async Task RefreshNowPlaying()
        {
            Plugin.Log.LogInfo("[SpotifyPatches] RefreshNowPlaying: calling GetCurrentlyPlaying...");
            SpotifyNowPlayingInfo info = await SpotifyApi.GetCurrentlyPlaying();
            Plugin.Log.LogInfo(info == null
                ? "[SpotifyPatches] RefreshNowPlaying: GetCurrentlyPlaying returned null"
                : $"[SpotifyPatches] RefreshNowPlaying: got '{info.Title}' by '{info.Artist}'");

            // ทุกอย่างที่แตะ Unity API (Text, Image, Texture2D) ต้องรันบน main thread เท่านั้น
            Plugin.RunOnMainThread(() => ApplyNowPlaying(info));

            // เช็ค playlist เปลี่ยนไหม "ต่อพ่วง" จาก call เดียวกันนี้เลย ไม่ยิง endpoint แยกอีกต่างหาก
            // และไม่มี timer คอยเช็คเป็นระยะแล้ว จะเช็คเฉพาะตอนที่ยังไงก็ต้องยิง now-playing อยู่แล้วเท่านั้น
            string playlistId = info?.PlaylistContextId;
            if (SpotifyAuth.IsLoggedIn && playlistId != _lastSeenPlaylistContextId)
            {
                bool loaded = await RefreshPlaylist(playlistId);
                // commit เฉพาะตอนโหลดสำเร็จ (หรือไม่มี playlist ให้โหลด) - ถ้าพลาดปล่อยให้รอบ poll หน้า retry เอง
                if (loaded || string.IsNullOrEmpty(playlistId))
                    _lastSeenPlaylistContextId = playlistId;
            }
        }

        private static void ApplyNowPlaying(SpotifyNowPlayingInfo info)
        {
            if (_trackTitleText == null)
            {
                // Unity เช็ค null แบบ overload เอง ถ้า GameObject เดิมถูก Destroy ไปแล้ว
                // ค่านี้จะเป็น null ทันทีแม้ field จะยังไม่ได้ set = null เอง
                Plugin.Log.LogWarning("[SpotifyPatches] ApplyNowPlaying: _trackTitleText is null/destroyed - UI section ของเก่าตายไปแล้ว ต้อง re-inject ใหม่");
                return;
            }

            if (info == null || string.IsNullOrEmpty(info.Title))
            {
                _lastKnownIsPlaying = false;
                _isInterpolating = false;
                if (_trackTitleText != null)
                    _trackTitleText.text = "Connect Spotify and play a song on any device to see controls";
                if (_artistText != null) _artistText.text = "";
                if (_posText != null) _posText.text = "0:00";
                if (_durText != null) _durText.text = "0:00";
                if (_progressSlider != null) _progressSlider.value = 0f;
                _currentTrackId = null;
                UpdateQueueHighlight();
                return;
            }

            _lastKnownIsPlaying = info.IsPlaying;

            // highlight ชื่อเพลงที่กำลังเล่นใน queue list ให้ตรงกับเพลงปัจจุบันเสมอ
            if (_currentTrackId != info.TrackId)
            {
                _currentTrackId = info.TrackId;
                UpdateQueueHighlight();
            }

            if (_trackTitleText != null) _trackTitleText.text = info.Title;
            if (_artistText != null) _artistText.text = info.Artist ?? "-";

            // ตั้ง "จุด sync" ใหม่จากข้อมูลจริงที่เพิ่ง poll มา ให้ Tick() คำนวณ interpolate ต่อจากตรงนี้
            // แทนที่จะ set slider ตรงๆ ทุกครั้งที่ poll (ซึ่งจะทำให้ progress ขยับเป็นสเต็ปทุก 5 วิ)
            _syncedPosition = info.Position;
            _syncedDuration = info.Duration;
            _syncedAtUtc = DateTime.UtcNow;
            _isInterpolating = true;
            _songEndTriggerFired = false; // เพลงใหม่มาแล้ว (หรือ resync) รีเซ็ตให้ตรวจจับเพลงจบรอบถัดไปได้อีก
            EnsureSubscribedToTick();

            if (_playPauseLabel != null) _playPauseLabel.text = info.IsPlaying ? "||" : ">";

            // โหลด cover art ก็ต้องอยู่บน main thread ด้วย (สร้าง Texture2D ใหม่)
            // ข้ามได้เมื่อเป็น bytes ชุดเดิม (reference เดิมจาก cache) และปกยังแสดงอยู่บนจอ
            if (info.ThumbnailBytes != null && info.ThumbnailBytes.Length > 0
                && (!ReferenceEquals(info.ThumbnailBytes, _lastAppliedCoverBytes)
                    || _coverImage == null || _coverImage.sprite == null))
            {
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(info.ThumbnailBytes))
                {
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    if (_coverImage != null)
                    {
                        _coverImage.sprite = sprite;
                        _coverImage.color = Color.white; // ล้าง tint เทาของ placeholder ไม่งั้นรูปโดนคูณสีจนคล้ำ
                        _lastAppliedCoverBytes = info.ThumbnailBytes;
                    }
                }
            }
        }

        // ใช้ Canvas.willRenderCanvases แทน Update() ของ component เอง (เกมนี้ไม่เรียก Update()
        // ของ component ปกติด้วยเหตุผลที่ไม่ทราบแน่ชัด ตรวจสอบไปแล้วก่อนหน้านี้) เป็น global engine
        // event ที่ทำงานทุกเฟรมแน่นอน ไม่ต้องพึ่ง MonoBehaviour lifecycle ของเราเลย
        private static void EnsureSubscribedToTick()
        {
            if (_subscribedToTick) return;
            _subscribedToTick = true;
            Canvas.willRenderCanvases += TickProgressBar;
        }

        // รันทุกเฟรม คำนวณตำแหน่งเพลงเองจากเวลาจริงที่ผ่านไป ไม่ยิง API เพิ่มเลย
        // ค่าจริงจาก Spotify (ที่ poll ทุก 5 วิ) จะมา sync ทับจุด anchor นี้ใหม่ทุกครั้งใน ApplyNowPlaying
        private static void TickProgressBar()
        {
            if (!_isInterpolating || _posText == null || _durText == null || _progressSlider == null)
                return;

            TimeSpan displayPosition = _syncedPosition;
            bool reachedEnd = false;

            if (_lastKnownIsPlaying)
            {
                TimeSpan elapsed = DateTime.UtcNow - _syncedAtUtc;
                displayPosition = _syncedPosition + elapsed;
                if (_syncedDuration > TimeSpan.Zero && displayPosition >= _syncedDuration)
                {
                    displayPosition = _syncedDuration;
                    reachedEnd = true;
                }
            }

            _posText.text = FormatTime(displayPosition);
            _durText.text = FormatTime(_syncedDuration);

            double total = _syncedDuration.TotalSeconds;
            _progressSlider.value = total > 0 ? (float)(displayPosition.TotalSeconds / total) : 0f;

            // เพลงจบตามนาฬิกาเราเอง (ยังไม่ได้ยิง API เลยสักครั้งตอนนี้) -> ยิงครั้งเดียวเพื่อดึงเพลงถัดไป
            // ที่ Spotify auto-advance ไปแล้วจริงๆ ตอนนี้ ป้องกันยิงซ้ำทุกเฟรมด้วย _songEndTriggerFired
            if (reachedEnd && !_songEndTriggerFired)
            {
                _songEndTriggerFired = true;
                _isInterpolating = false; // หยุด interpolate ไว้ก่อน รอข้อมูลเพลงใหม่มา sync ทับ
                SafeFireAndForget(RefreshNowPlaying());
            }
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            return $"{t.Minutes}:{t.Seconds:00}";
        }

        private static async void SafeFireAndForget(Task task)
        {
            try { await task; }
            catch (Exception ex) { Plugin.Log.LogError($"[SpotifyPatches] Async error: {ex}"); }
        }

        // === Helpers ===
        private static Slider CreateProgressSlider(Transform parent)
        {
            GameObject go = new GameObject("ProgressSlider");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 8f;

            Slider slider = go.AddComponent<Slider>();
            slider.interactable = false; // แค่แสดงผล ไม่ให้ user ลากเปลี่ยนตำแหน่งเพลง (Spotify API ตัวนี้ยังไม่รองรับ seek)
            slider.transition = Selectable.Transition.None;

            // แถบจริงสูง 6px หัวท้ายมน วางกึ่งกลางแนวตั้งของพื้นที่ slider
            GameObject bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f); bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 6f);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = UiSprites.Bar;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(1f, 1f, 1f, 0.22f);

            GameObject fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f); fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.sizeDelta = new Vector2(0f, 6f);

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillArea.transform, worldPositionStays: false);
            RectTransform fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f); fillRt.anchorMax = new Vector2(0f, 1f); fillRt.sizeDelta = new Vector2(10f, 0f);
            Image fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = UiSprites.Bar;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = ButtonActive;

            slider.fillRect = fillRt;
            slider.targetGraphic = fillImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0f;

            return slider;
        }

        private static Text CreateInlineText(Transform parent, string content, float width)
        {
            GameObject go = new GameObject("Time");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = TextSecondary;
            text.font = _arialFont;
            text.raycastTarget = false;
            return text;
        }

        // ปุ่มวงกลมภาษาเดียวกับปุ่มไอคอนของเกม:
        // solid = วงกลมขาวทึบ + glyph เข้ม (ปุ่มหลักอย่าง play/pause กลาง transport)
        // ไม่ solid = วงแหวนขอบขาว พื้นใส ชี้/กดแล้วมีวงกลมขาวจางโผล่ข้างใน
        private static Button CreateCircleButton(Transform parent, string label, float size, bool solid, Color? ringColor = null)
        {
            GameObject go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.minWidth = size;
            le.preferredHeight = size; le.minHeight = size;

            Image shape = go.AddComponent<Image>();
            shape.sprite = solid ? UiSprites.Circle : UiSprites.Ring;
            shape.preserveAspect = true;
            shape.color = solid ? Color.white : (ringColor ?? LineColor);

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            if (solid)
            {
                btn.targetGraphic = shape;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
                cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
                cb.selectedColor = Color.white; // default 0.96 ขาวเกือบทึบ - ถ้าไม่ตั้งปุ่มจะติดสว่างค้างหลังกด
            }
            else
            {
                // วงแหวนคงที่ตลอด feedback เป็นวงกลมสีเข้มจางๆ โผล่ข้างใน (ใช้สีเข้มเพื่อไม่ให้กลืน glyph ขาว)
                GameObject pressGo = new GameObject("PressFill");
                pressGo.transform.SetParent(go.transform, worldPositionStays: false);
                RectTransform pressRt = pressGo.AddComponent<RectTransform>();
                pressRt.anchorMin = Vector2.zero; pressRt.anchorMax = Vector2.one; pressRt.sizeDelta = Vector2.zero;
                Image press = pressGo.AddComponent<Image>();
                press.sprite = UiSprites.Circle;
                press.preserveAspect = true;
                press.raycastTarget = false;
                press.color = Color.black; // tint จาก ColorBlock คุมแค่ alpha - ตัว fill เป็นสีเข้มเสมอ
                btn.targetGraphic = press;
                cb.normalColor = new Color(1f, 1f, 1f, 0f);
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
                cb.pressedColor = new Color(1f, 1f, 1f, 0.35f);
                cb.selectedColor = new Color(1f, 1f, 1f, 0f); // กลับหายทันทีหลังปล่อย ไม่ติดค้าง
            }
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;

            Text text = labelGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = Mathf.RoundToInt(size * 0.36f);
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = solid ? new Color(0.05f, 0.05f, 0.09f, 1f) : Color.white;
            text.font = _arialFont;
            text.raycastTarget = false;

            return btn;
        }

        // ปุ่ม pill ตามภาษาเกม: filled = พื้นทึบสี accent + ตัวหนังสือเข้ม (ปุ่มหลัก เช่น Connect เขียว Spotify)
        // outline = ขอบขาวพื้นใส + ตัวหนังสือขาว (ปุ่มรอง เช่น Search / My Lists)
        private static Button CreatePillButton(Transform parent, string label, bool filled, Color accent, float height = 30f)
        {
            GameObject go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            Image img = go.AddComponent<Image>();
            img.sprite = filled ? UiSprites.Pill : UiSprites.PillOutline;
            img.type = Image.Type.Sliced;
            img.color = filled ? accent : LineColor;

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            if (filled)
            {
                // สีจริงมาจาก img.color แล้วโดน ColorBlock คูณทับ - ขาวล้วน = สีเดิม, เทา = เข้มลงตอนกด
                btn.targetGraphic = img;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
                cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                cb.selectedColor = Color.white; // default 0.96 - กันปุ่มติดสีค้างหลังกด
            }
            else
            {
                GameObject pressGo = new GameObject("PressFill");
                pressGo.transform.SetParent(go.transform, worldPositionStays: false);
                RectTransform pressRt = pressGo.AddComponent<RectTransform>();
                pressRt.anchorMin = Vector2.zero; pressRt.anchorMax = Vector2.one; pressRt.sizeDelta = Vector2.zero;
                Image press = pressGo.AddComponent<Image>();
                press.sprite = UiSprites.Pill;
                press.type = Image.Type.Sliced;
                press.raycastTarget = false;
                press.color = Color.black; // fill สีเข้ม กันกลืนตัวหนังสือขาวตอนกด
                btn.targetGraphic = press;
                cb.normalColor = new Color(1f, 1f, 1f, 0f);
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
                cb.pressedColor = new Color(1f, 1f, 1f, 0.35f);
                cb.selectedColor = new Color(1f, 1f, 1f, 0f);
            }
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;

            Text text = labelGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = 12;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = filled ? new Color(0.02f, 0.09f, 0.045f, 1f) : Color.white;
            text.font = _arialFont;
            text.raycastTarget = false;

            return btn;
        }

        // พยายามใช้ฟอนต์ UI ของเกมเองให้ section กลมกลืน - หาไม่ได้ค่อย fallback เป็น Arial
        // รับเฉพาะ dynamic font ที่มีตัวอักษรพื้นฐานครบ กันไปหยิบ icon font แล้วตัวหนังสือพัง
        private static Font FindUiFont()
        {
            try
            {
                foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
                {
                    if (f == null || !f.dynamic) continue;
                    string n = f.name.ToLowerInvariant();
                    if (n.Contains("arial")) continue;
                    if (!f.HasCharacter('A') || !f.HasCharacter('g') || !f.HasCharacter('0') || !f.HasCharacter(':'))
                        continue;
                    Plugin.Log.LogInfo($"[SpotifyPatches] ใช้ฟอนต์ของเกม: {f.name}");
                    return f;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SpotifyPatches] หาฟอนต์ของเกมไม่สำเร็จ: {ex.Message}");
            }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static async Task OnSearchClicked()
        {
            if (_searchInput == null || string.IsNullOrWhiteSpace(_searchInput.text)) return;
            string query = _searchInput.text.Trim();
            Plugin.Log.LogInfo($"[SpotifyPatches] Search: '{query}'");

            SpotifySearchResults results = await SpotifySearchApi.SearchAsync(query, limitPerType: 5);
            Plugin.RunOnMainThread(() => BuildSearchResults(results));
        }

        // toggle รายชื่อ playlist ของ user ในพื้นที่ผลค้นหา: กดครั้งแรกแสดง กดซ้ำหุบกลับ
        // ข้อมูลถูก cache ทั้ง session ฝั่ง SpotifyWebApi - กดกี่รอบก็ยิง API แค่ครั้งแรกครั้งเดียว
        private static bool _showingMyPlaylists;

        private static async Task OnMyPlaylistsClicked()
        {
            if (!SpotifyAuth.IsLoggedIn) return;

            if (_showingMyPlaylists)
            {
                _showingMyPlaylists = false;
                Plugin.RunOnMainThread(() =>
                {
                    if (_searchResultsList == null) return;
                    ClearChildren(_searchResultsList.transform);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_searchResultsList.GetComponent<RectTransform>());
                    if (_cachedScrollRect != null)
                        LayoutRebuilder.ForceRebuildLayoutImmediate(_cachedScrollRect.content);
                });
                return;
            }

            System.Collections.Generic.List<UserPlaylistInfo> playlists =
                await SpotifyWebApi.GetMyPlaylistsAsync(limit: 20);
            Plugin.RunOnMainThread(() => BuildMyPlaylistRows(playlists));
        }

        private static void BuildMyPlaylistRows(System.Collections.Generic.List<UserPlaylistInfo> playlists)
        {
            if (_searchResultsList == null) return;
            ClearChildren(_searchResultsList.transform);
            _showingMyPlaylists = true;

            CreateSectionLabel(_searchResultsList.transform, "My Playlists");

            if (playlists == null || playlists.Count == 0)
            {
                Text msg = CreateText(_searchResultsList.transform,
                    playlists == null ? "Failed to load playlists, try again" : "No playlists in this account",
                    11, TextAnchor.MiddleLeft);
                msg.color = new Color(0.65f, 0.65f, 0.65f, 1f);
            }
            else
            {
                foreach (UserPlaylistInfo p in playlists)
                {
                    UserPlaylistInfo captured = p;
                    // สั่งเล่นทั้ง playlist ผ่าน context_uri (อ่าน track list ตรงๆ โดนบล็อกใน dev mode อยู่แล้ว)
                    // พอเริ่มเล่น RefreshNowPlaying จะเห็น context ใหม่แล้วอัปเดตชื่อ/ปก/คิวให้เองอัตโนมัติ
                    BuildSearchRow(_searchResultsList.transform,
                        p.Name, $"{p.TrackCount} tracks", null,
                        () => SafeFireAndForget(PlayContext($"spotify:playlist:{captured.Id}")));
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_searchResultsList.GetComponent<RectTransform>());
            if (_cachedScrollRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_cachedScrollRect.content);
        }

        // onEndEdit ยิงทั้งตอนกด Enter และตอน field เสีย focus (คลิกที่อื่น)
        // เลยต้องเช็คว่าเป็น Enter จริงๆ ก่อนค่อยยิง search (ISubmitHandler ใช้ไม่ได้ - event system ของเกมไม่ส่ง Submit มา)
        private static void OnSearchInputEndEdit(string text)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                SafeFireAndForget(OnSearchClicked());
        }

        private static void OnSearchTextChanged(string newText)
        {
            if (string.IsNullOrWhiteSpace(newText) && _searchResultsList != null)
            {
                _showingMyPlaylists = false; // list โดนเคลียร์ toggle ต้องกลับสถานะ "หุบ" ด้วย
                ClearChildren(_searchResultsList.transform);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_searchResultsList.GetComponent<RectTransform>());
                if (_cachedScrollRect != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_cachedScrollRect.content);
            }
        }

        private static void BuildSearchResults(SpotifySearchResults results)
        {
            if (_searchResultsList == null) return;
            _showingMyPlaylists = false; // ผลค้นหาเข้ามาแทนที่รายชื่อ playlist แล้ว
            ClearChildren(_searchResultsList.transform);

            bool anyResults = (results.Tracks.Count + results.Artists.Count +
                               results.Albums.Count + results.Playlists.Count) > 0;
            if (!anyResults) return;

            // --- Tracks ---
            if (results.Tracks.Count > 0)
            {
                CreateSectionLabel(_searchResultsList.transform, "Tracks");
                foreach (SearchTrackResult t in results.Tracks)
                {
                    SearchTrackResult captured = t;
                    string dur = FormatTime(TimeSpan.FromMilliseconds(t.DurationMs));
                    BuildSearchRow(_searchResultsList.transform,
                        t.Title, t.Artist, dur,
                        () => SafeFireAndForget(PlayTrack(captured.Id)));
                }
            }

            // --- Artists ---
            if (results.Artists.Count > 0)
            {
                CreateSectionLabel(_searchResultsList.transform, "Artists");
                foreach (SearchArtistResult a in results.Artists)
                {
                    // กด artist -> ไม่ support play directly ผ่าน Web API เลยแสดง top tracks แทน (ข้าม implementation ณ ตอนนี้)
                    BuildSearchRow(_searchResultsList.transform, a.Name, "Artist", null, null);
                }
            }

            // --- Albums ---
            if (results.Albums.Count > 0)
            {
                CreateSectionLabel(_searchResultsList.transform, "Albums");
                foreach (SearchAlbumResult al in results.Albums)
                {
                    SearchAlbumResult captured = al;
                    BuildSearchRow(_searchResultsList.transform,
                        al.Name, al.ArtistName, null,
                        () => SafeFireAndForget(LoadAlbumTracks(captured.Id, captured.Name, captured.CoverUrl)));
                }
            }

            // --- Playlists ---
            if (results.Playlists.Count > 0)
            {
                CreateSectionLabel(_searchResultsList.transform, "Playlists");
                foreach (SearchPlaylistResult p in results.Playlists)
                {
                    SearchPlaylistResult captured = p;
                    // สั่งเล่น playlist เลยแทนการโหลดรายชื่อเพลงมาแสดง (อ่าน track list ตรงๆ โดน Spotify
                    // บล็อกสำหรับ dev-mode app อยู่แล้ว) - พอเริ่มเล่น RefreshNowPlaying จะเห็น context ใหม่
                    // แล้วอัปเดตชื่อ/ปก/คิวเพลงของ playlist นี้ในหน้า playlist ให้เองอัตโนมัติ
                    BuildSearchRow(_searchResultsList.transform,
                        p.Name, p.OwnerName ?? "-", null,
                        () => SafeFireAndForget(PlayContext($"spotify:playlist:{captured.Id}")));
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(
                _searchResultsList.GetComponent<RectTransform>());
        }

        private static async Task PlayTrack(string trackId)
        {
            if (!SpotifyAuth.IsLoggedIn) return;
            // สั่ง play ผ่าน Web API โดยระบุ track uri ตรงๆ ไม่ต้องรอ context
            string path = "me/player/play";
            if (!string.IsNullOrEmpty(SpotifyApi.LastKnownDeviceId))
                path += $"?device_id={SpotifyApi.LastKnownDeviceId}";
            string body = $"{{\"uris\":[\"spotify:track:{trackId}\"]}}";
            await SpotifyApi.SendPlayBody(path, body);
            await Task.Delay(300);
            await RefreshNowPlaying();
        }

        // สั่งเล่นทั้ง context (playlist/album) ตั้งแต่ต้น - ใช้กับการกด playlist จากผลค้นหา
        private static async Task PlayContext(string contextUri)
        {
            if (!SpotifyAuth.IsLoggedIn || string.IsNullOrEmpty(contextUri)) return;
            string path = "me/player/play";
            if (!string.IsNullOrEmpty(SpotifyApi.LastKnownDeviceId))
                path += $"?device_id={SpotifyApi.LastKnownDeviceId}";
            string body = $"{{\"context_uri\":\"{contextUri}\"}}";
            await SpotifyApi.SendPlayBody(path, body);
            await Task.Delay(300);
            await RefreshNowPlaying();
        }

        // เล่นเพลงจากตำแหน่งใน playlist/album โดยตรง เพื่อให้ปุ่ม next/prev ยังเดินตาม context เดิมต่อได้
        private static async Task PlayTrackInPlaylist(string contextUri, string trackId)
        {
            if (!SpotifyAuth.IsLoggedIn || string.IsNullOrEmpty(trackId)) return;
            string path = "me/player/play";
            if (!string.IsNullOrEmpty(SpotifyApi.LastKnownDeviceId))
                path += $"?device_id={SpotifyApi.LastKnownDeviceId}";
            string body = string.IsNullOrEmpty(contextUri)
                ? $"{{\"uris\":[\"spotify:track:{trackId}\"]}}"
                : $"{{\"context_uri\":\"{contextUri}\",\"offset\":{{\"uri\":\"spotify:track:{trackId}\"}}}}";
            await SpotifyApi.SendPlayBody(path, body);
            await Task.Delay(300);
            await RefreshNowPlaying();
        }

        private static async Task LoadAlbumTracks(string albumId, string albumName, string coverUrl = null)
        {
            if (!SpotifyAuth.IsLoggedIn) return;
            try
            {
                var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get,
                    $"https://api.spotify.com/v1/albums/{albumId}/tracks?limit=20");
                request.Headers.Add("Authorization", $"Bearer {SpotifyAuth.AccessToken}");
                var resp = await Http.SendAsync(request);
                if (!resp.IsSuccessStatusCode) return;

                string json = await resp.Content.ReadAsStringAsync();
                Newtonsoft.Json.Linq.JObject obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var tracks = new System.Collections.Generic.List<PlaylistTrackInfo>();

                if (obj["items"] is Newtonsoft.Json.Linq.JArray items)
                {
                    foreach (var it in items)
                    {
                        if (it == null) continue;
                        var artists = it["artists"] as Newtonsoft.Json.Linq.JArray;
                        string artist = artists != null && artists.Count > 0
                            ? string.Join(", ", System.Linq.Enumerable.Select(artists, a => (string)a["name"]))
                            : "-";
                        tracks.Add(new PlaylistTrackInfo
                        {
                            Id = (string)it["id"],
                            Title = (string)it["name"],
                            Artist = artist,
                            DurationMs = (int?)it["duration_ms"] ?? 0
                        });
                    }
                }

                // ปกอัลบั้มเอามาจากผลค้นหาที่มี url อยู่แล้ว (endpoint /albums/{id}/tracks ไม่ให้ภาพมาด้วย)
                byte[] coverBytes = null;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    try { coverBytes = await Http.GetByteArrayAsync(coverUrl); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[SpotifyPatches] โหลดปกอัลบั้มพลาด: {ex.Message}"); }
                }

                PlaylistInfo albumInfo = new PlaylistInfo
                {
                    Id = albumId,
                    Name = albumName,
                    Tracks = tracks,
                    CoverImageBytes = coverBytes,
                    ContextUri = $"spotify:album:{albumId}"
                };

                Plugin.RunOnMainThread(() =>
                {
                    if (_playlistHeader != null) _playlistHeader.SetActive(true);
                    if (_queueList != null) _queueList.SetActive(true);
                    ApplyPlaylist(albumInfo);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_cachedScrollRect.content);
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpotifyPatches] LoadAlbumTracks failed: {ex}");
            }
        }

        private static void CreateSectionLabel(Transform parent, string label)
        {
            Text t = CreateText(parent, label.ToUpper(), 10, TextAnchor.MiddleLeft);
            t.color = TextFaint;
        }

        private static void BuildSearchRow(Transform parent, string title, string sub, string right, UnityEngine.Events.UnityAction onClick)
        {
            GameObject row = new GameObject("SearchRow");
            row.transform.SetParent(parent, worldPositionStays: false);
            row.AddComponent<RectTransform>();
            LayoutElement rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 32f;
            rowLe.minHeight = 32f;

            HorizontalLayoutGroup rowHlg = row.AddComponent<HorizontalLayoutGroup>();
            rowHlg.childForceExpandWidth = false;
            rowHlg.childControlWidth = true;
            rowHlg.childControlHeight = true;
            rowHlg.spacing = 6f;
            rowHlg.childAlignment = TextAnchor.MiddleLeft;

            // ถ้ามี action ทำ row ทั้งแถวกดได้
            if (onClick != null)
            {
                Image rowBg = row.AddComponent<Image>();
                rowBg.sprite = UiSprites.Rounded;
                rowBg.type = Image.Type.Sliced;
                rowBg.color = Color.white; // alpha จริงมาจาก ColorBlock
                Button rowBtn = row.AddComponent<Button>();
                ColorBlock cb = rowBtn.colors;
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
                cb.pressedColor = new Color(1f, 1f, 1f, 0.15f);
                cb.normalColor = new Color(1f, 1f, 1f, 0f);
                cb.selectedColor = new Color(1f, 1f, 1f, 0f); // กันแถวติดสว่างค้างหลังกด
                rowBtn.colors = cb;
                rowBtn.targetGraphic = rowBg;
                rowBtn.onClick.AddListener(onClick);
            }

            // title + sub col
            GameObject nameCol = new GameObject("NameCol");
            nameCol.transform.SetParent(row.transform, worldPositionStays: false);
            nameCol.AddComponent<RectTransform>();
            LayoutElement nameColLe = nameCol.AddComponent<LayoutElement>();
            nameColLe.flexibleWidth = 1f;
            VerticalLayoutGroup nameVlg = nameCol.AddComponent<VerticalLayoutGroup>();
            nameVlg.childControlWidth = true;
            nameVlg.childControlHeight = true;
            nameVlg.childForceExpandWidth = true;
            nameVlg.spacing = 1f;

            Text titleText = CreateText(nameCol.transform, title ?? "-", 12, TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(sub))
            {
                Text subText = CreateText(nameCol.transform, sub, 10, TextAnchor.MiddleLeft);
                subText.color = new Color(0.65f, 0.65f, 0.65f, 1f);
            }

            if (!string.IsNullOrEmpty(right))
                CreateInlineText(row.transform, right, 36f);
        }

        private static InputField CreateSearchInputField(Transform parent)
        {
            GameObject go = new GameObject("SearchInput");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 30f;
            le.minHeight = 30f;

            Image bg = go.AddComponent<Image>();
            bg.sprite = UiSprites.Pill; // ช่องค้นหาทรง pill พื้นขาวจางแบบ input ของเกม
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.12f);

            // viewport กัน text ล้นขอบ
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(12f, 2f);
            vpRt.offsetMax = new Vector2(-12f, -2f);
            viewport.AddComponent<RectMask2D>();

            // placeholder
            GameObject placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(viewport.transform, worldPositionStays: false);
            RectTransform phRt = placeholderGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.sizeDelta = Vector2.zero;
            Text placeholder = placeholderGo.AddComponent<Text>();
            placeholder.text = "Search songs, artists, albums…";
            placeholder.fontSize = 11;
            placeholder.color = TextFaint;
            placeholder.font = _arialFont;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.raycastTarget = false;

            // text
            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(viewport.transform, worldPositionStays: false);
            RectTransform textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            Text inputText = textGo.AddComponent<Text>();
            inputText.fontSize = 12;
            inputText.color = Color.white;
            inputText.font = _arialFont;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;
            inputText.raycastTarget = false;

            InputField field = go.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = inputText;
            field.placeholder = placeholder;
            field.caretWidth = 2;
            field.caretColor = Color.white;
            field.selectionColor = new Color(0.11f, 0.73f, 0.33f, 0.4f); // SpotifyGreen โปร่งแสง

            // เคลียร์คำค้นหาแล้วต้องหุบผลลัพธ์กลับ ไม่ใช่รอกด Search ปุ่มอีกที
            field.onValueChanged.AddListener(OnSearchTextChanged);

            // กด Enter ให้ search ได้เลย ไม่ต้องกดปุ่ม Search
            field.onEndEdit.AddListener(OnSearchInputEndEdit);

            return field;
        }

        private static Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<RectTransform>();

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 6;
            le.minHeight = fontSize + 6;

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.font = _arialFont;
            text.raycastTarget = false;

            return text;
        }
    }
}