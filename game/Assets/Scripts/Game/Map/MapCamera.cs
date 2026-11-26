using UnityEngine;

namespace SpaceTrade.Game.Map
{
    /// <summary>
    /// Pan and zoom for the orthographic map camera. The zoom limits and the pan limits come from the galaxy's own size,
    /// so the whole galaxy always fits on screen at the widest zoom, however many systems it has.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MapCamera : MonoBehaviour
    {
        /// <summary>The closest zoom: half the visible height, in light-years.</summary>
        public const float MinHalfHeightLy = 1.5f;
        /// <summary>Room left around the galaxy at the widest zoom, as a share of its size, plus a fixed margin in light-years.</summary>
        public const float FitMarginShare = 0.12f;
        public const float FitMarginLy = 1.5f;
        /// <summary>How far past the outermost systems the centre of the view may go, in light-years.</summary>
        public const float PanMarginLy = 3f;

        private Camera _camera;
        private Rect _bounds;

        public Camera Camera => _camera != null ? _camera : _camera = GetComponent<Camera>();
        public float MinSize => MinHalfHeightLy;

        /// <summary>The widest zoom: the whole galaxy with a margin, at the current window shape.</summary>
        public float MaxSize
        {
            get
            {
                float aspect = Mathf.Max(0.1f, Camera.aspect);
                float half = Mathf.Max(_bounds.height / 2f, _bounds.width / 2f / aspect);
                return Mathf.Max(half * (1f + FitMarginShare) + FitMarginLy, MinSize * 2f);
            }
        }

        public float UnitsPerPixel => 2f * Camera.orthographicSize / Mathf.Max(1, Camera.pixelHeight);

        public void Configure(Rect galaxyBounds)
        {
            _bounds = galaxyBounds;
            Camera.orthographic = true;
            FitAll();
        }

        public void FitAll()
        {
            Camera.orthographicSize = MaxSize;
            SetCentre(_bounds.center);
        }

        public Vector2 Centre => transform.position;

        public void SetCentre(Vector2 centre)
        {
            float x = Mathf.Clamp(centre.x, _bounds.xMin - PanMarginLy, _bounds.xMax + PanMarginLy);
            float y = Mathf.Clamp(centre.y, _bounds.yMin - PanMarginLy, _bounds.yMax + PanMarginLy);
            transform.position = new Vector3(x, y, transform.position.z);
        }

        public void PanWorld(Vector2 delta) => SetCentre(Centre + delta);

        /// <summary>Drags the map by a screen movement in pixels, so the point under the cursor stays under it.</summary>
        public void DragPixels(Vector2 pixelDelta) => PanWorld(-pixelDelta * UnitsPerPixel);

        /// <summary>Zooms by a factor (below 1 zooms in) keeping the world point under the screen point fixed.</summary>
        public void ZoomAt(float factor, Vector2 screenPoint)
        {
            Vector2 before = Camera.ScreenToWorldPoint(screenPoint);
            float size = Mathf.Clamp(Camera.orthographicSize * factor, MinSize, MaxSize);
            Camera.orthographicSize = size;
            Vector2 after = Camera.ScreenToWorldPoint(screenPoint);
            PanWorld(before - after);
        }

        /// <summary>Keeps the zoom inside its limits if the window changes shape.</summary>
        private void LateUpdate()
        {
            if (_bounds.width <= 0f && _bounds.height <= 0f) return;
            float size = Mathf.Clamp(Camera.orthographicSize, MinSize, MaxSize);
            if (!Mathf.Approximately(size, Camera.orthographicSize)) Camera.orthographicSize = size;
        }
    }
}
