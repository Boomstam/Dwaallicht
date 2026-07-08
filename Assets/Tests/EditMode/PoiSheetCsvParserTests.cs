using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PoiSheetCsvParserTests
{
    private static Type ParserType => Type.GetType("Dwaallicht.Navigation.PoiSheetCsvParser, Assembly-CSharp", true);

    [Test]
    public void TryParse_UsesPublishedRowsAndRedPinColor()
    {
        var csv = "NAME,LATITUDE,LONGITUDE,PUBLISH\n"
            + "Visible,51.09475,4.34779,YES\n"
            + "Hidden,51.1076,4.369738,NO\n";

        Assert.That(TryParse(csv, Color.red, out var pois, out var error), Is.True, error);
        Assert.That(pois, Has.Count.EqualTo(1));
        Assert.That(GetField<string>(pois[0], "title"), Is.EqualTo("Visible"));
        Assert.That(GetField<float>(pois[0], "latitude"), Is.EqualTo(51.09475f).Within(0.00001f));
        Assert.That(GetField<float>(pois[0], "longitude"), Is.EqualTo(4.34779f).Within(0.00001f));
        Assert.That(GetField<Color>(pois[0], "color"), Is.EqualTo(Color.red));
    }

    [Test]
    public void TryParse_CorrectsCurrentSheetLongitudeLatitudeHeaderOrder()
    {
        var csv = "NAME,LONGITUDE,LATITUDE,PUBLISH\n"
            + "t Geleeg,51.09475,4.34779,YES\n";

        Assert.That(TryParse(csv, Color.red, out var pois, out var error), Is.True, error);
        Assert.That(pois, Has.Count.EqualTo(1));
        Assert.That(GetField<float>(pois[0], "latitude"), Is.EqualTo(51.09475f).Within(0.00001f));
        Assert.That(GetField<float>(pois[0], "longitude"), Is.EqualTo(4.34779f).Within(0.00001f));
    }

    private static bool TryParse(string csv, Color color, out IList pois, out string error)
    {
        var method = ParserType.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, "PoiSheetCsvParser.TryParse was not found.");

        var args = new object[] { csv, color, null, null };
        var result = (bool)method.Invoke(null, args);
        pois = (IList)args[2];
        error = (string)args[3];
        return result;
    }

    private static T GetField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(field, $"PointOfInterest.{fieldName} was not found.");
        return (T)field.GetValue(target);
    }
}
