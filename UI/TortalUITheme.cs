using System;
using UnityEngine;

namespace TortalPortal
{
    // Look and feel for the portal windows, built to the gilt picture-frame reference: slim polished double
    // rails running each edge, a big acanthus flourish mitring every corner, and a palmette crest set into
    // the middle of the top and bottom rails. All ornament, no motion - "scrolling" here is scrollwork.
    //
    // Every pixel is generated at runtime, for the same reason the rune VFX is (see TortalRuneTexture): the mod
    // ships no AssetBundle, so a texture that is not computed in code does not exist. The textures are static
    // and flagged HideAndDontSave - without that Unity collects them on the next scene load and OnGUI starts
    // handing destroyed textures to the draw calls the moment the player reloads a world.
    //
    // Shapes are painted into a coverage buffer plus a HEIGHT buffer, then embossed at bake time against one
    // fixed light over the top-left shoulder. Doing the lighting once, globally, at the end is what keeps four
    // separately generated edges and four mirrored corners agreeing on where the light is - and it means a
    // mirrored corner is still lit correctly, because mirroring only moves pixels, never the lamp.
    internal static class TortalUITheme
    {
        public static readonly Color GoldDeep = new Color(0.26f, 0.17f, 0.05f, 1f);
        public static readonly Color Gold = new Color(0.80f, 0.62f, 0.26f, 1f);
        public static readonly Color GoldBright = new Color(1.00f, 0.94f, 0.72f, 1f);
        public static readonly Color Parchment = new Color(0.90f, 0.86f, 0.75f, 1f);
        public static readonly Color Muted = new Color(0.60f, 0.56f, 0.48f, 1f);

        // Rich-text colours, kept beside the Color values they mirror so the two never drift apart.
        public const string HexMuted = "#9A9182";
        public const string HexLocked = "#E0736B";
        public const string HexOpen = "#87D278";

        // ---- frame metrics -------------------------------------------------------------------------------
        public const float Band = 18f;          // rail band thickness, and the rail textures' across size
        public const float Pad = 22f;           // clears the corner flourishes, which reach 36px deep

        // Grow with UITextScale, because they are the bands the title and footer text sit in. The fixed part of
        // TitleHeight is the crest clearance: the top palmette reaches 26px into the window, so the title has
        // to start below that whatever the text is doing.
        public static float TitleHeight = 54f;
        public static float FooterHeight = 30f;

        // What UITextScale resolved to for the styles currently built. Scale layout metrics that hold text
        // through S() so they grow with the font instead of clipping it.
        public static float TextScale { get; private set; } = 1f;

        // UIFontSizeDelta: a flat point offset applied after the scale. Fatty's port of this theme carries
        // the same knob at the same +2 default, so both mods' windows read at one size out of the box.
        private static int _fontDelta;

        public static float S(float v) => Mathf.Round(v * TextScale);

        private const int CornerTile = 84;      // corner texture is square
        private const float Overhang = 10f;     // how far corners and crests sit outside the window rect
        private const float CornerExtent = CornerTile - Overhang;   // rail run starts this far along each edge
        private const int CrestW = 56;
        private const int CrestH = 34;
        private const float CrestOverhang = 8f;

        // Rail cross-section, measured from the OUTER edge of the band inwards.
        private const float OuterMid = 4.4f, OuterHalf = 2.7f;
        private const float InnerMid = 12.4f, InnerHalf = 1.6f;
        private const float BendCentre = 32f;   // corner arc centre, in corner-tile space (includes Overhang)

        public static GUIStyle Title, SubTitle, Header, Key, Value, Note, Footer, Button, Primary, Row, Field, ImageButton;

        private static Texture2D _railTop, _railBottom, _railLeft, _railRight;
        private static Texture2D _cornerTL, _cornerTR, _cornerBL, _cornerBR;
        private static Texture2D _crestTop, _crestBottom, _diamond;
        private static Texture2D _panel, _white;
        private static Texture2D _btnNormal, _btnHover, _btnActive;
        private static Texture2D _rowNormal, _rowHover, _rowActive, _selection;
        private static Texture2D _fieldTex;

