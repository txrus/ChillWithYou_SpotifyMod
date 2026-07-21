using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ChillWithYou_SpotifyMod
{
    [BepInPlugin("com.pw_txr.spotifyplayer", "Spotify Player Mod", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony _harmony;

        // เมธอดนี้จะเรียกใช้ Dispatcher ตัวใหม่ของเราแทน
        public static void RunOnMainThread(Action action)
        {
            MainThreadDispatcher.Enqueue(action);
        }
        private async void Awake()
        {
            Log = Logger;
            Log.LogInfo("[Plugin] Awake() called. กำลังเริ่มต้นปลั๊กอิน...");

            // 1. เรียกใช้งาน Dispatcher อมตะของเราทันทีที่ปลั๊กอินตื่นขึ้นมา
            MainThreadDispatcher.Initialize();

            _harmony = new Harmony("com.pw_txr.spotifyplayer");
            _harmony.PatchAll(typeof(SpotifyPatches));
            Log.LogInfo("[SpotifyMod v1.1.0] loaded.");

            bool resumed = await SpotifyAuth.TryResumeSessionAsync();
            Log.LogInfo(resumed ? "[Spotify] Resume session สำเร็จ" : "[Spotify] ยังไม่ได้ login");

            if (resumed)
                await SpotifyApi.LogCurrentUser();


        }
    }

    /// <summary>
    /// คลาสใหม่สำหรับจัดการ Main Thread Queue โดยเฉพาะ 
    /// จะถูกผูกติดกับ GameObject ที่ไม่มีวันถูกเกมลบหรือ Disable
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static readonly object _queueLock = new object();

        // ฟังก์ชันสำหรับสร้าง GameObject ที่ปลอดภัย
        public static void Initialize()
        {
            if (_instance == null)
            {
                // สร้าง Object ใหม่ ตั้งชื่อให้ชัดเจน
                GameObject go = new GameObject("SpotifyMod_MainThreadDispatcher");

                // สั่งไม่ให้เกมลบ Object นี้ตอนเปลี่ยนฉาก
                DontDestroyOnLoad(go);

                // ซ่อน Object นี้จากระบบเกมบางส่วน เพื่อป้องกันไม่ให้เกมมาสั่ง Disable
                go.hideFlags = HideFlags.HideAndDontSave;

                // แปะสคริปต์นี้เข้าไป
                _instance = go.AddComponent<MainThreadDispatcher>();

                Plugin.Log.LogInfo("[Dispatcher] สร้าง MainThreadDispatcher สำเร็จ! พร้อมลุย!");
            }
        }

        // รับงานเข้ามาในคิว
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queueLock)
            {
                _queue.Enqueue(action);
            }
        }

        // ทำงานทุกๆ เฟรมของเกมอย่างปลอดภัย
        private void Update()
        {
            int drained = 0;
            while (true)
            {
                Action action;
                lock (_queueLock)
                {
                    if (_queue.Count == 0) break;
                    action = _queue.Dequeue();
                }

                try
                {
                    action();
                    drained++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Dispatcher] โค้ดในคิวเกิดข้อผิดพลาด: {ex}");
                }
            }

            // เปิด Log นี้ไว้ดูชั่วคราวได้ หากต้องการยืนยันว่า UI ถูกอัปเดตแล้วจริงๆ
            if (drained > 0)
            {
                Plugin.Log.LogInfo($"[Dispatcher] ประมวลผลและอัปเดต UI สำเร็จจำนวน {drained} งาน");
            }
        }
    }
}