using UnityEngine;

namespace Dwaallicht.Navigation
{
    public sealed class PoiMarker : MonoBehaviour
    {
        public PointOfInterest Poi { get; private set; }

        public void Bind(PointOfInterest poi)
        {
            Poi = poi;
            name = "POI_" + (poi?.title ?? "Marker");
        }
    }
}