        // Call at the top of every OnGUI. The null check is not laziness: these are UnityEngine.Objects, so
        // "already built" and "still alive" are different questions.
        public static void EnsureBuilt()
        {
            if (_railTop != null)
            {
                // Styles are cheap to rebuild and textures are not, so UITextScale is picked up live without
                // regenerating a single pixel of ornament.
                float wanted = Configuration.uiTextScale != null ? Configuration.uiTextScale.Value : 1f;
                int wantedDelta = Configuration.uiFontSizeDelta != null ? Configuration.uiFontSizeDelta.Value : 0;
                if (Title == null || !Mathf.Approximately(wanted, TextScale) || wantedDelta != _fontDelta)
                {
                    BuildStyles(wanted, wantedDelta);
                }
                return;
            }

            _railTop = BuildRail(Edge.Top);
            _railBottom = BuildRail(Edge.Bottom);
            _railLeft = BuildRail(Edge.Left);
            _railRight = BuildRail(Edge.Right);

            _cornerTL = BuildCorner(false, false);
            _cornerTR = BuildCorner(true, false);
            _cornerBL = BuildCorner(false, true);
            _cornerBR = BuildCorner(true, true);

            _crestTop = BuildCrest(false);
            _crestBottom = BuildCrest(true);
            _diamond = BuildDiamond(14);

            _panel = BuildPanel(64);
            _white = BuildSolid(Color.white);

            _btnNormal = BuildPatch(new Color(0.100f, 0.085f, 0.065f, 0.95f), GoldDeep);
            _btnHover = BuildPatch(new Color(0.190f, 0.150f, 0.090f, 0.97f), Gold);
            _btnActive = BuildPatch(new Color(0.300f, 0.230f, 0.110f, 0.98f), GoldBright);

            _rowNormal = BuildPatch(Color.clear, Color.clear);
            _rowHover = BuildPatch(new Color(0.80f, 0.62f, 0.26f, 0.10f), new Color(0.80f, 0.62f, 0.26f, 0.45f));
            _rowActive = BuildPatch(new Color(0.80f, 0.62f, 0.26f, 0.20f), Gold);
            _selection = BuildSolid(new Color(0.80f, 0.62f, 0.26f, 0.15f));

            _fieldTex = BuildPatch(new Color(0.03f, 0.03f, 0.025f, 0.95f), GoldDeep);

            BuildStyles(Configuration.uiTextScale != null ? Configuration.uiTextScale.Value : 1f,
                        Configuration.uiFontSizeDelta != null ? Configuration.uiFontSizeDelta.Value : 0);
        }

        // ---- window chrome -------------------------------------------------------------------------------

        public static void DrawWindow(Rect win, string title)
        {
            EnsureBuilt();

            GUI.DrawTexture(new Rect(win.x + 1f, win.y + 1f, win.width - 2f, win.height - 2f), _panel, ScaleMode.StretchToFill);
            DrawFrame(win);

            Rect titleRect = new Rect(win.x + CornerExtent, win.y + Band + 8f, win.width - 2f * CornerExtent, TitleHeight - 20f);
            DrawShadowed(titleRect, title.ToUpperInvariant(), Title);

            DrawRule(new Rect(win.x + Band + Pad, win.y + Band + TitleHeight - 12f, win.width - 2f * (Band + Pad), 1f));
        }

        // The content rectangle inside the frame, the title and the footer strip.
        public static Rect Body(Rect win)
        {
            return new Rect(
                win.x + Band + Pad,
                win.y + Band + TitleHeight,
                win.width - 2f * (Band + Pad),
                win.height - 2f * Band - TitleHeight - FooterHeight);
        }

        public static Rect FooterLine(Rect win)
        {
            return new Rect(win.x + Band + Pad, win.yMax - Band - FooterHeight, win.width - 2f * (Band + Pad), S(18f));
        }

