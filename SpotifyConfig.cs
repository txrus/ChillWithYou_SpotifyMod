using System;
using BepInEx.Configuration;

namespace ChillWithYou_SpotifyMod
{
    // Client ID ต้องเป็นของผู้เล่นแต่ละคนเอง (Spotify ผูก quota + redirect URI ไว้กับ app ของแต่ละบัญชี)
    // เดิมมันเป็น const ใน SpotifyAuth ที่ต้อง build ใหม่ทุกครั้ง = ผู้เล่นต้องลง .NET SDK ทั้งชุด
    // แค่เพื่อแก้สตริงเดียว ตอนนี้อ่านจาก BepInEx config file ก่อน แล้วค่อย fallback ไป env var
    // และค่าที่ build.ps1 ฝังไว้ (คนที่ build เองอยู่แล้วจึงไม่ต้องแก้อะไรเลย)
    internal static class SpotifyConfig
    {
        // ชื่อไฟล์นี้ BepInEx ตั้งจาก GUID ใน [BepInPlugin] - ถ้าเปลี่ยน GUID ต้องแก้ตรงนี้ด้วย
        // เพราะข้อความบอกทางใน UI/log ชี้ไปที่ path นี้ตรงๆ
        public const string ConfigFilePath = @"BepInEx\config\com.pw_txr.spotifyplayer.cfg";

        public const string EnvVarName = "CHILLWITHYOU_SPOTIFY_CLIENT_ID";

        // ค่าที่ build.ps1 หาแล้วแทนที่ตอน build - อย่าเปลี่ยนรูปประโยคบรรทัดนี้โดยไม่แก้ regex ใน build.ps1
        private const string BakedInClientId = "ENTER_YOUR_CLIENT_ID";

        private const string Placeholder = "ENTER_YOUR_CLIENT_ID";

        public static string MissingClientIdMessage =>
            "no Spotify Client ID set - add it to " + ConfigFilePath;

        // ว่าง = ยังไม่ได้ตั้งค่า ห้ามยิง OAuth ใดๆ ทั้งสิ้น (Spotify จะตอบ 400 แล้ว log จะงงมาก)
        public static string ClientId { get; private set; } = "";

        public static bool HasClientId => ClientId.Length > 0;

        // เรียกครั้งเดียวจาก Plugin.Awake ก่อนแตะ SpotifyAuth - Config มาจาก BaseUnityPlugin
        public static void Load(ConfigFile config)
        {
            ConfigEntry<string> entry = config.Bind(
                "Spotify",
                "ClientId",
                "",
                "Your Spotify app's Client ID (32 hex characters).\n" +
                "Create an app at https://developer.spotify.com/dashboard, add\n" +
                "http://127.0.0.1:8901/callback/ to its Redirect URIs, then paste the Client ID here.\n" +
                "Leave empty to fall back to the " + EnvVarName + " environment variable.");

            string fromConfig = Sanitize(entry.Value);
            string fromEnv = Sanitize(Environment.GetEnvironmentVariable(EnvVarName));
            string fromBuild = Sanitize(BakedInClientId);

            string source;
            if (fromConfig.Length > 0)
            {
                ClientId = fromConfig;
                source = ConfigFilePath;
            }
            else if (fromEnv.Length > 0)
            {
                ClientId = fromEnv;
                source = "environment variable " + EnvVarName;
            }
            else if (fromBuild.Length > 0)
            {
                ClientId = fromBuild;
                source = "the value compiled into the DLL";
            }
            else
            {
                ClientId = "";
                Plugin.Log.LogWarning(
                    $"[SpotifyConfig] ยังไม่ได้ตั้ง Client ID - เปิด {ConfigFilePath} แล้วใส่ค่า ClientId " +
                    "(หรือตั้ง env var " + EnvVarName + ") แล้วเริ่มเกมใหม่");
                return;
            }

            Plugin.Log.LogInfo($"[SpotifyConfig] Client ID {Mask(ClientId)} (from {source})");

            // ไม่ block เพราะรูปแบบอาจเปลี่ยนในอนาคต แค่เตือนไว้ - เคสจริงคือ copy มาไม่ครบ/ติดช่องว่าง
            if (!LooksLikeClientId(ClientId))
            {
                Plugin.Log.LogWarning(
                    $"[SpotifyConfig] ค่า Client ID ไม่เหมือนของจริง (ยาว {ClientId.Length} ตัว, " +
                    "ปกติเป็น hex 32 ตัว) - ถ้า login ไม่ผ่านให้เช็คว่า copy มาครบไหม");
            }
        }

        // user มักวางค่ามาพร้อมช่องว่าง/เครื่องหมายคำพูดที่ copy ติดมาจากหน้า dashboard หรือจากโค้ดตัวอย่าง
        private static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            string value = raw.Trim().Trim('"', '\'').Trim();
            if (value.Length == 0) return "";
            if (string.Equals(value, Placeholder, StringComparison.OrdinalIgnoreCase)) return "";

            return value;
        }

        private static bool LooksLikeClientId(string value)
        {
            if (value.Length != 32) return false;

            foreach (char c in value)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }

            return true;
        }

        // Client ID ไม่ใช่ความลับ (PKCE ไม่ใช้ secret) แต่ log ของผู้เล่นมักถูกแปะลง Nexus/Discord
        // โชว์แค่หัวท้ายก็พอให้เทียบกับ dashboard ได้ว่าใช่ตัวเดียวกันไหม
        private static string Mask(string value)
        {
            if (value.Length <= 8) return new string('*', value.Length);
            return value.Substring(0, 4) + new string('*', value.Length - 8) + value.Substring(value.Length - 4);
        }
    }
}
