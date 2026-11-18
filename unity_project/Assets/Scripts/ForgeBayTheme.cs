using System.Collections.Generic;
using UnityEngine;

public static class ForgeBayTheme
{
    private static readonly Dictionary<Color32, Texture2D> texCache = new Dictionary<Color32, Texture2D>();

    // Core Industrial CAD Palette
    public static readonly Color BgDeep        = new Color(0.045f, 0.062f, 0.088f, 0.95f);
    public static readonly Color BgPanel       = new Color(0.068f, 0.092f, 0.128f, 0.94f);
    public static readonly Color BgHeader      = new Color(0.095f, 0.130f, 0.180f, 0.98f);
    public static readonly Color BgCode        = new Color(0.032f, 0.044f, 0.062f, 0.99f);
    public static readonly Color BgButton      = new Color(0.115f, 0.155f, 0.215f, 0.96f);
    public static readonly Color BgButtonHover = new Color(0.165f, 0.225f, 0.310f, 0.98f);
    public static readonly Color BorderSubtle  = new Color(0.220f, 0.300f, 0.400f, 0.65f);
    public static readonly Color AccentCyan    = new Color(0.220f, 0.780f, 0.980f, 1.00f);
    public static readonly Color AccentEmerald = new Color(0.160f, 0.920f, 0.620f, 1.00f);
    public static readonly Color AccentAmber   = new Color(0.980f, 0.740f, 0.180f, 1.00f);
    public static readonly Color AccentCrimson = new Color(0.940f, 0.260f, 0.300f, 1.00f);
    public static readonly Color TextPrimary   = new Color(0.930f, 0.960f, 0.990f, 1.00f);
    public static readonly Color TextMuted     = new Color(0.600f, 0.690f, 0.790f, 1.00f);

    private static int lastUiBlockFrame = -1;
    public static bool IsPointerOverUI => Time.frameCount - lastUiBlockFrame <= 1;

    public static void BlockSceneScrollIfHovered(Rect uiRect)
    {
        if (uiRect.Contains(Event.current.mousePosition))
            lastUiBlockFrame = Time.frameCount;
    }

    public static Texture2D GetTex(Color c)
    {
        Color32 key = c;
        if (texCache.TryGetValue(key, out Texture2D existing) && existing != null)
            return existing;

        Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, c);
        t.Apply();
        texCache[key] = t;
        return t;
    }

    public static void DrawCard(Rect rect, Color bgColor, Color borderColor, Color? topAccent = null)
    {
        GUI.DrawTexture(rect, GetTex(bgColor));

        Texture2D bTex = GetTex(borderColor);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), bTex);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), bTex);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), bTex);
        GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), bTex);

        if (topAccent.HasValue)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2.5f), GetTex(topAccent.Value));
        }
    }

    public static bool DrawPillButton(Rect rect, string label, bool active = false, Color? customAccent = null)
    {
        bool hovered = rect.Contains(Event.current.mousePosition);
        Color accent = customAccent ?? AccentCyan;

        Color bg = active
            ? new Color(accent.r * 0.24f, accent.g * 0.28f, accent.b * 0.34f, 0.98f)
            : (hovered ? BgButtonHover : BgButton);

        Color border = active ? accent : (hovered ? new Color(accent.r, accent.g, accent.b, 0.75f) : BorderSubtle);
        DrawCard(rect, bg, border, active ? accent : (Color?)null);

        GUIStyle st = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = active ? Color.white : (hovered ? Color.white : TextPrimary) }
        };

        return GUI.Button(rect, label, st);
    }

    public static GUIStyle LabelStyle(int size, Color color, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            alignment = align,
            wordWrap = true,
            normal = { textColor = color }
        };
    }
}
