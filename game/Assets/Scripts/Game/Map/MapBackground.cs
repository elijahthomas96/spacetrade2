using SpaceTrade.Game.Art;
using UnityEngine;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// The tiled starfield and the nebula over it. Each tile is one screen high at any zoom, so the backdrop never blurs
    /// or shrinks to noise, and it drifts slowly as the map pans, which reads as distance.
    /// </summary>
    public sealed class MapBackground : MonoBehaviour
    {
        private sealed class Layer
        {
            public SpriteRenderer Renderer;
            public float TileScreens;
            public float DriftTilesPerLy;
        }

        private Layer _stars, _nebula;
        private Camera _camera;

        public void Build(Camera camera)
        {
            _camera = camera;
            _stars = MakeLayer("Starfield", "Map/starfield", -100, 1f, 0.012f);
            _nebula = MakeLayer("Nebula", "Map/nebula_overlay", -90, 1.6f, 0.006f);
        }

        private Layer MakeLayer(string name, string key, int order, float tileScreens, float drift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = ArtLibrary.Get(key);
            r.drawMode = SpriteDrawMode.Tiled;
            r.sortingOrder = order;
            return new Layer { Renderer = r, TileScreens = tileScreens, DriftTilesPerLy = drift };
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            Place(_stars);
            Place(_nebula);
        }

        private void Place(Layer layer)
        {
            var r = layer.Renderer;
            Vector2 spriteSize = r.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;
            float viewHeight = 2f * _camera.orthographicSize;
            float viewWidth = viewHeight * _camera.aspect;
            float scale = viewHeight * layer.TileScreens / spriteSize.y;
            Vector2 tile = spriteSize * scale;
            Vector2 cam = _camera.transform.position;
            Vector2 drift = cam * layer.DriftTilesPerLy;
            Vector2 frac = new Vector2(drift.x - Mathf.Floor(drift.x), drift.y - Mathf.Floor(drift.y));
            int cols = Mathf.CeilToInt(viewWidth / tile.x) + 2;
            int rows = Mathf.CeilToInt(viewHeight / tile.y) + 2;
            r.transform.localScale = Vector3.one * scale;
            r.size = new Vector2(cols * spriteSize.x, rows * spriteSize.y);
            r.transform.position = new Vector3(cam.x - frac.x * tile.x, cam.y - frac.y * tile.y, 0f);
        }
    }
}
