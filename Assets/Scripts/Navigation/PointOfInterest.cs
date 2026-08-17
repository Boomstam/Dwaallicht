using System;
using UnityEngine;

namespace Dwaallicht.Navigation
{
    [Serializable]
    public sealed class PointOfInterest
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "Nieuw punt";
        public string category = "Algemeen";
        [TextArea] public string description = "";
        public bool hasAr;
        public float latitude = 51.18623f;
        public float longitude = 4.22974f;
        public Color color = new Color(0.12f, 0.55f, 0.95f, 1f);
        public bool active = true;
        public bool hidden;

        public Vector2 LatLon => new Vector2(latitude, longitude);

        public void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }
        }
    }
}
