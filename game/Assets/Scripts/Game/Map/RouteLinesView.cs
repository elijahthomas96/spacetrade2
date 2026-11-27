using System.Collections.Generic;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using UnityEngine;

namespace SpaceTrade.Game.Map
{
    /// <summary>How a leg is drawn, weakest first. When two ships share a leg the stronger style wins.</summary>
    public enum RouteLineStyle
    {
        /// <summary>A paused route of the selected ship: drawn dimmed. Other paused routes are not drawn.</summary>
        PausedRoute = 0,
        /// <summary>A leg of an active automated route.</summary>
        Route = 1,
        /// <summary>The leg a ship is flying now.</summary>
        CurrentLeg = 2,
        /// <summary>The selected ship's current leg or active route.</summary>
        Selected = 3,
    }

    /// <summary>
    /// Draws every ship's current leg and every active route's whole loop (spec 9.1), with an arrow showing which way each
    /// leg runs. The selected ship's route is always drawn, dimmed while paused. Lines keep the same width on screen at
    /// every zoom.
    /// </summary>
    public sealed class RouteLinesView : MonoBehaviour
    {
        /// <summary>How far along a leg its arrow sits (from the theme), so the two directions of a shared line keep their arrows apart.</summary>
        public static float ArrowAlong => Theme.Routes.ArrowAlong;

        private Simulation _sim;
        private Camera _camera;
        private Texture2D _lineTexture;
        private Sprite _lineSprite;
        private readonly Dictionary<(int From, int To), RouteLineStyle> _legs = new Dictionary<(int, int), RouteLineStyle>();
        private readonly List<(int From, int To, RouteLineStyle Style)> _segments = new List<(int, int, RouteLineStyle)>();
        private readonly List<(SpriteRenderer Line, SpriteRenderer Arrow)> _pool = new List<(SpriteRenderer, SpriteRenderer)>();

        /// <summary>The legs drawn in the last frame, by system id, with their style.</summary>
        public IReadOnlyList<(int From, int To, RouteLineStyle Style)> Segments => _segments;

        public static float Alpha(RouteLineStyle style)
        {
            var t = Theme.Routes;
            switch (style)
            {
                case RouteLineStyle.PausedRoute: return t.PausedRouteAlpha;
                case RouteLineStyle.Route: return t.RouteAlpha;
                case RouteLineStyle.CurrentLeg: return t.CurrentLegAlpha;
                default: return t.SelectedAlpha;
            }
        }

        public static float WidthPixels(RouteLineStyle style)
        {
            var t = Theme.Routes;
            return style == RouteLineStyle.Selected ? t.SelectedWidth : style == RouteLineStyle.CurrentLeg ? t.CurrentLegWidth : t.RouteWidth;
        }

        public void Build(Simulation sim, Camera camera)
        {
            _sim = sim;
            _camera = camera;
            if (_lineTexture == null)
            {
                _lineTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Route line", wrapMode = TextureWrapMode.Clamp };
                // A plain white square: the line's colour comes from the theme as the sprite's tint.
                var white = new Color[16];
                for (int i = 0; i < white.Length; i++) white[i] = Color.white;
                _lineTexture.SetPixels(white);
                _lineTexture.Apply(false, true);
                _lineSprite = Sprite.Create(_lineTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
                _lineSprite.name = "Route line";
            }
            Refresh(-1);
        }

        /// <summary>Works out which legs to draw from the fleet's voyages and routes, and draws them. Called once a frame by the map screen.</summary>
        public void Refresh(int selectedShipId)
        {
            if (_sim == null || _camera == null) return;
            _legs.Clear();
            foreach (var ship in _sim.Ships)
            {
                bool selected = ship.Id == selectedShipId;
                var route = ship.Route;
                if (route != null && (route.IsActive || selected))
                {
                    var style = route.IsPaused ? RouteLineStyle.PausedRoute : selected ? RouteLineStyle.Selected : RouteLineStyle.Route;
                    var stops = route.Stops;
                    for (int i = 0; i < stops.Count; i++)
                        Add(stops[i].SystemId, stops[(i + 1) % stops.Count].SystemId, style);
                }
                if (ship.Voyage != null)
                    Add(ship.Voyage.OriginId, ship.Voyage.DestinationId, selected ? RouteLineStyle.Selected : RouteLineStyle.CurrentLeg);
            }

            _segments.Clear();
            foreach (var pair in _legs) _segments.Add((pair.Key.From, pair.Key.To, pair.Value));
            _segments.Sort((a, b) => a.Style != b.Style ? a.Style.CompareTo(b.Style) : a.From != b.From ? a.From.CompareTo(b.From) : a.To.CompareTo(b.To));

            float upp = 2f * _camera.orthographicSize / Mathf.Max(1, _camera.pixelHeight);
            var systems = _sim.Market.Galaxy.Systems;
            for (int i = 0; i < _segments.Count; i++)
            {
                if (i == _pool.Count) _pool.Add(MakePair());
                var (line, arrow) = _pool[i];
                var seg = _segments[i];
                Vector2 a = GalaxyMapView.WorldPosition(systems[seg.From]);
                Vector2 b = GalaxyMapView.WorldPosition(systems[seg.To]);
                Vector2 d = b - a;
                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                Color colour = Theme.Colours.RouteLine;
                colour.a = Alpha(seg.Style);
                line.gameObject.SetActive(true);
                line.color = colour;
                line.transform.localPosition = (a + b) / 2f;
                line.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                line.transform.localScale = new Vector3(d.magnitude, WidthPixels(seg.Style) * upp, 1f);
                arrow.gameObject.SetActive(true);
                arrow.color = colour;
                arrow.transform.localPosition = a + d * ArrowAlong;
                arrow.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                float arrowWidth = arrow.sprite.bounds.size.x;
                if (arrowWidth > 0f) arrow.transform.localScale = Vector3.one * (Theme.Routes.ArrowPixels * upp / arrowWidth);
            }
            for (int i = _segments.Count; i < _pool.Count; i++)
            {
                _pool[i].Line.gameObject.SetActive(false);
                _pool[i].Arrow.gameObject.SetActive(false);
            }
        }

        private void Add(int from, int to, RouteLineStyle style)
        {
            if (from == to) return;
            if (!_legs.TryGetValue((from, to), out var had) || style > had) _legs[(from, to)] = style;
        }

        private (SpriteRenderer, SpriteRenderer) MakePair()
        {
            var line = new GameObject("Leg").AddComponent<SpriteRenderer>();
            line.transform.SetParent(transform, false);
            line.sprite = _lineSprite;
            line.sortingOrder = 0;
            var arrow = new GameObject("Arrow").AddComponent<SpriteRenderer>();
            arrow.transform.SetParent(transform, false);
            arrow.sprite = ArtLibrary.Get("Map/route_arrow");
            arrow.sortingOrder = 0;
            return (line, arrow);
        }

        private void OnDestroy()
        {
            if (_lineSprite != null) Destroy(_lineSprite);
            if (_lineTexture != null) Destroy(_lineTexture);
        }
    }
}