        private static void DrawFrame(Rect r)
        {
            float spanX = r.width - 2f * CornerExtent;
            float spanY = r.height - 2f * CornerExtent;

            GUI.DrawTexture(new Rect(r.x + CornerExtent, r.y, spanX, Band), _railTop);
            GUI.DrawTexture(new Rect(r.x + CornerExtent, r.yMax - Band, spanX, Band), _railBottom);
            GUI.DrawTexture(new Rect(r.x, r.y + CornerExtent, Band, spanY), _railLeft);
            GUI.DrawTexture(new Rect(r.xMax - Band, r.y + CornerExtent, Band, spanY), _railRight);

            float far = CornerTile - Overhang;
            GUI.DrawTexture(new Rect(r.x - Overhang, r.y - Overhang, CornerTile, CornerTile), _cornerTL);
            GUI.DrawTexture(new Rect(r.xMax - far, r.y - Overhang, CornerTile, CornerTile), _cornerTR);
            GUI.DrawTexture(new Rect(r.x - Overhang, r.yMax - far, CornerTile, CornerTile), _cornerBL);
            GUI.DrawTexture(new Rect(r.xMax - far, r.yMax - far, CornerTile, CornerTile), _cornerBR);

            float cx = r.center.x - CrestW * 0.5f;
            GUI.DrawTexture(new Rect(cx, r.y - CrestOverhang, CrestW, CrestH), _crestTop);
            GUI.DrawTexture(new Rect(cx, r.yMax + CrestOverhang - CrestH, CrestW, CrestH), _crestBottom);

            // The hairline where the frame meets the picture. No outer box line - it would slice straight
            // through the corner flourishes that deliberately overhang the rect.
            DrawOutline(new Rect(r.x + Band, r.y + Band, r.width - 2f * Band, r.height - 2f * Band),
                        new Color(Gold.r, Gold.g, Gold.b, 0.45f), 1f);
        }

        // ---- primitives ----------------------------------------------------------------------------------

