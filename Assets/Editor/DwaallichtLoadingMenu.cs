using UnityEditor;
using UnityEngine;

public static class DwaallichtLoadingMenu
{
    [MenuItem("Dwaallicht/Loading Screen Preview")]
    private static void Toggle()
    {
        var app = Object.FindFirstObjectByType<DwaallichtAppController>();
        if (app != null) app.SetLoadingScreenPreview(!app.LoadingScreenPreview);
    }

    [MenuItem("Dwaallicht/Loading Screen Preview", true)]
    private static bool Validate()
    {
        var app = Object.FindFirstObjectByType<DwaallichtAppController>();
        Menu.SetChecked("Dwaallicht/Loading Screen Preview", app != null && app.LoadingScreenPreview);
        return EditorApplication.isPlaying && app != null;
    }
}
