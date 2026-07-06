using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Dwaallicht.Navigation
{
    [AddComponentMenu("Dwaallicht/Navigation/Map Navigation Overlay")]
    public sealed class MapNavigationOverlay : MonoBehaviour
    {
        [SerializeField] private MapController mapController;
        [SerializeField] private CompassHeadingProvider headingProvider;
        [SerializeField] private PoiManager poiManager;
        [SerializeField] private Camera mapCamera;
        [SerializeField] private float markerSize = 55f;
        [SerializeField] private float markerHeight = 8f;
        [SerializeField] private bool showAdminPanel = true;

        private readonly Dictionary<string, GameObject> markers = new Dictionary<string, GameObject>();
        private GameObject facingArrow;
        private LineRenderer routeLine;
        private string newPoiTitle = "Nieuw punt";
        private string newPoiCategory = "Algemeen";
        private string newPoiDescription = "";
        private Vector2 pendingPoiLatLon;
        private bool hasPendingPoi;
        private Coroutine initializeRoutine;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (poiManager != null)
            {
                poiManager.PoisChanged += RebuildMarkers;
                poiManager.SelectionChanged += HandleSelectionChanged;
            }

            initializeRoutine = StartCoroutine(InitializeWhenReady());
        }

        private void OnDisable()
        {
            if (initializeRoutine != null)
            {
                StopCoroutine(initializeRoutine);
                initializeRoutine = null;
            }

            if (poiManager != null)
            {
                poiManager.PoisChanged -= RebuildMarkers;
                poiManager.SelectionChanged -= HandleSelectionChanged;
            }
        }

        private void HandleSelectionChanged(PointOfInterest poi)
        {
            UpdateMarkerSelection();
            UpdateRouteLine();
        }

        private void Update()
        {
            RefreshRuntimeOverlay();
        }

        public void RefreshRuntimeOverlay()
        {
            ResolveReferences();
            if (mapController == null || !mapController.isLoaded)
            {
                return;
            }

            if (markers.Count == 0 && poiManager != null)
            {
                RebuildMarkers();
            }

            EnsureFacingArrow();
            UpdateFacingArrow();
            UpdateRouteLine();
            HandleSelectionClick();
        }

        private IEnumerator InitializeWhenReady()
        {
            while (true)
            {
                ResolveReferences();
                if (mapController != null && mapController.isLoaded && headingProvider != null && poiManager != null)
                {
                    RebuildMarkers();
                    EnsureFacingArrow();
                    UpdateFacingArrow();
                    UpdateRouteLine();
                    yield break;
                }

                yield return null;
            }
        }

        private void ResolveReferences()
        {
            mapController = mapController != null ? mapController : FindFirstObjectByType<MapController>();
            headingProvider = headingProvider != null ? headingProvider : FindFirstObjectByType<CompassHeadingProvider>();
            poiManager = poiManager != null ? poiManager : FindFirstObjectByType<PoiManager>();
            mapCamera = mapCamera != null ? mapCamera : Camera.main;
        }

        private void RebuildMarkers()
        {
            foreach (var marker in markers.Values)
            {
                Destroy(marker);
            }

            markers.Clear();

            if (mapController == null || !mapController.isLoaded || poiManager == null)
            {
                return;
            }

            foreach (var poi in poiManager.Pois)
            {
                if (!poi.active)
                {
                    continue;
                }

                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.transform.SetParent(transform, false);
                marker.transform.position = mapController.GeoToWorldPosition(poi.LatLon, markerHeight);
                marker.transform.localScale = new Vector3(markerSize, markerHeight, markerSize);
                marker.AddComponent<PoiMarker>().Bind(poi);

                var renderer = marker.GetComponent<Renderer>();
                renderer.material = CreateMaterial(poi.color);
                renderer.material.color = poi.color;

                markers.Add(poi.id, marker);
            }

            UpdateMarkerSelection();
        }

        private void UpdateMarkerSelection()
        {
            if (poiManager == null)
            {
                return;
            }

            foreach (var pair in markers)
            {
                var selected = poiManager.SelectedPoi != null && pair.Key == poiManager.SelectedPoi.id;
                pair.Value.transform.localScale = selected
                    ? new Vector3(markerSize * 1.35f, markerHeight * 1.8f, markerSize * 1.35f)
                    : new Vector3(markerSize, markerHeight, markerSize);
            }
        }

        private void EnsureFacingArrow()
        {
            if (facingArrow != null)
            {
                return;
            }

            facingArrow = new GameObject("PhoneFacingArrow");
            facingArrow.transform.SetParent(transform, false);

            var line = facingArrow.AddComponent<LineRenderer>();
            line.positionCount = 4;
            line.loop = false;
            line.useWorldSpace = true;
            line.startWidth = markerSize * 0.22f;
            line.endWidth = markerSize * 0.05f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = Color.black;
            line.endColor = Color.black;
        }

        private void UpdateFacingArrow()
        {
            if (headingProvider == null || !headingProvider.IsReady)
            {
                return;
            }

            var center = mapController.GeoToWorldPosition(headingProvider.CurrentLatLon, markerHeight * 3f);
            var direction = HeadingToWorldDirection(headingProvider.Heading);
            var right = Vector3.Cross(Vector3.up, direction).normalized;
            float length = markerSize * 3.2f;
            float wing = markerSize * 0.95f;

            var line = facingArrow.GetComponent<LineRenderer>();
            line.SetPosition(0, center);
            line.SetPosition(1, center + direction * length);
            line.SetPosition(2, center + direction * (length - wing) + right * wing * 0.45f);
            line.SetPosition(3, center + direction * length);
        }

        private void UpdateRouteLine()
        {
            if (poiManager == null || poiManager.SelectedPoi == null || headingProvider == null || !headingProvider.IsReady)
            {
                if (routeLine != null)
                {
                    routeLine.enabled = false;
                }

                return;
            }

            if (routeLine == null)
            {
                var route = new GameObject("SelectedPoiRoute");
                route.transform.SetParent(transform, false);
                routeLine = route.AddComponent<LineRenderer>();
                routeLine.positionCount = 2;
                routeLine.useWorldSpace = true;
                routeLine.startWidth = markerSize * 0.12f;
                routeLine.endWidth = markerSize * 0.12f;
                routeLine.material = new Material(Shader.Find("Sprites/Default"));
                routeLine.startColor = Color.black;
            }

            routeLine.enabled = true;
            routeLine.endColor = poiManager.SelectedPoi.color;
            routeLine.SetPosition(0, mapController.GeoToWorldPosition(headingProvider.CurrentLatLon, markerHeight * 2f));
            routeLine.SetPosition(1, mapController.GeoToWorldPosition(poiManager.SelectedPoi.LatLon, markerHeight * 2f));
        }

        private void HandleSelectionClick()
        {
            if (!Input.GetMouseButtonDown(0) || mapCamera == null || poiManager == null)
            {
                return;
            }

            var ray = mapCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
            {
                return;
            }

            var marker = hit.collider.GetComponentInParent<PoiMarker>();
            if (marker != null)
            {
                poiManager.SelectPoi(marker.Poi);
                return;
            }

            if (showAdminPanel && mapController.TryWorldToGeo(hit.point, out var latLon))
            {
                pendingPoiLatLon = latLon;
                hasPendingPoi = true;
            }
        }

        private void OnGUI()
        {
            if (!showAdminPanel || poiManager == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(16f, 16f, 310f, 420f), GUI.skin.window);
            GUILayout.Label("POI beheer");

            if (headingProvider != null)
            {
                GUILayout.Label($"Heading: {headingProvider.Heading:0} graden ({headingProvider.Status})");
            }

            if (poiManager.SelectedPoi != null && headingProvider != null)
            {
                var bearing = GeoMath.BearingTo(headingProvider.CurrentLatLon, poiManager.SelectedPoi.LatLon);
                var turn = GeoMath.SignedDeltaDegrees(headingProvider.Heading, bearing);
                var distance = GeoMath.DistanceMeters(headingProvider.CurrentLatLon, poiManager.SelectedPoi.LatLon);
                GUILayout.Label($"Navigatie: {poiManager.SelectedPoi.title}");
                GUILayout.Label($"Afstand: {distance:0} m  Draai: {turn:+0;-0;0} graden");
            }

            GUILayout.Space(8f);
            GUILayout.Label("POI's");
            foreach (var poi in poiManager.Pois)
            {
                if (GUILayout.Button(poi.title))
                {
                    poiManager.SelectPoi(poi);
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label(hasPendingPoi
                ? $"Nieuw punt op {pendingPoiLatLon.x:0.00000}, {pendingPoiLatLon.y:0.00000}"
                : "Klik op de kaart om een nieuw punt te kiezen.");
            newPoiTitle = GUILayout.TextField(newPoiTitle);
            newPoiCategory = GUILayout.TextField(newPoiCategory);
            newPoiDescription = GUILayout.TextArea(newPoiDescription, GUILayout.Height(48f));

            GUI.enabled = hasPendingPoi;
            if (GUILayout.Button("Voeg POI toe"))
            {
                poiManager.AddPoi(newPoiTitle, pendingPoiLatLon, newPoiCategory, newPoiDescription);
                hasPendingPoi = false;
                newPoiTitle = "Nieuw punt";
                newPoiDescription = "";
            }

            GUI.enabled = poiManager.SelectedPoi != null;
            if (GUILayout.Button("Verwijder geselecteerde POI"))
            {
                poiManager.RemovePoi(poiManager.SelectedPoi);
            }

            GUI.enabled = true;
            GUILayout.EndArea();
        }

        private static Vector3 HeadingToWorldDirection(float heading)
        {
            float radians = heading * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(radians), 0f, -Mathf.Cos(radians)).normalized;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader);
            material.color = color;
            return material;
        }
    }
}
