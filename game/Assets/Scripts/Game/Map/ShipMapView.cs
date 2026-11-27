using System.Collections.Generic;
using SpaceTrade.Game.Art;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Voyages;
using UnityEngine;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// Draws the fleet on the map (spec 9.1). A ship in transit sits exactly where the simulation puts it
    /// (<see cref="Simulation.PositionOf"/>); this view only turns milli-light-years into map units. Docked ships fan out
    /// beside their star, at most <see cref="MaxFannedPerStar"/> per star, with a count for the rest.
    /// </summary>
    public sealed class ShipMapView : MonoBehaviour
    {
        public const float IconPixels = 26f;
        public const float DockedIconPixels = 22f;
        public const float GlowPixels = 56f;
        public const float TrailPixels = 52f;
        public const float RingPixels = 44f;
        public const int MaxFannedPerStar = 5;
        public const float FanRadiusPixels = 36f;
        public const float FanFirstAngleDegrees = 150f;
        public const float FanStepDegrees = 40f;
        /// <summary>Where the "+N" count for the docked ships not drawn sits, below the star.</summary>
        public const float OverflowBelowPixels = 62f;

        private static readonly Color GlowTint = new Color(1f, 1f, 1f, 0.75f);
        private static readonly Color TrailTint = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color RingTint = new Color(1f, 0.85f, 0.4f, 1f);

        private sealed class Marker
        {
            public int ShipId;
            public Transform Root;
            public SpriteRenderer Icon, Glow, Trail;
            public Vector2 Anchor, Drawn;
            public bool Visible, InTransit;
        }

        private readonly Dictionary<int, Marker> _markers = new Dictionary<int, Marker>();
        private readonly List<Marker> _ordered = new List<Marker>();
        private readonly Dictionary<int, List<Marker>> _docked = new Dictionary<int, List<Marker>>();
        private readonly List<(int SystemId, int Hidden)> _overflow = new List<(int, int)>();
        private Simulation _sim;
        private Camera _camera;
        private SpriteRenderer _ring;

        public int SelectedShipId { get; set; } = -1;
        public int MarkerCount => _markers.Count;

        /// <summary>Stars with more docked ships than are drawn, and how many are not drawn.</summary>
        public IReadOnlyList<(int SystemId, int Hidden)> Overflow => _overflow;

        public static Vector2 WorldPosition(ShipPosition p) => new Vector2(p.XMilliLy / 1000f, p.YMilliLy / 1000f);

        public void Build(Simulation sim, Camera camera)
        {
            Clear();
            _sim = sim;
            _camera = camera;
            _ring = MakeSprite(transform, "Ship selection", ArtLibrary.Get("Map/selection_ring"), RingTint, 8);
            _ring.enabled = false;
            Refresh();
        }

        /// <summary>Places every ship from the simulation's numbers for the current tick. Called once a frame by the map screen.</summary>
        public void Refresh()
        {
            if (_sim == null || _camera == null) return;
            float upp = 2f * _camera.orthographicSize / Mathf.Max(1, _camera.pixelHeight);
            SyncMarkers();
            foreach (var list in _docked.Values) list.Clear();
            _overflow.Clear();

            var systems = _sim.Market.Galaxy.Systems;
            foreach (var m in _ordered)
            {
                var p = _sim.PositionOf(m.ShipId);
                m.Anchor = WorldPosition(p);
                m.InTransit = p.InTransit;
                if (p.InTransit)
                {
                    m.Drawn = m.Anchor;
                    m.Visible = true;
                    var from = systems[p.OriginId.Value];
                    var to = systems[p.DestinationId.Value];
                    float angle = Mathf.Atan2(to.YMilliLy - from.YMilliLy, to.XMilliLy - from.XMilliLy) * Mathf.Rad2Deg;
                    m.Root.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
                else
                {
                    if (!_docked.TryGetValue(p.DockedSystemId.Value, out var list)) _docked[p.DockedSystemId.Value] = list = new List<Marker>();
                    list.Add(m);
                }
            }

            foreach (var pair in _docked)
            {
                var list = pair.Value;
                if (list.Count == 0) continue;
                int selected = list.FindIndex(m => m.ShipId == SelectedShipId);
                if (selected >= MaxFannedPerStar)
                {
                    var s = list[selected];
                    list.RemoveAt(selected);
                    list.Insert(MaxFannedPerStar - 1, s);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    m.Visible = i < MaxFannedPerStar;
                    float a = (FanFirstAngleDegrees + FanStepDegrees * i) * Mathf.Deg2Rad;
                    m.Drawn = m.Anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (FanRadiusPixels * upp);
                    m.Root.localRotation = Quaternion.identity;
                }
                if (list.Count > MaxFannedPerStar) _overflow.Add((pair.Key, list.Count - MaxFannedPerStar));
            }

            foreach (var m in _ordered)
            {
                m.Root.gameObject.SetActive(m.Visible);
                if (!m.Visible) continue;
                m.Root.localPosition = m.Drawn;
                SetPixelSize(m.Icon, m.InTransit ? IconPixels : DockedIconPixels, upp);
                m.Glow.enabled = m.InTransit;
                m.Trail.enabled = m.InTransit;
                if (m.InTransit)
                {
                    SetPixelSize(m.Glow, GlowPixels, upp);
                    SetPixelSize(m.Trail, TrailPixels, upp);
                    m.Trail.transform.localPosition = new Vector3(-(IconPixels * 0.35f + TrailPixels / 2f) * upp, 0f, 0f);
                }
            }

            bool ringOn = SelectedShipId >= 0 && _markers.TryGetValue(SelectedShipId, out var sel) && sel.Visible;
            _ring.enabled = ringOn;
            if (ringOn)
            {
                _ring.transform.localPosition = _markers[SelectedShipId].Drawn;
                SetPixelSize(_ring, RingPixels, upp);
            }
        }

        /// <summary>Where the ship is drawn this frame (beside its star when docked). False when it is not drawn.</summary>
        public bool TryGetDrawn(int shipId, out Vector2 world)
        {
            world = default;
            if (!_markers.TryGetValue(shipId, out var m) || !m.Visible) return false;
            world = m.Drawn;
            return true;
        }

        /// <summary>The simulation's position for the ship this frame, in map units.</summary>
        public Vector2 AnchorOf(int shipId) => _markers[shipId].Anchor;

        public bool IsDrawn(int shipId) => _markers.TryGetValue(shipId, out var m) && m.Visible;

        public Sprite IconOf(int shipId) => _markers[shipId].Icon.sprite;

        public Transform RootOf(int shipId) => _markers[shipId].Root;

        /// <summary>How many ship icons are drawn beside this star.</summary>
        public int DrawnDockedAt(int systemId)
        {
            if (!_docked.TryGetValue(systemId, out var list)) return 0;
            int n = 0;
            foreach (var m in list) if (m.Visible) n++;
            return n;
        }

        /// <summary>The drawn ship nearest the screen point and within <paramref name="maxPixels"/>, or -1.</summary>
        public int PickShip(Vector2 screenPoint, float maxPixels, out float distanceSquared)
        {
            int best = -1;
            distanceSquared = maxPixels * maxPixels;
            if (_camera == null) return -1;
            foreach (var m in _ordered)
            {
                if (!m.Visible) continue;
                Vector2 sp = _camera.WorldToScreenPoint(m.Drawn);
                float d = (sp - screenPoint).sqrMagnitude;
                if (d <= distanceSquared) { distanceSquared = d; best = m.ShipId; }
            }
            return best;
        }

        private void SyncMarkers()
        {
            var ships = _sim.Ships;
            if (ships.Count == _ordered.Count) return;
            foreach (var ship in ships)
            {
                if (_markers.ContainsKey(ship.Id)) continue;
                var root = new GameObject("Ship " + ship.Id + " " + ship.Name).transform;
                root.SetParent(transform, false);
                var m = new Marker
                {
                    ShipId = ship.Id,
                    Root = root,
                    Glow = MakeSprite(root, "Glow", ArtLibrary.Get("Ships/warp_glow"), GlowTint, 5),
                    Trail = MakeSprite(root, "Trail", ArtLibrary.Get("Ships/engine_trail"), TrailTint, 6),
                    Icon = MakeSprite(root, "Icon", ArtLibrary.ShipMapIcon(ship.ClassId), Color.white, 7),
                };
                _markers[ship.Id] = m;
                _ordered.Add(m);
            }
            _ordered.Sort((a, b) => a.ShipId.CompareTo(b.ShipId));
        }

        private static void SetPixelSize(SpriteRenderer r, float pixels, float unitsPerPixel)
        {
            float spriteWidth = r.sprite.bounds.size.x;
            if (spriteWidth <= 0f) return;
            r.transform.localScale = Vector3.one * (pixels * unitsPerPixel / spriteWidth);
        }

        private static SpriteRenderer MakeSprite(Transform parent, string name, Sprite sprite, Color tint, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = tint;
            r.sortingOrder = order;
            return r;
        }

        private void Clear()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            _markers.Clear();
            _ordered.Clear();
            _docked.Clear();
            _overflow.Clear();
            _ring = null;
            _sim = null;
            SelectedShipId = -1;
        }
    }
}
