using UnityEditor;
using UnityEngine;

public static class DwaallichtMapMenu
{
    [MenuItem("Dwaallicht/Map/Toggle Calibration")]
    private static void ToggleCalibration()
    {
        var controller = Object.FindFirstObjectByType<DwaallichtAppController>();
        if (controller != null) controller.SetMapCalibrationMode(!controller.MapCalibrationMode);
    }

    [MenuItem("Dwaallicht/Map/Toggle Calibration", true)]
    private static bool ValidateToggleCalibration()
    {
        var controller = Object.FindFirstObjectByType<DwaallichtAppController>();
        Menu.SetChecked("Dwaallicht/Map/Toggle Calibration", controller != null && controller.MapCalibrationMode);
        return EditorApplication.isPlaying && controller != null;
    }
}
