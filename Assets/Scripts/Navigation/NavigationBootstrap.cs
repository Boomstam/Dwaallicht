using UnityEngine;

namespace Dwaallicht.Navigation
{
    public static class NavigationBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureNavigationObjects()
        {
            var heading = Object.FindFirstObjectByType<CompassHeadingProvider>();
            if (heading == null)
            {
                var go = new GameObject("Compass Heading Provider");
                heading = go.AddComponent<CompassHeadingProvider>();
            }

            var manager = Object.FindFirstObjectByType<PoiManager>();
            if (manager == null)
            {
                var go = new GameObject("POI Manager");
                manager = go.AddComponent<PoiManager>();
            }

            var map = Object.FindFirstObjectByType<MapController>();
            if (map != null && Object.FindFirstObjectByType<MapNavigationOverlay>() == null)
            {
                var go = new GameObject("Map Navigation Overlay");
                go.AddComponent<MapNavigationOverlay>();
            }
        }
    }
}
