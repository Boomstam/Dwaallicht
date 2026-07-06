using UnityEngine;

namespace Dwaallicht.Navigation
{
    public static class GeoMath
    {
        private const float EarthRadiusMeters = 6371000f;

        public static float NormalizeDegrees(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        public static float BearingTo(Vector2 fromLatLon, Vector2 toLatLon)
        {
            float lat1 = fromLatLon.x * Mathf.Deg2Rad;
            float lat2 = toLatLon.x * Mathf.Deg2Rad;
            float dLon = (toLatLon.y - fromLatLon.y) * Mathf.Deg2Rad;

            float y = Mathf.Sin(dLon) * Mathf.Cos(lat2);
            float x = Mathf.Cos(lat1) * Mathf.Sin(lat2) -
                      Mathf.Sin(lat1) * Mathf.Cos(lat2) * Mathf.Cos(dLon);

            return NormalizeDegrees(Mathf.Atan2(y, x) * Mathf.Rad2Deg);
        }

        public static float SignedDeltaDegrees(float fromDegrees, float toDegrees)
        {
            return Mathf.DeltaAngle(fromDegrees, toDegrees);
        }

        public static float DistanceMeters(Vector2 fromLatLon, Vector2 toLatLon)
        {
            float lat1 = fromLatLon.x * Mathf.Deg2Rad;
            float lat2 = toLatLon.x * Mathf.Deg2Rad;
            float dLat = (toLatLon.x - fromLatLon.x) * Mathf.Deg2Rad;
            float dLon = (toLatLon.y - fromLatLon.y) * Mathf.Deg2Rad;

            float sinLat = Mathf.Sin(dLat * 0.5f);
            float sinLon = Mathf.Sin(dLon * 0.5f);
            float a = sinLat * sinLat + Mathf.Cos(lat1) * Mathf.Cos(lat2) * sinLon * sinLon;
            float c = 2f * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1f - a));
            return EarthRadiusMeters * c;
        }

        public static double LongitudeToTileX(double longitude, int zoom)
        {
            double n = 1 << zoom;
            return (longitude + 180.0) / 360.0 * n;
        }

        public static double LatitudeToTileY(double latitude, int zoom)
        {
            double latRad = latitude * Mathf.Deg2Rad;
            double n = 1 << zoom;
            return (1.0 - System.Math.Log(System.Math.Tan(latRad) + 1.0 / System.Math.Cos(latRad)) / System.Math.PI) * 0.5 * n;
        }

        public static double TileXToLongitude(double tileX, int zoom)
        {
            double n = 1 << zoom;
            return tileX / n * 360.0 - 180.0;
        }

        public static double TileYToLatitude(double tileY, int zoom)
        {
            double n = 1 << zoom;
            double radians = System.Math.Atan(System.Math.Sinh(System.Math.PI * (1.0 - 2.0 * tileY / n)));
            return radians * Mathf.Rad2Deg;
        }
    }
}
