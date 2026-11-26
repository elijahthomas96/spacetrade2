using System.Collections.Generic;
using UnityEngine;

namespace SpaceTrade.Game.Art
{
    /// <summary>
    /// Looks up art by fixed name (spec 9.6). A name with no file gets a generated stand-in, so a missing or not-yet-drawn
    /// image never stops the game and never logs an error. Industry and commodity icons are looked up by tuning id.
    /// </summary>
    public static class ArtLibrary
    {
        /// <summary>
        /// The four default industries whose icon files were named before their tuning ids existed. Every other industry
        /// id x, including any added to tuning.json later, loads Icons/industry_x.
        /// </summary>
        private static readonly Dictionary<string, string> IndustryFileForDefaultId = new Dictionary<string, string>
        {
            { "mining_colony", "industry_mining" },
            { "agricultural_system", "industry_agriculture" },
            { "population_hub", "industry_population" },
            { "shipyard_works", "industry_shipyard" },
        };

        private static Dictionary<string, Sprite> _sprites;
        private static readonly Dictionary<string, Sprite> StandIns = new Dictionary<string, Sprite>();

        private static Dictionary<string, Sprite> Sprites
        {
            get
            {
                if (_sprites == null) Load(Resources.Load<ArtCatalog>(ArtCatalog.ResourceName));
                return _sprites;
            }
        }

        /// <summary>Uses the given catalog (null means no art files at all, so everything is a stand-in).</summary>
        public static void Load(ArtCatalog catalog)
        {
            _sprites = new Dictionary<string, Sprite>();
            if (catalog == null) return;
            foreach (var e in catalog.Entries)
                if (!string.IsNullOrEmpty(e.key) && e.sprite != null) _sprites[e.key] = e.sprite;
        }

        public static bool HasFile(string key) => Sprites.ContainsKey(key);

        /// <summary>The sprite for a key such as "Map/star", or a stand-in when there is no such file.</summary>
        public static Sprite Get(string key)
        {
            if (Sprites.TryGetValue(key, out var sprite)) return sprite;
            if (!StandIns.TryGetValue(key, out sprite))
            {
                sprite = MakeStandIn(key);
                StandIns[key] = sprite;
            }
            return sprite;
        }

        public static string IndustryIconKey(string industryId) =>
            "Icons/" + (IndustryFileForDefaultId.TryGetValue(industryId, out var file) ? file : "industry_" + industryId);

        public static string CommodityIconKey(string commodityId) => "Icons/cargo_" + commodityId;

        public static Sprite IndustryIcon(string industryId) => Get(IndustryIconKey(industryId));

        public static Sprite CommodityIcon(string commodityId) => Get(CommodityIconKey(commodityId));

        /// <summary>A white ring with a cross on transparent, so code can tint it like the real icons.</summary>
        private static Sprite MakeStandIn(string key)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "StandIn " + key, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                bool ring = r > size * 0.38f && r < size * 0.47f;
                bool cross = r < size * 0.38f && (Mathf.Abs(dx - dy) < 2.5f || Mathf.Abs(dx + dy) < 2.5f);
                pixels[y * size + x] = ring || cross ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = tex.name;
            return sprite;
        }

        /// <summary>True when the sprite is a generated stand-in rather than a file.</summary>
        public static bool IsStandIn(Sprite sprite) => sprite != null && sprite.name.StartsWith("StandIn ");
    }
}
