using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class KrefelArScannerTests
{
    private static Type ScannerType => Type.GetType("Dwaallicht.AR.DwaallichtKrefelArScanner, Assembly-CSharp", true);

    [Test]
    public void KrefelReferenceDimensionsMatchDeskPaper()
    {
        Assert.That(GetConstant<string>("KrefelReferenceImageName"), Is.EqualTo("Krefel aankoop"));
        Assert.That(GetConstant<float>("KrefelImageWidthMeters"), Is.EqualTo(0.10f).Within(0.0001f));
        Assert.That(GetConstant<float>("KrefelImageHeightMeters"), Is.EqualTo(0.15f).Within(0.0001f));
    }

    [Test]
    public void CubeUsesFiveCentimeterSizeAndHeight()
    {
        Assert.That(GetConstant<float>("CubeSizeMeters"), Is.EqualTo(0.05f).Within(0.0001f));
        Assert.That(GetConstant<float>("CubeCenterHeightMeters"), Is.EqualTo(0.05f).Within(0.0001f));
    }

    [Test]
    public void EditorSimulationSpawnsCubeOnScanActivation()
    {
        var root = new GameObject("Test AR Scanner");
        try
        {
            var scanner = root.AddComponent(ScannerType);
            InvokeSetScanningActive(scanner, true);

            var cube = GameObject.Find("Krefel Recognition Cube");
            Assert.NotNull(cube);
            Assert.That(GetProperty<bool>(scanner, "IsScanningActive"), Is.True);
            Assert.That(GetProperty<bool>(scanner, "HasVisibleCube"), Is.True);
            Assert.That(cube.transform.localPosition.y, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(cube.transform.localScale, Is.EqualTo(Vector3.one * 0.05f));

            InvokeSetScanningActive(scanner, false);
            Assert.That(GetProperty<bool>(scanner, "HasVisibleCube"), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            var cube = GameObject.Find("Krefel Recognition Cube");
            if (cube != null)
            {
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }
    }

    private static T GetConstant<T>(string name)
    {
        var field = ScannerType.GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field, name + " constant was not found.");
        return (T)field.GetValue(null);
    }

    private static T GetProperty<T>(Component target, string name)
    {
        var property = ScannerType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property, name + " property was not found.");
        return (T)property.GetValue(target);
    }

    private static void InvokeSetScanningActive(Component target, bool active)
    {
        var method = ScannerType.GetMethod("SetScanningActive", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method, "SetScanningActive was not found.");
        method.Invoke(target, new object[] { active });
    }
}
