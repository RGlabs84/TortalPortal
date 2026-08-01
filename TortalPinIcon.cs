using System;
using System.IO;
using UnityEngine;

namespace TortalPortal
{
    // The map-marker art for the two non-vanilla PortalPinIcon choices.
    //
    // Rune is generated at runtime like every other piece of art in this mod: one of the Elder Futhark glyphs
    // from TortalRuneTexture, sat on a dark disc inside a bright ring so it reads as a marker rather than a
    // loose squiggle at map scale. Custom is a TortalPortalPin.png dropped beside the plugin DLL - the "admins
    // make their own" path. The pin is drawn on every client from that client's own file, so a server that
    // wants one pin for everybody ships the PNG in its modpack next to the DLL.
    public static class TortalPinIcon
    {
        private static Sprite _rune;
        private static Sprite _custom;
        private static bool _customTried;

        // Never returns null: Custom falls back to Rune when no PNG is there, and Rune is generated code that
        // cannot fail to exist. Vanilla icons never reach this - the caller maps those to Minimap.PinType.
        public static Sprite Get(PortalPinIcon which)
        {
            if (which == PortalPinIcon.Custom)
            {
                Sprite custom = LoadCustom();
                if (custom != null) return custom;
            }
            return GetRune();
        }

        private static Sprite GetRune()
        {
            if (_rune != null) return _rune;

            Texture2D tex = BuildRunePin(new Color(0.30f, 1.00f, 0.55f, 1f));
            _rune = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _rune;
        }

        // The rune texture is a glyph over transparency; alone on the map it reads as debris. Composite it
        // over a dark backing disc and put a ring round it, and it reads as a deliberate marker at any zoom.
        private static Texture2D BuildRunePin(Color colour)
        {
            // Glyph 7 rather than the colour-hashed pick: the marker should be the SAME rune on every client
            // and every session, so two players comparing maps are looking at the same symbol.
            Texture2D rune = TortalRuneTexture.GenerateRune(colour, 7);
            int s = rune.width;
            Color[] px = rune.GetPixels();

            float mid = (s - 1) * 0.5f;
            float discR = s * 0.42f;
            float ringR = s * 0.45f;
            float ringW = s * 0.035f;

            Color disc = new Color(0.02f, 0.07f, 0.04f, 0.80f);
            Color ring = new Color(colour.r, colour.g, colour.b, 1f);

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    int i = y * s + x;
                    float dx = x - mid, dy = y - mid;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // Backing disc UNDER the glyph, so the rune stays the brightest thing in the marker.
                    float discA = Mathf.Clamp01(discR - d) * disc.a;
                    Color under = new Color(disc.r, disc.g, disc.b, discA);
                    Color over = px[i];
                    float outA = over.a + under.a * (1f - over.a);
                    Color c = outA > 0f
                        ? new Color((over.r * over.a + under.r * under.a * (1f - over.a)) / outA,
                                    (over.g * over.a + under.g * under.a * (1f - over.a)) / outA,
                                    (over.b * over.a + under.b * under.a * (1f - over.a)) / outA,
                                    outA)
                        : Color.clear;

                    // Ring on top of everything.
                    float ringA = Mathf.Clamp01(ringW + 0.5f - Mathf.Abs(d - ringR));
                    if (ringA > 0f)
                    {
                        float a = ringA + c.a * (1f - ringA);
                        c = new Color((ring.r * ringA + c.r * c.a * (1f - ringA)) / Mathf.Max(a, 0.0001f),
                                      (ring.g * ringA + c.g * c.a * (1f - ringA)) / Mathf.Max(a, 0.0001f),
                                      (ring.b * ringA + c.b * c.a * (1f - ringA)) / Mathf.Max(a, 0.0001f),
                                      a);
                    }

                    px[i] = c;
                }
            }

            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = UnityEngine.FilterMode.Bilinear,
                // Same lesson as the UI theme: without this Unity collects the texture on the next scene
                // load and the map starts drawing a destroyed sprite.
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels(px);
            tex.Apply(false);

            // The intermediate rune texture is not kept - everything it had to say is composited in.
            UnityEngine.Object.Destroy(rune);
            return tex;
        }

        private static Sprite LoadCustom()
        {
            if (_customTried) return _custom;
            _customTried = true;

            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location) ?? "", "TortalPortalPin.png");
                if (!File.Exists(path))
                {
                    Plugin.Log.LogInfo("TortalPortal: PortalPinIcon is Custom but there is no TortalPortalPin.png beside the plugin - using the Rune pin instead.");
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = UnityEngine.FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
                PortalUIManager.LoadImageViaReflection(tex, bytes);
                _custom = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                Plugin.Log.LogInfo($"TortalPortal: loaded custom map pin from {path} ({tex.width}x{tex.height}).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"TortalPortal: could not load TortalPortalPin.png (non-fatal, using the Rune pin). Reason: {ex}");
                _custom = null;
            }

            return _custom;
        }
    }
}
