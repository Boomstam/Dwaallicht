using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class NavigationMathTests
{
    private static Type GeoMathType => Type.GetType("Dwaallicht.Navigation.GeoMath, Assembly-CSharp", true);

    [Test]
    public void BearingTo_ReturnsCardinalDirections()
    {
        var origin = new Vector2(51f, 4f);

        Assert.That(BearingTo(origin, new Vector2(52f, 4f)), Is.EqualTo(0f).Within(0.5f));
        Assert.That(BearingTo(origin, new Vector2(51f, 5f)), Is.EqualTo(89.6f).Within(1f));
        Assert.That(BearingTo(origin, new Vector2(50f, 4f)), Is.EqualTo(180f).Within(0.5f));
        Assert.That(BearingTo(origin, new Vector2(51f, 3f)), Is.EqualTo(270.4f).Within(1f));
    }

    [Test]
    public void TileProjection_RoundTripsLatLon()
    {
        var latLon = new Vector2(51.18623f, 4.22974f);
        double x = LongitudeToTileX(latLon.y, 14);
        double y = LatitudeToTileY(latLon.x, 14);

        Assert.That(TileXToLongitude(x, 14), Is.EqualTo(latLon.y).Within(0.00001));
        Assert.That(TileYToLatitude(y, 14), Is.EqualTo(latLon.x).Within(0.00001));
    }

    [Test]
    public void SignedDeltaDegrees_UsesShortestTurn()
    {
        Assert.That(SignedDeltaDegrees(350f, 10f), Is.EqualTo(20f).Within(0.001f));
        Assert.That(SignedDeltaDegrees(10f, 350f), Is.EqualTo(-20f).Within(0.001f));
    }

    private static float BearingTo(Vector2 from, Vector2 to) => Invoke<float>("BearingTo", from, to);
    private static float SignedDeltaDegrees(float from, float to) => Invoke<float>("SignedDeltaDegrees", from, to);
    private static double LongitudeToTileX(double longitude, int zoom) => Invoke<double>("LongitudeToTileX", longitude, zoom);
    private static double LatitudeToTileY(double latitude, int zoom) => Invoke<double>("LatitudeToTileY", latitude, zoom);
    private static double TileXToLongitude(double tileX, int zoom) => Invoke<double>("TileXToLongitude", tileX, zoom);
    private static double TileYToLatitude(double tileY, int zoom) => Invoke<double>("TileYToLatitude", tileY, zoom);

    private static T Invoke<T>(string methodName, params object[] args)
    {
        var method = GeoMathType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, $"GeoMath.{methodName} was not found.");
        return (T)method.Invoke(null, args);
    }
}
