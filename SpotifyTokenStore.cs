using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BepInEx;

namespace ChillWithYou_SpotifyMod
{
    // เก็บ refresh token ลงไฟล์ในโฟลเดอร์ config ของ BepInEx
    // บน Windows จะเข้ารหัสด้วย DPAPI (ผูกกับเครื่อง+user ที่ล็อกอินอยู่)
    // แพลตฟอร์มอื่นจะเก็บเป็น plain text (ยังดีกว่าไม่เก็บเลย แต่ระวังไฟล์รั่ว)
    internal static class SpotifyTokenStore
    {
        private static readonly string TokenFilePath =
            Path.Combine(Paths.ConfigPath, "spotify_refresh_token.dat");

        // เกลือ (entropy) เพิ่มความปลอดภัยให้ DPAPI อีกชั้น เปลี่ยนเป็นค่าอะไรก็ได้ของท่านเอง
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ChillWithYou_SpotifyMod_v1");

        public static void SaveRefreshToken(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(refreshToken);
                byte[] toWrite;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    toWrite = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                }
                else
                {
                    toWrite = plainBytes; // fallback: ไม่มี DPAPI บน mac/linux
                }

                Directory.CreateDirectory(Path.GetDirectoryName(TokenFilePath)!);
                File.WriteAllBytes(TokenFilePath, toWrite);
            }
            catch (Exception ex)
            {
                Debug_Log($"[SpotifyTokenStore] Save failed: {ex.Message}");
            }
        }

        public static string LoadRefreshToken()
        {
            try
            {
                if (!File.Exists(TokenFilePath))
                    return null;

                byte[] raw = File.ReadAllBytes(TokenFilePath);
                byte[] plainBytes;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    plainBytes = ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser);
                }
                else
                {
                    plainBytes = raw;
                }

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                Debug_Log($"[SpotifyTokenStore] Load failed (token invalid/corrupt, will need re-login): {ex.Message}");
                // ถ้า decrypt ไม่ได้ (เช่น ย้ายเครื่อง) ให้ลบไฟล์เก่าทิ้งกันค้าง
                TryDelete();
                return null;
            }
        }

        public static void TryDelete()
        {
            try
            {
                if (File.Exists(TokenFilePath))
                    File.Delete(TokenFilePath);
            }
            catch { /* เงียบไว้ ไม่ critical */ }
        }

        private static void Debug_Log(string msg) => UnityEngine.Debug.Log(msg);
    }
}
