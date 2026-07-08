using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class DwaallichtAppControllerPoiContentTests
{
    private static Type ControllerType => Type.GetType("DwaallichtAppController, Assembly-CSharp", true);

    [Test]
    public void GetPoiContentEntries_OrdersContentAndStripsVisibleNumericPrefixes()
    {
        var folder = Path.Combine(Application.temporaryCachePath, "DwaallichtPoiContentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "1. Start Text.txt"), "First");
            File.WriteAllBytes(Path.Combine(folder, "2. Banana.png"), new byte[] { 1, 2, 3 });
            File.WriteAllText(Path.Combine(folder, "3. Middle Text.txt"), "Second");
            File.WriteAllBytes(Path.Combine(folder, "4. Sound.mp3"), new byte[] { 4, 5, 6 });
            File.WriteAllText(Path.Combine(folder, "10. End Text.txt"), "Last");
            File.WriteAllText(Path.Combine(folder, "Ignored.txt.meta"), "meta");

            var gameObject = new GameObject("Controller");
            try
            {
                var controller = gameObject.AddComponent(ControllerType);
                var entries = GetEntries(controller, folder, true);

                Assert.That(GetKind(entries[0]), Is.EqualTo("Ar"));
                Assert.That(GetKind(entries[1]), Is.EqualTo("Text"));
                Assert.That(GetText(entries[1]), Is.EqualTo("First"));
                Assert.That(GetDisplayName(entries[1]), Is.EqualTo("Start Text"));
                Assert.That(GetKind(entries[2]), Is.EqualTo("Image"));
                Assert.That(GetDisplayName(entries[2]), Is.EqualTo("Banana"));
                Assert.That(GetKind(entries[3]), Is.EqualTo("Text"));
                Assert.That(GetText(entries[3]), Is.EqualTo("Second"));
                Assert.That(GetKind(entries[4]), Is.EqualTo("Audio"));
                Assert.That(GetDisplayName(entries[4]), Is.EqualTo("Sound"));
                Assert.That(GetKind(entries[5]), Is.EqualTo("Text"));
                Assert.That(GetText(entries[5]), Is.EqualTo("Last"));
                Assert.That(GetDisplayName(entries[5]), Is.EqualTo("End Text"));
                Assert.That(entries, Has.Count.EqualTo(6));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void StripNumericPrefix_LeavesUnnumberedNamesAlone()
    {
        var method = ControllerType.GetMethod("StripNumericPrefix", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.That((string)method.Invoke(null, new object[] { "12. Intro" }), Is.EqualTo("Intro"));
        Assert.That((string)method.Invoke(null, new object[] { "Intro 12" }), Is.EqualTo("Intro 12"));
    }

    private static IList GetEntries(Component controller, string folder, bool includeAr)
    {
        var method = ControllerType.GetMethod("GetPoiContentEntries", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (IList)method.Invoke(controller, new object[] { folder, includeAr });
    }

    private static string GetKind(object entry)
    {
        return GetField(entry, "kind").ToString();
    }

    private static string GetText(object entry)
    {
        return (string)GetField(entry, "text");
    }

    private static string GetDisplayName(object entry)
    {
        return (string)GetField(entry, "displayName");
    }

    private static object GetField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(field, $"PoiContentEntry.{fieldName} was not found.");
        return field.GetValue(target);
    }
}