        public static void DrawOutline(Rect r, Color c, float t)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), _white);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), _white);
            GUI.color = prev;
        }

        public static void DrawFill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        // A recessed well - used behind the portal list and the preview slot.
        public static void DrawInset(Rect r)
        {
            DrawFill(r, new Color(0f, 0f, 0f, 0.45f));
            DrawOutline(r, new Color(Gold.r, Gold.g, Gold.b, 0.32f), 1f);
        }

        public static void DrawSelection(Rect r)
        {
            GUI.DrawTexture(r, _selection);
        }

        // A hairline with a lozenge set into the middle of it.
        public static void DrawRule(Rect r)
        {
            DrawFill(r, new Color(Gold.r, Gold.g, Gold.b, 0.40f));
            const float d = 14f;
            GUI.DrawTexture(new Rect(r.center.x - d * 0.5f, r.y - d * 0.5f + 0.5f, d, d), _diamond);
        }

        // IMGUI has no text shadow, so draw the string twice. Mutating the shared style is safe here - OnGUI
        // is single threaded and the colour is put back before anything else can read it.
        public static void DrawShadowed(Rect r, string text, GUIStyle style)
        {
            Color prev = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = prev;
            GUI.Label(r, text, style);
        }

        // ---- styles --------------------------------------------------------------------------------------

        private static void BuildStyles(float scale, int fontDelta)
        {
            TextScale = Mathf.Clamp(scale, 0.6f, 2f);
            _fontDelta = Mathf.Clamp(fontDelta, -6, 12);

            // The title and footer bands have to grow with the delta as well as the scale, or a positive
            // delta just clips the text they exist to hold. Only a POSITIVE delta widens them - shrinking the
            // text must not pull the title band up into the crest, which is fixed ornament at 26px.
            float slack = Mathf.Max(0, _fontDelta);
            TitleHeight = 26f + Mathf.Round(28f * TextScale) + Mathf.Round(slack * 1.5f);
            FooterHeight = Mathf.Max(30f, Mathf.Round(30f * TextScale) + slack);

            Font font = FindFont();

            Title = Text(font, Pt(25), FontStyle.Bold, GoldBright, TextAnchor.MiddleCenter);
            SubTitle = Text(font, Pt(18), FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
            Header = Text(font, Pt(13), FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
            Key = Text(font, Pt(13), FontStyle.Normal, Muted, TextAnchor.MiddleLeft);
            Value = Text(font, Pt(13), FontStyle.Bold, Parchment, TextAnchor.MiddleLeft);
            Note = Text(font, Pt(13), FontStyle.Italic, Muted, TextAnchor.MiddleCenter);
            Note.wordWrap = true;
            Footer = Text(font, Pt(11), FontStyle.Normal, Muted, TextAnchor.MiddleCenter);

            Button = Patch(font, Pt(13), FontStyle.Bold, TextAnchor.MiddleCenter, _btnNormal, _btnHover, _btnActive);
            Button.padding = new RectOffset(12, 12, 6, 6);

            Primary = Patch(font, Pt(16), FontStyle.Bold, TextAnchor.MiddleCenter, _btnNormal, _btnHover, _btnActive);
            Primary.padding = new RectOffset(12, 12, 8, 8);
            Primary.normal.textColor = Gold;

            Row = Patch(font, Pt(14), FontStyle.Normal, TextAnchor.MiddleLeft, _rowNormal, _rowHover, _rowActive);
            Row.padding = new RectOffset(12, 12, 4, 4);
            Row.normal.textColor = Parchment;

            // Transparent until hovered, when the row textures give the picture a gold edge - the affordance
            // for "this opens full size".
            ImageButton = Patch(font, Pt(11), FontStyle.Normal, TextAnchor.LowerCenter, _rowNormal, _rowHover, _rowActive);

            Field = new GUIStyle
            {
                font = font,
                fontSize = Pt(13),
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 4, 4),
                border = new RectOffset(3, 3, 3, 3),
                clipping = TextClipping.Clip
            };
            Field.normal.background = _fieldTex;
            Field.focused.background = _fieldTex;
            Field.hover.background = _fieldTex;
            Field.active.background = _fieldTex;
            Field.normal.textColor = Parchment;
            Field.focused.textColor = GoldBright;
            Field.hover.textColor = Parchment;
            Field.active.textColor = Parchment;
        }

        // Scale first, then the flat delta - so UITextScale stays a proportional zoom of the whole window and
        // the delta stays worth exactly what it says in points, whatever the scale is set to.
        private static int Pt(int designSize)
        {
            return Mathf.Max(8, Mathf.RoundToInt(designSize * TextScale) + _fontDelta);
        }

        private static GUIStyle Text(Font font, int size, FontStyle fs, Color colour, TextAnchor anchor)
        {
            GUIStyle s = new GUIStyle
            {
                font = font,
                fontSize = size,
                fontStyle = fs,
                alignment = anchor,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            s.normal.textColor = colour;
            return s;
        }

        private static GUIStyle Patch(Font font, int size, FontStyle fs, TextAnchor anchor, Texture2D normal, Texture2D hover, Texture2D active)
        {
            GUIStyle s = new GUIStyle
            {
                font = font,
                fontSize = size,
                fontStyle = fs,
                alignment = anchor,
                richText = true,
                wordWrap = false,
                border = new RectOffset(3, 3, 3, 3),
                clipping = TextClipping.Clip
            };
            s.normal.background = normal;
            s.hover.background = hover;
            s.active.background = active;
            s.focused.background = normal;
            s.normal.textColor = Parchment;
            s.hover.textColor = GoldBright;
            s.active.textColor = GoldBright;
            s.focused.textColor = Parchment;
            return s;
        }

        // Borrow the game's own body font so the window sits in Valheim rather than on top of it. Returning
        // null is a valid answer - IMGUI then falls back to GUI.skin.font - and it is the right answer when
        // nothing matches, because Arial.ttf stopped being a builtin resource in the Unity version Valheim
        // ships on, so asking for it would just hand back null anyway.
        private static Font FindFont()
        {
            Font[] all = Resources.FindObjectsOfTypeAll<Font>();
            string[] preferred = { "AveriaSerifLibre", "Norsebold", "Norse" };

            foreach (string want in preferred)
            {
                foreach (Font f in all)
                {
                    if (f != null && f.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return f;
                }
            }
            return null;
        }

        // ---- ornament -------------------------------------------------------------------------------------

        private enum Edge { Top, Bottom, Left, Right }

        // A straight run of the double rail. Only four pixels long: the profile is constant along the edge,
        // so IMGUI stretching it across the whole window costs nothing and stays perfectly sharp.
        //
        // Which side of the band is "outer" flips per edge, and that is the whole reason there are four of
        // these rather than one rotated texture - it is what puts the thick rail on the outside all the way
        // round, and lets the global bake light shade each edge the way a real frame catches light.
        private static Texture2D BuildRail(Edge edge)
        {
            bool horizontal = edge == Edge.Top || edge == Edge.Bottom;
            int w = horizontal ? 4 : (int)Band;
            int h = horizontal ? (int)Band : 4;
            Painter p = new Painter(w, h);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float across;
                    switch (edge)
                    {
                        case Edge.Top: across = y; break;
                        case Edge.Bottom: across = h - 1 - y; break;
                        case Edge.Left: across = x; break;
                        default: across = w - 1 - x; break;
                    }
                    p.RailPixel(x, y, Mathf.Abs(across - OuterMid), OuterHalf);
                    p.RailPixel(x, y, Mathf.Abs(across - InnerMid), InnerHalf);
                }
            }

            return p.Bake();
        }

        // The corner flourish: both rails mitred round a rounded elbow, a palmette sitting outside the bend on
        // the diagonal, and - repeated for each of the two edges via the painter's diagonal transpose - a
        // volute, a leaf frond and a small terminal curl running back along the rail.
        //
        // The ornament is deliberately shallow (nothing reaches past ~36px from the edge) and long: that is
        // both what the reference does and what keeps it clear of Body(), which starts at Band + Pad = 40.
        private static Texture2D BuildCorner(bool mirrorX, bool mirrorY)
        {
            Painter p = new Painter(CornerTile, CornerTile) { MirrorX = mirrorX, MirrorY = mirrorY };

            p.RailElbow(OuterMid + Overhang, OuterHalf, BendCentre);
            p.RailElbow(InnerMid + Overhang, InnerHalf, BendCentre);

            // Finial outside the bend on the diagonal, tied back into the outer rail so it reads as cast onto
            // the frame rather than floating beside it - which is exactly how it looked before the taper.
            p.Lozenge(11f, 11f, 4.5f);
            p.Taper(new Vector2(13.5f, 13.5f), new Vector2(19.3f, 19.3f), 1.2f, 2.2f);

            // Boss in the pocket inside the elbow. Self-symmetric about the diagonal, so it is painted once.
            p.Lozenge(36f, 36f, 3.4f);

            for (int pass = 0; pass < 2; pass++)
            {
                p.Transpose = pass == 1;

                // Stem linking the boss to this edge's volute, so the two edges read as one ornament.
                p.Taper(new Vector2(38.5f, 35f), new Vector2(47f, 32f), 1.4f, 0.45f);
                // Volute: 1.1 turns of a logarithmic spiral, thin at the eye and thickening to the tail. Its
                // eye is kept well clear of the bend centre - sat on top of the elbow it just read as a blob.
                p.Spiral(new Vector2(50f, 31f), 1.3f, 0.33f, 0f, 6.8f, 5.371f, 0.55f, 2.4f);
                // Leaf frond sweeping on along the rail, dying out before the tile edge meets the straight run.
                p.Bezier(new Vector2(56f, 36f), new Vector2(68f, 41f), new Vector2(78f, 30f), 2.2f, 0.35f);
                // Terminal curl where the frond runs out.
                p.Spiral(new Vector2(76f, 32f), 0.9f, 0.32f, 0f, 4.6f, 1.0f, 1.1f, 0.3f);
                // Bead over the joint between volute and frond.
                p.Disc(58f, 34f, 1.8f);
            }
            p.Transpose = false;

            return p.Bake();
        }

        // The palmette set into the middle of the top and bottom rails: a lozenge on a short spine with a
        // symmetric pair of curls falling away from it.
        private static Texture2D BuildCrest(bool flip)
        {
            Painter p = new Painter(CrestW, CrestH) { MirrorY = flip };
            const float mid = CrestW * 0.5f;

            p.Disc(mid, 2.5f, 1.8f);
            p.Lozenge(mid, 9f, 6.5f);
            p.Taper(new Vector2(mid, 14f), new Vector2(mid, 24f), 2.2f, 1.2f);
            p.Spiral(new Vector2(mid - 11f, 25f), 1.2f, 0.33f, 0f, 5.4f, 2.6f, 2.0f, 0.35f);
            p.Spiral(new Vector2(mid + 11f, 25f), 1.2f, 0.33f, 0f, 5.4f, 2.6f, 2.0f, 0.35f, true);
            p.Bezier(new Vector2(mid - 5f, 19f), new Vector2(mid - 15f, 24f), new Vector2(mid - 24f, 16f), 1.6f, 0.3f);
            p.Bezier(new Vector2(mid + 5f, 19f), new Vector2(mid + 15f, 24f), new Vector2(mid + 24f, 16f), 1.6f, 0.3f);
            p.Disc(mid - 25f, 14f, 1.5f);
            p.Disc(mid + 25f, 14f, 1.5f);

            return p.Bake();
        }

        private static Texture2D BuildDiamond(int size)
        {
            Painter p = new Painter(size, size);
            float mid = (size - 1) * 0.5f;
            p.Lozenge(mid, mid, size * 0.36f);
            return p.Bake();
        }

        // ---- flat textures ---------------------------------------------------------------------------------

        // Warm near-black leather that lifts very slightly towards the middle. The vignette is radial so it
        // still reads correctly however far IMGUI stretches it.
        private static Texture2D BuildPanel(int size)
        {
            Texture2D tex = New(size, size, TextureWrapMode.Clamp, UnityEngine.FilterMode.Bilinear);
            Color[] px = new Color[size * size];
            float mid = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - mid) / mid, ny = (y - mid) / mid;
                    float v = Mathf.Clamp01(1f - 0.55f * Mathf.Sqrt(0.6f * (nx * nx + ny * ny)));
                    px[y * size + x] = new Color(0.055f * v + 0.020f, 0.048f * v + 0.017f, 0.038f * v + 0.013f, 0.955f);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        // A 9-sliced patch: one pixel of border around a flat fill. Point filtered and sliced at 3px so the
        // outline stays exactly one pixel however far the control stretches.
        private static Texture2D BuildPatch(Color fill, Color border)
        {
            const int s = 12;
            Texture2D tex = New(s, s, TextureWrapMode.Clamp, UnityEngine.FilterMode.Point);
            Color[] px = new Color[s * s];

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(s - 1 - x, s - 1 - y));
                    px[y * s + x] = edge == 0 ? border : fill;
                }
            }

            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        private static Texture2D BuildSolid(Color c)
        {
            Texture2D tex = New(1, 1, TextureWrapMode.Clamp, UnityEngine.FilterMode.Point);
            tex.SetPixel(0, 0, c);
            tex.Apply(false);
            return tex;
        }

        // UnityEngine.FilterMode is spelled out throughout this file: the mod defines its own FilterMode enum
        // for the item filters, and in this namespace that one wins.
        private static Texture2D New(int w, int h, TextureWrapMode wrap, UnityEngine.FilterMode filter)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = filter,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        // ---- the painter -------------------------------------------------------------------------------------

        // Paints coverage and height into two buffers, then bakes both into a lit metal texture.
        //
        // Coordinates are design-space pixels with y pointing DOWN, which is how the shapes above are laid out
        // and how they read on screen. MirrorX/MirrorY place the same design in the other three corners, and
        // Transpose reflects it about the diagonal to repeat an edge's ornament on the adjoining edge. All
        // three only move pixels, so the bake light stays put no matter which variant is being drawn.
        private sealed class Painter
        {
            public bool MirrorX, MirrorY, Transpose;

            private readonly int _w, _h;
            private readonly float[] _a;
            private readonly float[] _z;

            public Painter(int w, int h)
            {
                _w = w;
                _h = h;
                _a = new float[w * h];
                _z = new float[w * h];
            }

            // ---- writes

            private void Put(float fx, float fy, float a, float z)
            {
                if (a <= 0f) return;

                if (Transpose) { float t = fx; fx = fy; fy = t; }
                int x = Mathf.RoundToInt(fx);
                int y = Mathf.RoundToInt(fy);
                if (MirrorX) x = _w - 1 - x;
                if (MirrorY) y = _h - 1 - y;
                if (x < 0 || y < 0 || x >= _w || y >= _h) return;

                int i = y * _w + x;
                if (a > _a[i]) _a[i] = a;
                if (z > _z[i]) _z[i] = z;
            }

            // A soft dome. Everything except the rails and the lozenge is built out of these.
            public void Disc(float cx, float cy, float r)
            {
                if (r <= 0f) return;
                int x0 = Mathf.FloorToInt(cx - r - 1f), x1 = Mathf.CeilToInt(cx + r + 1f);
                int y0 = Mathf.FloorToInt(cy - r - 1f), y1 = Mathf.CeilToInt(cy + r + 1f);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r + 0.5f - d);
                        if (a <= 0f) continue;
                        Put(x, y, a, Mathf.Sqrt(Mathf.Max(0f, 1f - (d / r) * (d / r))));
                    }
                }
            }

            public void Lozenge(float cx, float cy, float r)
            {
                int x0 = Mathf.FloorToInt(cx - r - 1f), x1 = Mathf.CeilToInt(cx + r + 1f);
                int y0 = Mathf.FloorToInt(cy - r - 1f), y1 = Mathf.CeilToInt(cy + r + 1f);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
                        float a = Mathf.Clamp01(r + 0.5f - d);
                        if (a <= 0f) continue;
                        Put(x, y, a, Mathf.Sqrt(Mathf.Max(0f, 1f - (d / r) * (d / r))));
                    }
                }
            }

            public void Taper(Vector2 a, Vector2 b, float w0, float w1)
            {
                int steps = Mathf.Max(8, Mathf.CeilToInt(Vector2.Distance(a, b) * 3f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector2 p = Vector2.Lerp(a, b, t);
                    Disc(p.x, p.y, Mathf.Lerp(w0, w1, t));
                }
            }

            public void Bezier(Vector2 a, Vector2 b, Vector2 c, float w0, float w1)
            {
                int steps = Mathf.Max(16, Mathf.CeilToInt((Vector2.Distance(a, b) + Vector2.Distance(b, c)) * 3f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float u = 1f - t;
                    Vector2 p = u * u * a + 2f * u * t * b + t * t * c;
                    Disc(p.x, p.y, Mathf.Lerp(w0, w1, t));
                }
            }

            // A logarithmic spiral - r grows as r0 * e^(growth * t) - stroked from the eye outward. This is
            // the volute the whole frame hangs on, so it gets a real curve rather than a stack of arcs.
            public void Spiral(Vector2 eye, float r0, float growth, float t0, float t1, float phase, float w0, float w1, bool mirror = false)
            {
                int steps = Mathf.Max(48, Mathf.CeilToInt((t1 - t0) * 40f));
                for (int i = 0; i <= steps; i++)
                {
                    float k = i / (float)steps;
                    float t = Mathf.Lerp(t0, t1, k);
                    float r = r0 * Mathf.Exp(growth * t);
                    float ang = t + phase;
                    float dx = Mathf.Cos(ang) * r;
                    float dy = Mathf.Sin(ang) * r;
                    if (mirror) dx = -dx;
                    Disc(eye.x + dx, eye.y + dy, Mathf.Lerp(w0, w1, k));
                }
            }

            // One pixel of a straight rail, given its distance from the rail centreline.
            public void RailPixel(int x, int y, float d, float half)
            {
                float a = Mathf.Clamp01(half + 0.5f - d);
                if (a <= 0f) return;
                Put(x, y, a, Mathf.Sqrt(Mathf.Max(0f, 1f - (d / half) * (d / half))));
            }

            // A rail mitred round a corner: straight runs at "mid" from each edge, joined by an arc centred on
            // (centre, centre). Every rail shares that centre, so the two bends stay concentric and the double
            // rail keeps its spacing all the way round the turn.
            public void RailElbow(float mid, float half, float centre)
            {
                float r = centre - mid;

                for (int y = 0; y < _h; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        float d;
                        if (x <= centre && y <= centre)
                        {
                            float dx = x - centre, dy = y - centre;
                            d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r);
                        }
                        else if (y <= centre) d = Mathf.Abs(y - mid);
                        else if (x <= centre) d = Mathf.Abs(x - mid);
                        else d = Mathf.Min(Mathf.Abs(y - mid), Mathf.Abs(x - mid));

                        RailPixel(x, y, d, half);
                    }
                }
            }

            // ---- bake

            public Texture2D Bake()
            {
                Texture2D tex = New(_w, _h, TextureWrapMode.Clamp, UnityEngine.FilterMode.Bilinear);
                Color[] px = new Color[_w * _h];

                for (int y = 0; y < _h; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        int i = y * _w + x;
                        float a = _a[i];
                        // Texture row 0 is the bottom of the image, design y counts down from the top.
                        int o = (_h - 1 - y) * _w + x;

                        if (a <= 0f) { px[o] = Color.clear; continue; }

                        float s = (Z(x - 1, y) - Z(x + 1, y)) + (Z(x, y - 1) - Z(x, y + 1));
                        float shade = Mathf.Clamp01(0.42f + 0.50f * s + 0.18f * _z[i]);
                        Color c = shade < 0.5f
                            ? Color.Lerp(GoldDeep, Gold, shade * 2f)
                            : Color.Lerp(Gold, GoldBright, (shade - 0.5f) * 2f);

                        px[o] = new Color(c.r, c.g, c.b, a);
                    }
                }

                tex.SetPixels(px);
                tex.Apply(false);
                return tex;
            }

            private float Z(int x, int y)
            {
                return _z[Mathf.Clamp(y, 0, _h - 1) * _w + Mathf.Clamp(x, 0, _w - 1)];
            }
        }
    }
}
