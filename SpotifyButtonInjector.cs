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
        private static Text _playlistSubText; // "PLAYING FROM <PLAYLIST|ALBUM|ARTIST>" ตามชนิดของ context
                                              // ซ่อนเมื่อไม่ได้เล่นจาก context ใดๆ หรือดูชนิดไม่ออก

        private static InputField _searchInput;
        private static GameObject _searchResultsList;
        private static GameObject _searchRow;

        // แถวใน queue list ทั้งหมด (trackId -> Text ชื่อเพลงของแถวนั้น) เก็บไว้ highlight เพลงที่กำลังเล่นอยู่
        private static readonly System.Collections.Generic.List<(string trackId, Text title)> _queueRowTitles =
            new System.Collections.Generic.List<(string, Text)>();

        // สถานะการเล่น + นาฬิกา interpolate ย้ายไป NowPlayingSession แล้ว (ทดสอบได้โดยไม่พึ่ง Unity)
        private static readonly NowPlayingSession _session = new NowPlayingSession();

        // "แถวไหนโผล่/list โชว์อะไร/ต้อง reflow ไหม" ทั้งหมดเป็นของ PanelViewModel (state machine
        // ล้วน ทดสอบด้วย xUnit ได้) - ไฟล์นี้แค่ป้อน event เข้า VM แล้วเอา state มาเข้า Apply() จุดเดียว
        // ห้ามเรียก VM นอก main thread (event มาจาก background ให้ห่อ Plugin.RunOnMainThread ก่อน)
        private static readonly PanelViewModel _vm = new PanelViewModel();

        // เลข revision ของ list ที่ render ไปแล้ว - Apply จะ rebuild แถวเฉพาะตอนเลขใน state ขยับ
        private static int _appliedQueueRev = -1;
        private static int _appliedResultsRev = -1;

        // จำ bytes ของปกที่แสดงอยู่ (reference เดิมจาก cache ฝั่ง SpotifyApi) กันสร้าง Texture2D ซ้ำทุกรอบ Apply
        private static byte[] _lastAppliedCoverBytes;
        private static byte[] _lastAppliedHeaderCoverBytes;

        private static bool _subscribedToTick;
        private static bool _subscribedToFocus;
        private static DateTime _lastFocusRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan FocusRefreshCooldown = TimeSpan.FromSeconds(3);
        private static bool _songEndTriggerFired;

        // โทนสี/ฟอนต์/ทรงปุ่มทั้งหมดย้ายไป SpotifyUiKit - ไฟล์นี้เหลือแค่ประกอบ layout + ผูก behavior

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
                SpotifyUiKit.ResolveFont();
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
                bg.color = SpotifyUiKit.PanelColor;

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

                Text eyebrow = SpotifyUiKit.CreateText(eyebrowRow.transform, "S P O T I F Y", 11, TextAnchor.MiddleLeft);
                eyebrow.fontStyle = FontStyle.Bold;
                eyebrow.color = SpotifyUiKit.TextSecondary;
                eyebrow.GetComponent<LayoutElement>().preferredWidth = 96f;

                GameObject rule = new GameObject("Rule");
                rule.transform.SetParent(eyebrowRow.transform, worldPositionStays: false);
                rule.AddComponent<RectTransform>();
                LayoutElement ruleLe = rule.AddComponent<LayoutElement>();
                ruleLe.flexibleWidth = 1f;
                ruleLe.preferredHeight = 1f;
                ruleLe.minHeight = 1f;
                Image ruleImg = rule.AddComponent<Image>();
                ruleImg.color = SpotifyUiKit.LineSoft;
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
                _coverImage.color = SpotifyUiKit.CoverPlaceholder; // placeholder เข้มๆ จนกว่าจะโหลดปกอัลบั้มจริง

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

                _trackTitleText = SpotifyUiKit.CreateText(textCol.transform, "Connect Spotify and play a song on any device to see controls", 14, TextAnchor.MiddleLeft);
                _trackTitleText.fontStyle = FontStyle.Bold;
                _artistText = SpotifyUiKit.CreateText(textCol.transform, "", 12, TextAnchor.MiddleLeft);
                _artistText.color = SpotifyUiKit.TextSecondary;

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

                _posText = SpotifyUiKit.CreateInlineText(progressRow.transform, "0:00", 30f);
                _progressSlider = SpotifyUiKit.CreateProgressSlider(progressRow.transform);
                _durText = SpotifyUiKit.CreateInlineText(progressRow.transform, "0:00", 30f);

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
                Button prevBtn = SpotifyUiKit.CreateCircleButton(_controlsRow.transform, "<<", 36f, solid: false);
                Button playPauseBtn = SpotifyUiKit.CreateCircleButton(_controlsRow.transform, "||", 46f, solid: true);
                _playPauseLabel = playPauseBtn.GetComponentInChildren<Text>();
                Button nextBtn = SpotifyUiKit.CreateCircleButton(_controlsRow.transform, ">>", 36f, solid: false);

                prevBtn.onClick.AddListener(() => SafeFireAndForget(OnPrevClicked()));
                playPauseBtn.onClick.AddListener(() => SafeFireAndForget(OnPlayPauseClicked()));
                nextBtn.onClick.AddListener(() => SafeFireAndForget(OnNextClicked()));

                _statusText = SpotifyUiKit.CreateText(_spotifySection.transform, "", 11, TextAnchor.MiddleCenter);
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
                Button connectBtn = SpotifyUiKit.CreatePillButton(_connectRow.transform, "Connect Spotify", filled: true, SpotifyUiKit.ButtonActive, height: 34f);
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
                _playlistImage.color = SpotifyUiKit.CoverPlaceholder;

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

                _playlistNameText = SpotifyUiKit.CreateText(plNameCol.transform, "-", 13, TextAnchor.MiddleLeft);
                _playlistNameText.fontStyle = FontStyle.Bold;
                _playlistSubText = SpotifyUiKit.CreateText(plNameCol.transform, "PLAYING FROM PLAYLIST", 9, TextAnchor.MiddleLeft);
                _playlistSubText.color = SpotifyUiKit.TextFaint;

                // ปุ่ม refresh คิวเพลง (คิวเดินหน้าไปเรื่อยๆ ระหว่างฟัง กดนี้เพื่อดึง snapshot ล่าสุด)
                Button refreshBtn = SpotifyUiKit.CreateCircleButton(_playlistHeader.transform, "↻", 30f, solid: false, ringColor: SpotifyUiKit.LineSoft);
                Text refreshLabel = refreshBtn.GetComponentInChildren<Text>();
                if (refreshLabel != null) { refreshLabel.fontSize = 15; refreshLabel.color = SpotifyUiKit.TextSecondary; }
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

                // flexibleWidth=1 อยู่แล้ว -> ขยายเต็มพื้นที่ที่เหลือ
                _searchInput = SpotifyUiKit.CreateSearchInputField(_searchRow.transform, "Search songs, artists, albums…");
                // เคลียร์คำค้นหาแล้วต้องหุบผลลัพธ์กลับ ไม่ใช่รอกด Search ปุ่มอีกที
                _searchInput.onValueChanged.AddListener(OnSearchTextChanged);
                // กด Enter ให้ search ได้เลย ไม่ต้องกดปุ่ม Search
                _searchInput.onEndEdit.AddListener(OnSearchInputEndEdit);

                // ปุ่มต้องมี preferredWidth ตายตัว ไม่งั้น HLG จะบีบจนหายไป
                Button searchBtn = SpotifyUiKit.CreatePillButton(_searchRow.transform, "Search", filled: false, SpotifyUiKit.ButtonActive);
                LayoutElement searchBtnLe = searchBtn.GetComponent<LayoutElement>();
                searchBtnLe.preferredWidth = 64f;
                searchBtnLe.minWidth = 64f;
                searchBtn.onClick.AddListener(() => SafeFireAndForget(OnSearchClicked()));

                // ปุ่มเรียกดู playlist ของตัวเอง - แสดงผลในพื้นที่เดียวกับผลค้นหา
                Button myListsBtn = SpotifyUiKit.CreatePillButton(_searchRow.transform, "My Lists", filled: false, SpotifyUiKit.ButtonActive);
                LayoutElement myListsBtnLe = myListsBtn.GetComponent<LayoutElement>();
                myListsBtnLe.preferredWidth = 72f;
                myListsBtnLe.minWidth = 72f;
                myListsBtn.onClick.AddListener(() => SafeFireAndForget(OnMyPlaylistsClicked()));

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

                // UI ชุดใหม่ยังไม่เคย render list ใดๆ - รีเซ็ตเลขที่ apply ไว้ให้ rebuild จาก state รอบแรกเสมอ
                _appliedQueueRev = -1;
                _appliedResultsRev = -1;
                _lastAppliedCoverBytes = null;
                _lastAppliedHeaderCoverBytes = null;
                Apply(_vm.ResetForInject(SpotifyAuth.IsLoggedIn));

                _spotifySection.SetActive(true);

                // UI เพิ่งถูกสร้างใหม่ทั้งชุด (เกม destroy panel เก่าทิ้งตอนปิดเมนู) -> _queueList ตัวใหม่ยังว่างอยู่
                // ต้อง reset ตัวจำ context เพื่อบังคับให้รอบ poll ถัดไปเรียก RefreshContext มาเติมใหม่
                // (ฝั่ง SpotifyWebApi ยัง cache ข้อมูลไว้อยู่ เลยได้ของจาก cache ทันทีโดยไม่เปลือง API call เพิ่ม)
                _lastSeenContextUri = null;

                // ติดอาวุธ resync ตอน alt-tab กลับเข้าเกมตั้งแต่ inject เลย (handler กันเองว่ายังไม่ login ก็เฉยๆ)
                // เดิม subscribe ใน ApplyNowPlaying หลัง null check -> เคสใช้งานครั้งแรก (connect ตอนยังไม่มี
                // เพลงเล่น) ไม่เคยถึงบรรทัดนั้น พอไปเปิดเพลงใน Spotify แล้วสลับกลับมา เกมเลยไม่ resync ให้
                EnsureSubscribedToFocus();

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
            string trackBefore = _vm.Current.HighlightedTrackId;
            await SpotifyApi.Previous();
            await RefreshAfterPlay(trackBefore, _lastSeenContextUri);
        }

        private static async Task OnPlayPauseClicked()
        {
            bool ok = await SpotifyApi.PlayPause(_session.IsPlaying);

            // สั่งสำเร็จ + มีข้อมูลเพลงอยู่แล้ว -> ไม่ต้องยิง GET ตามหลัง เพราะสิ่งเดียวที่เปลี่ยนคือ is_playing
            // ซึ่งรู้ผลอยู่แล้ว แค่สลับสถานะในเครื่องพอ (ถ้าสั่งพลาด เช่น state ไม่ตรงกับเครื่องอื่น ค่อย resync)
            if (ok && _session.IsActive)
            {
                Plugin.RunOnMainThread(ApplyLocalPlayPause);
                return;
            }

            await Task.Delay(300);
            await RefreshNowPlaying();
        }

        // สลับ play/pause ในเครื่อง: session ตรึงตำแหน่ง ณ ตอนนี้เป็น anchor ใหม่ให้ Tick() นับต่อ/หยุดนับ
        private static void ApplyLocalPlayPause()
        {
            _session.TogglePlayPause(DateTime.UtcNow);
            Apply(_vm.LocalPlayPauseToggled(_session.IsPlaying));
        }

        private static async Task OnNextClicked()
        {
            string trackBefore = _vm.Current.HighlightedTrackId;
            await SpotifyApi.Next();
            await RefreshAfterPlay(trackBefore, _lastSeenContextUri);
        }

        private static void OnConnectClicked()
        {
            Apply(_vm.ConnectClicked());
            SpotifyAuth.StartLogin(OnLoginSuccess, OnLoginFailed);
        }

        // callback นี้ถูกเรียกจาก continuation ของ StartLoginInternal ซึ่งรันบน thread pool
        // (await GetContextAsync กลับมาไม่ได้อยู่บน main thread) ทุกอย่างที่แตะ Unity UI
        // ต้อง marshal กลับ main thread ก่อน ไม่งั้นจะ throw "can only be called from the main thread"
        // แล้วโดนกลืนเงียบๆ (fire-and-forget) -> browser ขึ้น success แต่ panel ในเกมค้างที่ปุ่ม Connect
        private static void OnLoginSuccess()
        {
            Plugin.RunOnMainThread(() =>
            {
                // VM ตั้ง NeedsReflow ให้เอง (section สูงขึ้นเพราะ header/search โผล่มาแทน connect row)
                Apply(_vm.LoginSucceeded());
                SafeFireAndForget(RefreshNowPlaying());
            });
        }

        private static void OnLoginFailed(string error)
        {
            Plugin.RunOnMainThread(() => Apply(_vm.LoginFailed(error)));
        }

        // เรียกจาก RefreshNowPlaying เท่านั้น เมื่อ context playlist เปลี่ยนไปจากที่จำไว้
        // ไม่มี timer แยกสำหรับ playlist อีกต่อไป - ใช้ playlistId ที่ parse มาจาก /me/player
        // call เดียวกันกับ now-playing เลย ไม่ต้องยิง endpoint แยก
        private static string _lastSeenContextUri;

        // กดปุ่ม ↻ ที่ header: ล้าง cache แล้วดึงคิวล่าสุดของ playlist ปัจจุบันใหม่ทั้งชุด
        private static async Task ForceRefreshQueue()
        {
            if (!SpotifyAuth.IsLoggedIn) return;
            SpotifyWebApi.InvalidateCache();
            _lastSeenContextUri = null; // บังคับให้ RefreshNowPlaying โหลด context ใหม่รอบนี้เลย
            await RefreshNowPlaying();
        }

        // คืน true เมื่อได้รายชื่อเพลงมาจริงๆ (ให้ผู้เรียกตัดสินใจว่าจะ commit ว่าโหลดแล้วหรือรอ retry)
        // แยกตามชนิดของ context: playlist อ่านจาก /playlists/{id} ได้ ส่วน artist/album อ่านไม่ได้แล้ว
        // เลยถอยไปใช้คิวเพลงจาก /me/player/queue แทน (ไม่งั้นหน้า queue จะว่างทั้งที่เพลงเล่นอยู่)
        private static async Task<bool> RefreshContext(SpotifyNowPlayingInfo info)
        {
            string contextUri = info?.ContextUri;
            string playlistContextId = info?.PlaylistContextId;

            if (!string.IsNullOrEmpty(contextUri) && string.IsNullOrEmpty(playlistContextId))
            {
                // album: ทุกเพลงในอัลบั้มใช้ปกเดียวกันอยู่แล้ว ยืมปกของเพลงที่เล่นอยู่มาใช้เป็นปก header ได้เลย
                // artist: ปกจะเปลี่ยนไปตามอัลบั้มของแต่ละเพลง ใช้ไม่ได้ -> ส่ง null แล้วซ่อนช่องรูปแทน
                byte[] cover = SpotifyContext.IsArtist(contextUri) ? null : info?.ThumbnailBytes;
                return await RefreshQueueContext(contextUri, info?.Artist, cover);
            }

            // 21 = เพลงปัจจุบัน + คิวอีก 20 ซึ่งเป็นเพดานสูงสุดที่ /me/player/queue ให้มา (ไม่มี pagination ต่อ)
            PlaylistInfo playlist = await SpotifyWebApi.GetCurrentPlaylistAsync(playlistContextId, maxTracks: 21);
            Plugin.RunOnMainThread(() => Apply(_vm.ContextLoaded(playlist)));
            return playlist != null && playlist.Id == playlistContextId && playlist.Tracks != null;
        }

        // context ที่ไม่ใช่ playlist (artist/album) - เอาคิวเพลงมาแสดงแทนรายชื่อเพลงของ context
        // ถ้าดึงคิวไม่ได้ ปล่อยของเดิมค้างไว้แล้วคืน false เพื่อให้ retry แทนการล้างหน้าจอเป็นค่าว่าง
        private static async Task<bool> RefreshQueueContext(string contextUri, string displayName, byte[] coverBytes)
        {
            PlaylistInfo queueInfo = await SpotifyWebApi.GetContextQueueAsync(contextUri, displayName, coverBytes, maxTracks: 21);
            if (queueInfo == null) return false;

            Plugin.RunOnMainThread(() => Apply(_vm.ContextLoaded(queueInfo)));
            return true;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        // คืนข้อมูลเพลงที่เพิ่งดึงมา ให้ RefreshAfterPlay ใช้เช็คว่า Spotify สลับเพลงให้แล้วหรือยัง
        public static async Task<SpotifyNowPlayingInfo> RefreshNowPlaying()
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
            string contextUri = info?.ContextUri;
            if (SpotifyAuth.IsLoggedIn && contextUri != _lastSeenContextUri)
            {
                bool loaded = await RefreshContext(info);
                // commit เฉพาะตอนโหลดสำเร็จ (หรือไม่มี context ให้โหลด) - ถ้าพลาดปล่อยให้รอบ poll หน้า retry เอง
                if (loaded || string.IsNullOrEmpty(contextUri))
                    _lastSeenContextUri = contextUri;
            }

            return info;
        }

        // Spotify ใช้เวลาครู่หนึ่งกว่าจะสลับเพลง/context หลังรับคำสั่ง play - refresh รอบเดียวหลัง delay สั้นๆ
        // มักยังเห็นของเก่า แล้ว UI จะค้างยาวเพราะไม่มี polling ตามเวลา จึงวนเช็คสูงสุด 4 รอบ (~1.8 วิ)
        // และหยุดทันทีที่เห็นเพลงหรือ context เปลี่ยนไปจากตอนก่อนสั่ง
        private static async Task RefreshAfterPlay(string trackIdBefore, string contextUriBefore)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(attempt == 0 ? 300 : 500);
                SpotifyNowPlayingInfo info = await RefreshNowPlaying();
                if (info != null && (info.TrackId != trackIdBefore || info.ContextUri != contextUriBefore))
                    return;
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

            // นาฬิกา interpolate เป็นของ NowPlayingSession (hot path ทุกเฟรม) - sync ตรงนี้
            // ส่วนข้อความ/glyph/highlight เป็นของ VM
            if (info == null || string.IsNullOrEmpty(info.Title))
            {
                _session.Clear();
            }
            else
            {
                // ตั้ง "จุด sync" ใหม่จากข้อมูลจริงที่เพิ่ง poll มา ให้ Tick() คำนวณ interpolate ต่อจากตรงนี้
                // แทนที่จะ set slider ตรงๆ ทุกครั้งที่ poll (ซึ่งจะทำให้ progress ขยับเป็นสเต็ปทุก 5 วิ)
                _session.Sync(info.Position, info.Duration, info.IsPlaying, DateTime.UtcNow);
                _songEndTriggerFired = false; // เพลงใหม่มาแล้ว (หรือ resync) รีเซ็ตให้ตรวจจับเพลงจบรอบถัดไปได้อีก
                EnsureSubscribedToTick();
            }

            Apply(_vm.NowPlayingUpdated(info));
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

        // มอดไม่ได้ poll ตามเวลา (ถอดออกไปตอนลดจำนวน API call) รู้ว่าเพลงเปลี่ยนได้แค่ 2 ทาง:
        // ผู้ใช้กดปุ่มในเกม หรือนาฬิกาเราเองนับจนเพลงจบ ทั้งคู่พลาดกรณีที่ไปสั่งจากแอป Spotify
        // โดยตรง - เกมจะยังนับ progress ของเพลงเก่าต่อไปจนกว่าจะกดอะไรสักอย่าง
        // สลับหน้าต่างกลับเข้าเกมเป็นจังหวะที่บอกได้ค่อนข้างชัดว่าผู้ใช้เพิ่งไปยุ่งกับที่อื่นมา
        // เลย resync ตรงนี้แทนการกลับไป poll ทุกไม่กี่วินาที ซึ่งเปลืองกว่ามาก
        private static void EnsureSubscribedToFocus()
        {
            if (_subscribedToFocus) return;
            _subscribedToFocus = true;
            Application.focusChanged += OnAppFocusChanged;
        }

        private static void OnAppFocusChanged(bool hasFocus)
        {
            if (!hasFocus || !SpotifyAuth.IsLoggedIn) return;
            // alt-tab รัวๆ ไม่ควรกลายเป็นการยิง API รัวๆ ตาม
            if (DateTime.UtcNow - _lastFocusRefreshUtc < FocusRefreshCooldown) return;
            _lastFocusRefreshUtc = DateTime.UtcNow;
            Plugin.Log.LogInfo("[SpotifyPatches] กลับเข้าเกม - resync เพลงที่เล่นอยู่");
            SafeFireAndForget(RefreshNowPlaying());
        }

        // รันทุกเฟรม คำนวณตำแหน่งเพลงเองจากเวลาจริงที่ผ่านไป ไม่ยิง API เพิ่มเลย
        // ค่าจริงจาก Spotify จะมา sync ทับจุด anchor นี้ใหม่ทุกครั้งใน ApplyNowPlaying
        // (ไม่มี poll ตามเวลาแล้ว - resync เกิดตอนกดปุ่ม เพลงจบ หรือสลับหน้าต่างกลับเข้าเกม)
        private static void TickProgressBar()
        {
            if (!_session.IsActive || _posText == null || _durText == null || _progressSlider == null)
                return;

            PlaybackFrame frame = _session.Tick(DateTime.UtcNow);

            _posText.text = PanelViewModel.FormatTime(frame.Position);
            _durText.text = PanelViewModel.FormatTime(frame.Duration);
            _progressSlider.value = frame.Fraction;

            // เพลงจบตามนาฬิกาเราเอง (ยังไม่ได้ยิง API เลยสักครั้งตอนนี้) -> ยิงครั้งเดียวเพื่อดึงเพลงถัดไป
            // ที่ Spotify auto-advance ไปแล้วจริงๆ ตอนนี้ ป้องกันยิงซ้ำทุกเฟรมด้วย _songEndTriggerFired
            // (frame ยัง clamp ค้างที่ปลายเพลงทุกเฟรมจนกว่าข้อมูลเพลงใหม่จะ Sync ทับ - แสดงบาร์เต็มไว้)
            if (frame.ReachedEnd && !_songEndTriggerFired)
            {
                _songEndTriggerFired = true;
                SafeFireAndForget(RefreshNowPlaying());
            }
        }

        private static async void SafeFireAndForget(Task task)
        {
            try { await task; }
            catch (Exception ex) { Plugin.Log.LogError($"[SpotifyPatches] Async error: {ex}"); }
        }






        private static async Task OnSearchClicked()
        {
            if (_searchInput == null || string.IsNullOrWhiteSpace(_searchInput.text)) return;
            string query = _searchInput.text.Trim();
            Plugin.Log.LogInfo($"[SpotifyPatches] Search: '{query}'");

            SpotifySearchResults results = await SpotifySearchApi.SearchAsync(query, limitPerType: 5);
            Plugin.RunOnMainThread(() => Apply(_vm.SearchResultsArrived(results)));
        }

        // toggle รายชื่อ playlist ของ user ในพื้นที่ผลค้นหา: กดครั้งแรกแสดง กดซ้ำหุบกลับ (VM เป็นคนถือ
        // สถานะ toggle) - ข้อมูลถูก cache ทั้ง session ฝั่ง SpotifyWebApi กดกี่รอบก็ยิง API แค่ครั้งแรก
        private static async Task OnMyPlaylistsClicked()
        {
            if (!SpotifyAuth.IsLoggedIn) return;

            // onClick เริ่มบน main thread และยังไม่ได้ await -> คุยกับ VM ตรงนี้ได้เลย
            if (!_vm.MyListsClicked())
            {
                Apply(_vm.Current); // หุบรายการที่โชว์อยู่ ไม่ต้อง fetch
                return;
            }

            System.Collections.Generic.List<UserPlaylistInfo> playlists =
                await SpotifyWebApi.GetMyPlaylistsAsync(limit: 20);
            Plugin.RunOnMainThread(() => Apply(_vm.MyPlaylistsArrived(playlists)));
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
            if (string.IsNullOrWhiteSpace(newText))
                Apply(_vm.SearchCleared());
        }

        // สั่งเล่นเพลงเดียว (ผลค้นหาประเภท track) - หลุดจาก context ที่เล่นอยู่
        private static Task PlayTrack(string trackId) =>
            PlayThen(() => SpotifyApi.PlayTrackUri($"spotify:track:{trackId}"));

        // สั่งเล่นทั้ง context (playlist/album/artist) ตั้งแต่ต้น - ใช้กับการกด playlist จากผลค้นหา
        private static Task PlayContext(string contextUri) =>
            string.IsNullOrEmpty(contextUri)
                ? Task.CompletedTask
                : PlayThen(() => SpotifyApi.PlayContextUri(contextUri));

        // เล่นเพลงจากตำแหน่งใน playlist/album โดยตรง เพื่อให้ปุ่ม next/prev ยังเดินตาม context เดิมต่อได้
        // ไม่ถูกเรียกด้วย artist context เพราะแถวคิวของศิลปินไม่ได้ผูกปุ่มไว้ (Spotify ไม่รับ offset)
        private static Task PlayTrackInPlaylist(string contextUri, string trackId)
        {
            if (string.IsNullOrEmpty(trackId)) return Task.CompletedTask;
            string trackUri = $"spotify:track:{trackId}";
            return PlayThen(() => string.IsNullOrEmpty(contextUri)
                ? SpotifyApi.PlayTrackUri(trackUri)
                : SpotifyApi.PlayContextAtTrackUri(contextUri, trackUri));
        }

        // ทุกคำสั่งเล่นจบเหมือนกัน: จำเพลงก่อนสั่ง แล้วตาม refresh จนเห็นว่า Spotify สลับให้แล้ว
        // (เรียกจาก onClick ซึ่งอยู่บน main thread จนกว่าจะถึง await แรก - อ่าน VM ตรงนี้ได้)
        private static async Task PlayThen(Func<Task<bool>> command)
        {
            if (!SpotifyAuth.IsLoggedIn) return;
            string trackBefore = _vm.Current.HighlightedTrackId;
            await command();
            await RefreshAfterPlay(trackBefore, _lastSeenContextUri);
        }

        // กดอัลบั้มจากผลค้นหา -> เอารายชื่อเพลงมาแสดงในพื้นที่คิว ให้เลือกเพลงเองได้ (ไม่เล่นทันที)
        private static async Task LoadAlbumTracks(string albumId, string albumName, string coverUrl = null)
        {
            if (!SpotifyAuth.IsLoggedIn) return;

            PlaylistInfo albumInfo = await SpotifyWebApi.GetAlbumTracksAsync(albumId, albumName, coverUrl);
            if (albumInfo == null) return; // โหลดพลาด - ปล่อยของเดิมค้างไว้ดีกว่าล้างจอเป็นว่าง

            Plugin.RunOnMainThread(() => Apply(_vm.ContextLoaded(albumInfo)));
        }

        // === Apply: จุดเดียวที่แปลง PanelState -> Unity UI ===
        // idempotent ทั้งก้อน - SetActive/ตั้งข้อความซ้ำค่าเดิมได้ไม่มีผลข้างเคียง ส่วน list rebuild
        // เฉพาะตอน revision ขยับ และ reflow ตาม flag เท่านั้น การ "ลืม" จุดใดจุดหนึ่งเลยเกิดไม่ได้
        // ตราบใดที่ทุก event เดินผ่านทางนี้ (ต้นเหตุบั๊ก 3 ใน 6 ตัวของ v1.1.2)
        private static void Apply(PanelState s)
        {
            if (_spotifySection == null) return;

            if (_connectRow != null) _connectRow.SetActive(s.ConnectRowVisible);
            if (_controlsRow != null) _controlsRow.SetActive(s.ControlsRowVisible);
            if (_playlistHeader != null) _playlistHeader.SetActive(s.PlaylistHeaderVisible);
            if (_queueList != null) _queueList.SetActive(s.QueueListVisible);
            if (_searchRow != null) _searchRow.SetActive(s.SearchRowVisible);

            if (_statusText != null) _statusText.text = s.StatusText ?? "";
            if (_trackTitleText != null) _trackTitleText.text = s.TrackTitle;
            if (_artistText != null) _artistText.text = s.TrackArtist;
            if (_playPauseLabel != null) _playPauseLabel.text = s.PlayPauseGlyph;

            // ตอนไม่มีเพลง แสดงเวลา/บาร์เป็นศูนย์ - ตอนมีเพลง hot path (TickProgressBar) เป็นเจ้าของ
            if (s.ShowIdleProgress)
            {
                if (_posText != null) _posText.text = "0:00";
                if (_durText != null) _durText.text = "0:00";
                if (_progressSlider != null) _progressSlider.value = 0f;
            }

            ApplyNowPlayingCover(s.NowPlayingCoverBytes);
            ApplyHeader(s);

            if (s.QueueRevision != _appliedQueueRev)
            {
                _appliedQueueRev = s.QueueRevision;
                if (_queueList != null)
                {
                    ClearChildren(_queueList.transform);
                    _queueRowTitles.Clear();
                    if (s.QueueMessage != null)
                    {
                        Text msg = SpotifyUiKit.CreateText(_queueList.transform, s.QueueMessage, 11, TextAnchor.MiddleLeft);
                        msg.color = SpotifyUiKit.TextFaint;
                    }
                    else
                    {
                        foreach (PanelRow row in s.QueueRows)
                            RenderRow(_queueList.transform, row);
                    }
                }
            }

            // ทาสีทุกรอบ (ถูกกว่า rebuild มาก) - เพลงเปลี่ยนบ่อยกว่ารายชื่อคิวเยอะ
            RecolorHighlight(s.HighlightedTrackId);

            if (s.ResultsRevision != _appliedResultsRev)
            {
                _appliedResultsRev = s.ResultsRevision;
                if (_searchResultsList != null)
                {
                    ClearChildren(_searchResultsList.transform);
                    foreach (PanelSection section in s.ResultsSections)
                    {
                        if (section.Label != null)
                            SpotifyUiKit.CreateSectionLabel(_searchResultsList.transform, section.Label);
                        if (section.Message != null)
                        {
                            Text msg = SpotifyUiKit.CreateText(_searchResultsList.transform, section.Message, 11, TextAnchor.MiddleLeft);
                            msg.color = new Color(0.65f, 0.65f, 0.65f, 1f);
                        }
                        foreach (PanelRow row in section.Rows)
                            RenderRow(_searchResultsList.transform, row);
                    }
                }
            }

            // โครงสร้างเปลี่ยน (แถวโผล่/หาย, list เปลี่ยน) -> ต้อง rebuild scroll content ชั้นนอกด้วย
            // ไม่งั้น section เราไม่สูงขึ้นตามเนื้อหา แล้วแถวเพลงของเกม (sibling ถัดไป) วาดทับของเรา
            if (s.NeedsReflow && _cachedScrollRect != null)
            {
                if (_searchResultsList != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_searchResultsList.GetComponent<RectTransform>());
                LayoutRebuilder.ForceRebuildLayoutImmediate(_cachedScrollRect.content);
            }
        }

        // ปกเพลงที่กำลังเล่น - สร้าง Texture2D เฉพาะตอน bytes เป็นชุดใหม่จริงๆ (Apply ถูกเรียก
        // ทุก event ถ้าสร้างทุกรอบ texture จะรั่วเรื่อยๆ) / null = คงปกเดิมไว้ (พฤติกรรมเดิม)
        private static void ApplyNowPlayingCover(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || _coverImage == null) return;
            if (ReferenceEquals(bytes, _lastAppliedCoverBytes) && _coverImage.sprite != null) return;

            Texture2D tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes)) return;
            _coverImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _coverImage.color = Color.white; // ล้าง tint เทาของ placeholder ไม่งั้นรูปโดนคูณสีจนคล้ำ
            _lastAppliedCoverBytes = bytes;
        }

        private static void ApplyHeader(PanelState s)
        {
            if (_playlistNameText != null) _playlistNameText.text = s.HeaderName;
            if (_playlistSubText != null)
            {
                _playlistSubText.gameObject.SetActive(s.HeaderSubLabel != null);
                if (s.HeaderSubLabel != null) _playlistSubText.text = s.HeaderSubLabel;
            }

            if (_playlistImage == null) return;
            _playlistImage.gameObject.SetActive(s.HeaderCoverVisible);

            if (s.HeaderCoverVisible && s.HeaderCoverBytes != null && s.HeaderCoverBytes.Length > 0)
            {
                if (ReferenceEquals(s.HeaderCoverBytes, _lastAppliedHeaderCoverBytes) && _playlistImage.sprite != null)
                    return;
                Texture2D tex = new Texture2D(2, 2);
                if (!tex.LoadImage(s.HeaderCoverBytes)) return;
                _playlistImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _playlistImage.color = Color.white;
                _lastAppliedHeaderCoverBytes = s.HeaderCoverBytes;
            }
            else
            {
                // ไม่มีปก -> กลับ placeholder กันภาพปกของ playlist ก่อนหน้าค้างแสดงผิดอัน
                _playlistImage.sprite = null;
                _playlistImage.color = SpotifyUiKit.CoverPlaceholder;
                _lastAppliedHeaderCoverBytes = null;
            }
        }

        // วาดแถวหนึ่งตาม PanelRow (ใช้ร่วมกันทั้งคิวเพลงและพื้นที่ผลค้นหา/My Lists)
        private static void RenderRow(Transform parent, PanelRow rowSpec)
        {
            GameObject row = new GameObject("Row");
            row.transform.SetParent(parent, worldPositionStays: false);
            row.AddComponent<RectTransform>();
            LayoutElement rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 36f; // พอสำหรับ 2 บรรทัด (title 12pt + sub 10pt) ของฟอนต์เกม
            rowLe.minHeight = 36f;

            HorizontalLayoutGroup rowHlg = row.AddComponent<HorizontalLayoutGroup>();
            rowHlg.childForceExpandWidth = false;
            rowHlg.childControlWidth = true;
            rowHlg.childControlHeight = true;
            rowHlg.spacing = 6f;
            rowHlg.childAlignment = TextAnchor.MiddleLeft;

            if (rowSpec.Action.Kind != RowActionKind.None)
            {
                RowAction captured = rowSpec.Action;
                Image rowBg = row.AddComponent<Image>();
                rowBg.color = new Color(0f, 0f, 0f, 0f); // โปร่งใส มีไว้รับคลิกให้ทั้งแถวเท่านั้น
                Button rowBtn = row.AddComponent<Button>();
                // แถวไม่มี effect ตอนชี้/กดเลย - ชื่อเพลงเปลี่ยนเป็นสีเขียวตอนเริ่มเล่นคือ feedback อยู่แล้ว
                rowBtn.transition = Selectable.Transition.None;
                rowBtn.targetGraphic = rowBg;
                rowBtn.onClick.AddListener(() => Dispatch(captured));
            }

            if (rowSpec.Index != null)
                SpotifyUiKit.CreateInlineText(row.transform, rowSpec.Index, 18f);

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

            Text titleText = SpotifyUiKit.CreateText(nameCol.transform, rowSpec.Title ?? "-", 12, TextAnchor.MiddleLeft);
            Text subText = null;
            if (!string.IsNullOrEmpty(rowSpec.Sub))
            {
                subText = SpotifyUiKit.CreateText(nameCol.transform, rowSpec.Sub, 10, TextAnchor.MiddleLeft);
                // แถวคิว (มีเลขลำดับ) ใช้โทนจางของ kit / แถวผลค้นหาใช้เทากลางแบบเดิม
                subText.color = rowSpec.Index != null ? SpotifyUiKit.TextFaint : new Color(0.65f, 0.65f, 0.65f, 1f);
            }
            SpotifyUiKit.ClipRowToSingleLine(nameCol, titleText, subText);

            if (!string.IsNullOrEmpty(rowSpec.Right))
                SpotifyUiKit.CreateInlineText(row.transform, rowSpec.Right, rowSpec.Index != null ? 40f : 36f);

            // จดคู่ trackId -> Text ไว้ให้ RecolorHighlight ทาสีเพลงที่กำลังเล่น
            if (!string.IsNullOrEmpty(rowSpec.TrackId))
                _queueRowTitles.Add((rowSpec.TrackId, titleText));
        }

        // ทาสีชื่อเพลงที่ trackId ตรงกับเพลงที่กำลังเล่นเป็นเขียว Spotify ส่วนเพลงอื่นคืนสีขาวปกติ
        private static void RecolorHighlight(string highlightedTrackId)
        {
            foreach (var (trackId, title) in _queueRowTitles)
            {
                if (title == null) continue; // แถวโดน destroy ไปแล้ว (Unity เทียบ null ได้กับ destroyed object)
                title.color = (!string.IsNullOrEmpty(highlightedTrackId) && trackId == highlightedTrackId)
                    ? SpotifyUiKit.ButtonActive // เขียว Spotify ตัวเดียวกับ progress bar
                    : Color.white;
            }
        }

        // แปลง action descriptor จาก VM เป็น API call จริง - จุดเดียวที่ปุ่มในแถวชนโลกภายนอก
        private static void Dispatch(RowAction action)
        {
            switch (action.Kind)
            {
                case RowActionKind.PlayTrack:
                    SafeFireAndForget(PlayTrack(action.TrackId));
                    break;
                case RowActionKind.PlayContext:
                    SafeFireAndForget(PlayContext(action.ContextUri));
                    break;
                case RowActionKind.PlayTrackInContext:
                    SafeFireAndForget(PlayTrackInPlaylist(action.ContextUri, action.TrackId));
                    break;
                case RowActionKind.LoadAlbum:
                    SafeFireAndForget(LoadAlbumTracks(action.AlbumId, action.AlbumName, action.AlbumCoverUrl));
                    break;
            }
        }
    }
}