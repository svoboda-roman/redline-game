using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Starts the game in whatever scene is loaded, so the project needs no scene setup:
    /// press Play in any scene (or run a build) and Redline builds its arena from code.
    /// </summary>
    public static class RedlineBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindAnyObjectByType<RedlineGame>() != null) return;
            var go = new GameObject("Redline");
            go.AddComponent<RedlineGame>();
        }
    }
}
