// NowPlayingSession.cs
// สถานะการเล่นเพลง + นาฬิกา interpolate ล้วนๆ - ไม่พึ่ง Unity หรือ logging เลย
// แยกออกมาจาก SpotifyButtonInjector เพื่อให้ logic ที่เคยมีบั๊ก (progress bar ค้าง, play/pause
// ไม่ re-anchor, เพลงจบไม่ดึงเพลงถัดไป) ทดสอบได้โดยไม่ต้องรัน Unity
//
// วิธีใช้: UI เรียก Sync() ทุกครั้งที่ได้ข้อมูลจริงจาก Spotify แล้วเรียก Tick() ทุกเฟรม
// เพื่อขอค่าที่จะแสดง โดยไม่ต้องยิง API เพิ่ม (คำนวณจากเวลาจริงที่ผ่านไปตั้งแต่ anchor ล่าสุด)
using System;

namespace ChillWithYou_SpotifyMod
{
    // ค่าที่ควรแสดงบน progress bar ณ ช่วงเวลาหนึ่ง - คำนวณจากนาฬิกาในเครื่อง
    public struct PlaybackFrame
    {
        public TimeSpan Position;   // ตำแหน่งเพลงที่ควรแสดง
        public TimeSpan Duration;   // ความยาวเพลง
        public float Fraction;      // 0..1 สำหรับตั้งค่า slider
        public bool ReachedEnd;     // true เมื่อเล่นถึง/เกินความยาวเพลงแล้ว (ใช้ trigger ดึงเพลงถัดไป)
    }

    public sealed class NowPlayingSession
    {
        private TimeSpan _syncedPosition;
        private TimeSpan _syncedDuration;
        private DateTime _syncedAtUtc;
        private bool _isPlaying;
        private bool _active;   // false = ยังไม่มีเพลง / เพลงหายไป -> UI ไม่ต้อง tick

        public bool IsPlaying => _isPlaying;
        public bool IsActive => _active;

        // ตั้ง anchor ใหม่จากข้อมูลจริงที่เพิ่ง poll มา ให้ Tick นับต่อจากจุดนี้
        public void Sync(TimeSpan position, TimeSpan duration, bool isPlaying, DateTime nowUtc)
        {
            _syncedPosition = position;
            _syncedDuration = duration;
            _isPlaying = isPlaying;
            _syncedAtUtc = nowUtc;
            _active = true;
        }

        // ไม่มีเพลงเล่นอยู่ -> หยุด interpolate
        public void Clear()
        {
            _active = false;
            _isPlaying = false;
        }

        // สลับ play/pause ในเครื่อง: ตรึงตำแหน่ง ณ ตอนนี้เป็น anchor ใหม่ แล้วพลิกสถานะ
        // ตอนกำลังเล่นต้องเก็บเวลาที่เดินไปแล้วเข้า anchor ก่อน ไม่งั้นพอ pause เวลาจะเด้งกลับไปจุด sync เดิม
        public void TogglePlayPause(DateTime nowUtc)
        {
            if (!_active) return;
            if (_isPlaying)
            {
                TimeSpan pos = _syncedPosition + (nowUtc - _syncedAtUtc);
                if (_syncedDuration > TimeSpan.Zero && pos > _syncedDuration) pos = _syncedDuration;
                _syncedPosition = pos;
            }
            _syncedAtUtc = nowUtc;
            _isPlaying = !_isPlaying;
        }

        // ค่าที่ควรแสดง ณ เวลา nowUtc - ถ้ากำลังเล่นก็บวกเวลาที่ผ่านไปจาก anchor (clamp ที่ความยาวเพลง)
        // ถ้าหยุดอยู่ ตำแหน่งคงที่ที่ anchor ล่าสุด
        public PlaybackFrame Tick(DateTime nowUtc)
        {
            TimeSpan pos = _syncedPosition;
            bool reachedEnd = false;

            if (_isPlaying)
            {
                pos = _syncedPosition + (nowUtc - _syncedAtUtc);
                if (_syncedDuration > TimeSpan.Zero && pos >= _syncedDuration)
                {
                    pos = _syncedDuration;
                    reachedEnd = true;
                }
            }

            double total = _syncedDuration.TotalSeconds;
            float fraction = total > 0 ? (float)(pos.TotalSeconds / total) : 0f;

            return new PlaybackFrame
            {
                Position = pos,
                Duration = _syncedDuration,
                Fraction = fraction,
                ReachedEnd = reachedEnd,
            };
        }
    }
}
