using System.Collections.Generic;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim.World;
using UnityEngine;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// Draws the star systems. One world unit is one light-year. Markers keep the same size on screen at every zoom, so
    /// the map works the same for 8 systems or 50. Sizes and tints come from the theme.
    /// </summary>
    public sealed class GalaxyMapView : MonoBehaviour
    {
        /// <summary>The star marker's size on screen, from the theme.</summary>
        public static float StarPixels => Theme.Map.StarPixels;

        private sealed class Marker
        {
            public int SystemId;
            public Transform Root;
            public SpriteRenderer Star, Glow, Badge;
        }

        private readonly List<Marker> _markers = new List<Marker>();
        private SpriteRenderer _ring;
        private Camera _camera;

        public Galaxy Galaxy { get; private set; }
        public Rect WorldBounds { get; private set; }
        public int MarkerCount => _markers.Count;
        public int SelectedSystemId { get; private set; } = -1;

        public static Vector2 WorldPosition(StarSystem s) => new Vector2(s.XMilliLy / 1000f, s.YMilliLy / 1000f);

        public void Build(Galaxy galaxy, Camera camera)
        {
            Clear();
            Galaxy = galaxy;
            _camera = camera;
            var colours = Theme.Colours;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var s in galaxy.Systems)
            {
                var p = WorldPosition(s);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
                var root = new GameObject("System " + s.Id + " " + s.Name).transform;
                root.SetParent(transform, false);
                root.localPosition = p;
                var m = new Marker
                {
                    SystemId = s.Id,
                    Root = root,
                    Glow = MakeSprite(root, "Glow", "Map/star_glow", colours.StarGlow, 1),
                    Star = MakeSprite(root, "Star", "Map/star", colours.Star, 2),
                };
                if (s.HasShipyard) m.Badge = MakeSprite(root, "Shipyard", "Map/shipyard_badge", colours.ShipyardBadge, 3);
                _markers.Add(m);
            }
            WorldBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            _ring = MakeSprite(transform, "Selection", "Map/selection_ring", colours.SystemRing, 4);
            _ring.enabled = false;
            Select(-1);
        }

        public void Select(int systemId)
        {
            SelectedSystemId = systemId;
            if (_ring == null) return;
            _ring.enabled = systemId >= 0;
            if (systemId >= 0) _ring.transform.localPosition = WorldPosition(Galaxy.Systems[systemId]);
        }

        /// <summary>The system whose marker is nearest the screen point and within <paramref name="maxPixels"/>, or -1.</summary>
        public int PickSystem(Vector2 screenPoint, float maxPixels) => PickSystem(screenPoint, maxPixels, out _);

        public int PickSystem(Vector2 screenPoint, float maxPixels, out float distanceSquared)
        {
            int best = -1;
            distanceSquared = maxPixels * maxPixels;
            if (Galaxy == null) return -1;
            foreach (var s in Galaxy.Systems)
            {
                Vector2 sp = _camera.WorldToScreenPoint(WorldPosition(s));
                float d = (sp - screenPoint).sqrMagnitude;
                if (d <= distanceSquared) { distanceSquared = d; best = s.Id; }
            }
            return best;
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            float unitsPerPixel = 2f * _camera.orthographicSize / Mathf.Max(1, _camera.pixelHeight);
            var map = Theme.Map;
            foreach (var m in _markers)
            {
                SetPixelSize(m.Star, map.StarPixels, unitsPerPixel);
                SetPixelSize(m.Glow, map.StarGlowPixels, unitsPerPixel);
                if (m.Badge != null)
                {
                    SetPixelSize(m.Badge, map.ShipyardBadgePixels, unitsPerPixel);
                    m.Badge.transform.localPosition = new Vector3(map.ShipyardBadgeOffset, map.ShipyardBadgeOffset, 0f) * map.StarPixels * unitsPerPixel;
                }
            }
            if (_ring != null) SetPixelSize(_ring, map.SelectionRingPixels, unitsPerPixel);
        }

        private static void SetPixelSize(SpriteRenderer r, float pixels, float unitsPerPixel)
        {
            float spriteWidth = r.sprite.bounds.size.x;
            if (spriteWidth <= 0f) return;
            r.transform.localScale = Vector3.one * (pixels * unitsPerPixel / spriteWidth);
        }

        private static SpriteRenderer MakeSprite(Transform parent, string name, string key, Color tint, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = ArtLibrary.Get(key);
            r.color = tint;
            r.sortingOrder = order;
            return r;
        }

        private void Clear()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            _markers.Clear();
            _ring = null;
            Galaxy = null;
            SelectedSystemId = -1;
        }
    }
}
