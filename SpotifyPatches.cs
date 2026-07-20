using Bulbul;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ChillWithYou_SpotifyMod
{
    internal static class SpotifyPatches
    {
        [HarmonyPatch(typeof(MusicPlayListView), "Setup")]
        [HarmonyPostfix]
        private static void MusicPlayListView_Setup_Postfix(MusicPlayListView __instance)
        {
            ScrollRect scrollRect = Traverse.Create(__instance).Field("_scrollRect").GetValue<ScrollRect>();
            GameObject buttonsParent = Traverse.Create(__instance).Field("_playListButtonsParent").GetValue<GameObject>();

            if (buttonsParent == null)
            {
                Plugin.Log.LogWarning("[SpotifyPatches] _playListButtonsParent not found.");
                return;
            }
            SpotifyButtonsInjector.Inject(scrollRect, buttonsParent);
        }

        // จุดที่ถูกเรียกจริงตอนผู้เล่นกดเปิดหน้าต่าง Playlist
        [HarmonyPatch(typeof(MusicUI), "ActivatePlayList")]
        [HarmonyPostfix]
        private static void MusicUI_ActivatePlayList_Postfix()
        {
            SpotifyButtonsInjector.OnActivate();
        }

        // หมายเหตุ: การ pump คิว main-thread ทำผ่าน MainThreadDispatcher (GameObject แยกที่สร้างเองใน
        // Plugin.Awake ด้วย DontDestroyOnLoad + HideFlags.HideAndDontSave) อยู่แล้ว ไม่ต้องพึ่ง
        // Harmony patch บน Bulbul.TimerCoreService.Update อีกต่อไป (เคยใช้คู่กับ ThreadDispatcher.cs
        // ที่ตอนนี้ลบทิ้งแล้วเพราะไม่มีใครเรียก ThreadDispatcher.Enqueue() เลย เป็น dead code)
    }
}