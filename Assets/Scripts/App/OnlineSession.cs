using Craftwar.Net;

namespace Craftwar.App
{
    /// <summary>
    /// Carries the logged-in online account session and its persistent
    /// <see cref="SocialClient"/> connection ACROSS scene loads.
    ///
    /// MainMenuController is destroyed and recreated on every Menu&lt;-&gt;Game
    /// scene transition (starting a match, or returning from one via the
    /// victory screen) — but staying logged into chat/friends should not
    /// depend on that MonoBehaviour's lifetime any more than the real
    /// Battle.net logged you out of chat every time you started a game.
    /// Before this existed, returning from a match always showed a blank
    /// login form even though the player never explicitly logged out.
    ///
    /// Deliberately shaped like <c>Craftwar.Net.Unity.NetSession</c>/
    /// <c>MatchSession</c>: a static the next MainMenuController instance
    /// picks back up, cleared only on an explicit logout (the online panel's
    /// Back button) — never just because a scene reloaded.
    /// </summary>
    public static class OnlineSession
    {
        public static string Host;
        public static int Port;
        public static string SessionToken;
        public static string Username;
        public static SocialClient Social;

        /// <summary>The channel to rejoin when a fresh MainMenuController
        /// adopts an already-live Social connection — the connection itself
        /// stayed in whatever channel it was in the whole time, but the new
        /// instance's cached roster/MOTD/member state starts empty, so
        /// MainMenuController.ShowOnline() forces a fresh ChannelJoinResult
        /// by rejoining this name (mirrors SocialClient.JoinChannel's own
        /// "leaves whatever channel you were in, joins/creates this one"
        /// behavior — rejoining your own current channel is a harmless
        /// leave+immediately-rejoin blip to other members, not a real
        /// departure).</summary>
        public static string CurrentChannel = SocialClient.DefaultChannelName;

        public static bool IsActive => !string.IsNullOrEmpty(SessionToken);

        /// <summary>Explicit logout only. Never call this from a
        /// MonoBehaviour's OnDestroy — that runs on every scene reload,
        /// which is exactly the lifetime this class exists to outlive.</summary>
        public static void Clear()
        {
            Social?.Dispose();
            Social = null;
            SessionToken = null;
            Username = null;
            Host = null;
            Port = 0;
            CurrentChannel = SocialClient.DefaultChannelName;
        }
    }
}
