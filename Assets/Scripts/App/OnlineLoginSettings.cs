using System;
using System.IO;
using UnityEngine;

namespace Craftwar.App
{
    /// <summary>
    /// Remembered online-login fields (server address, username, password),
    /// so the multiplayer login form doesn't start blank every session. Same
    /// persistence shape as <see cref="View.GameplaySettings"/> (JSON in
    /// persistentDataPath, a lazily-loaded static singleton) but kept as its
    /// own file since it's an unrelated concern (menu/account, not gameplay).
    ///
    /// Stored in PLAIN TEXT on the local machine, same trust level as this
    /// project's other local files — there is no OS keychain integration
    /// here. Acceptable for a self-hosted dev server; worth revisiting before
    /// this ever points at a shared/public one.
    /// </summary>
    [Serializable]
    public sealed class OnlineLoginSettings
    {
        public string server = "127.0.0.1:27015";
        public string username = "";
        public string password = "";

        static OnlineLoginSettings _current;

        public static OnlineLoginSettings Current => _current ??= Load();

        static string FilePath => Path.Combine(Application.persistentDataPath, "online-login.json");

        static OnlineLoginSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonUtility.FromJson<OnlineLoginSettings>(File.ReadAllText(FilePath));
                    if (loaded != null)
                        return loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Craftwar] Could not read online login settings: {e.Message}");
            }
            return new OnlineLoginSettings();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Craftwar] Could not save online login settings: {e.Message}");
            }
        }
    }
}
