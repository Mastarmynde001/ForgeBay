using UnityEngine;

public class ForgeBayLandingHero : MonoBehaviour
{
    private bool isHeroVisible = true;
    private float overlayAlpha = 1.0f;
    private bool isDismissing = false;

    private GUIStyle badgeStyle;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle cardTitleStyle;
    private GUIStyle cardBodyStyle;

    private void InitStyles()
    {
        if (titleStyle != null) return;

        badgeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ForgeBayTheme.AccentEmerald }
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            wordWrap = true,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.76f, 0.85f, 0.94f, 1f) }
        };

        cardTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = ForgeBayTheme.AccentCyan }
        };

        cardBodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = new Color(0.82f, 0.88f, 0.95f, 1f) }
        };
    }

    private void Update()
    {
        // Press [M] anytime to toggle the Hero Mission Brief overlay back on/off
        if (GUIUtility.keyboardControl == 0 && Input.GetKeyDown(KeyCode.M))
        {
            if (!isHeroVisible)
            {
                isHeroVisible = true;
                isDismissing = false;
                overlayAlpha = 1f;
            }
            else
            {
                isDismissing = true;
            }
        }

        if (isDismissing)
        {
            overlayAlpha = Mathf.MoveTowards(overlayAlpha, 0f, Time.deltaTime * 3.5f);
            if (overlayAlpha <= 0.01f)
            {
                overlayAlpha = 0f;
                isDismissing = false;
                isHeroVisible = false;
            }
        }
    }

    private void OnGUI()
    {
        InitStyles();

        if (!isHeroVisible)
        {
            // Subtle top-right pill below Telemetry so you can re-open the Hero Pitch Card anytime
            if (ForgeBayTheme.DrawPillButton(new Rect(Screen.width - 236f, 114f, 222f, 26f), "✦  [M] MISSION OVERVIEW", false, ForgeBayTheme.AccentCyan))
            {
                isHeroVisible = true;
                isDismissing = false;
                overlayAlpha = 1f;
            }
            return;
        }

        Color prevColor = GUI.color;
        GUI.depth = -200; // Render cleanly above all workspace HUD while active

        // 1. Sleek Translucent Backdrop Tint (keeps live moving 3D hardware visible behind!)
        GUI.color = new Color(1f, 1f, 1f, overlayAlpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
            ForgeBayTheme.GetTex(new Color(0.025f, 0.036f, 0.055f, 0.72f)));

        // Block 3D scroll zoom while hero overlay is active
        ForgeBayTheme.BlockSceneScrollIfHovered(new Rect(0f, 0f, Screen.width, Screen.height));

        // 2. Centered Glassmorphic Hero Command Deck
        float deckW = Mathf.Min(780f, Screen.width - 48f);
        float deckH = 410f;
        Rect deckRect = new Rect((Screen.width - deckW) * 0.5f, (Screen.height - deckH) * 0.5f, deckW, deckH);

        ForgeBayTheme.DrawCard(deckRect,
            new Color(0.045f, 0.065f, 0.095f, 0.94f),
            new Color(0.22f, 0.78f, 0.98f, 0.75f),
            ForgeBayTheme.AccentEmerald);

        // Top Live Status Pill
        float pillW = 310f;
        Rect livePill = new Rect(deckRect.x + (deckW - pillW) * 0.5f, deckRect.y + 24f, pillW, 24f);
        ForgeBayTheme.DrawCard(livePill,
            new Color(0.06f, 0.22f, 0.17f, 0.95f),
            ForgeBayTheme.AccentEmerald);
        GUI.Label(livePill, "●  LIVE HARDWARE-IN-THE-LOOP ENGINE", badgeStyle);

        // Main Hero Display Title
        GUI.Label(new Rect(deckRect.x + 24f, deckRect.y + 56f, deckW - 48f, 54f),
            "FORGEBAY  3D", titleStyle);

        // Crisp Value-Proposition Subtitle
        GUI.Label(new Rect(deckRect.x + 64f, deckRect.y + 112f, deckW - 128f, 44f),
            "Browser-Native Embedded Systems & Robotics Digital Twin.\nSummon CAD hardware, route color-coded DuPont circuits, and flash real C++ & MicroPython firmware.",
            subtitleStyle);

        // 3. Three Glassmorphic Capability Cards
        float cardGap = 14f;
        float cardsTotalW = deckW - 56f;
        float cardW = (cardsTotalW - (cardGap * 2f)) / 3f;
        float cardY = deckRect.y + 174f;
        float cardH = 128f;

        DrawFeatureCard(
            new Rect(deckRect.x + 28f, cardY, cardW, cardH),
            "01 // POCKET DIMENSION [B]",
            "Summon 30+ CAD electronics & UAV robotics modules into zero-gravity mid-air staging slots with zero table clutter.",
            ForgeBayTheme.AccentCyan);

        DrawFeatureCard(
            new Rect(deckRect.x + 28f + cardW + cardGap, cardY, cardW, cardH),
            "02 // DUPONT WIRING [W]",
            "Click-locked datasheet pinouts with 5V/GND short-circuit fuse protection, bus telemetry, and 3D catenary cables.",
            ForgeBayTheme.AccentEmerald);

        DrawFeatureCard(
            new Rect(deckRect.x + 28f + (cardW + cardGap) * 2f, cardY, cardW, cardH),
            "03 // LIVE C++ / PY IDE [I]",
            "Strict syntax-validated Arduino C++ & MicroPython compiler driving closed-loop servos, steppers & ultrasonic sensors.",
            ForgeBayTheme.AccentAmber);

        // 4. Primary Hero CTA Buttons
        float ctaY = deckRect.y + 324f;
        float primaryW = 270f;
        float secondaryW = 210f;
        float totalCtaW = primaryW + secondaryW + 16f;
        float ctaStartX = deckRect.x + (deckW - totalCtaW) * 0.5f;

        if (ForgeBayTheme.DrawPillButton(new Rect(ctaStartX, ctaY, primaryW, 44f), "▶   ENTER INTERACTIVE LAB", true, ForgeBayTheme.AccentEmerald))
        {
            isDismissing = true;
        }

        if (ForgeBayTheme.DrawPillButton(new Rect(ctaStartX + primaryW + 16f, ctaY, secondaryW, 44f), "⌨   OPEN FIRMWARE IDE", false, ForgeBayTheme.AccentCyan))
        {
            isDismissing = true;
            // Trigger IDE open immediately
            LiveCodeInterpreter ide = GetComponent<LiveCodeInterpreter>();
            if (ide != null) ide.SendMessage("OnGUI", SendMessageOptions.DontRequireReceiver);
        }

        // Subtle Footer Hint
        GUI.Label(new Rect(deckRect.x, deckRect.yMax - 28f, deckW, 20f),
            "Right-Click Drag: Orbit 3D Camera   •   Scroll: Zoom   •   [F]: Inspect Part   •   [M]: Toggle Mission Brief",
            ForgeBayTheme.LabelStyle(11, ForgeBayTheme.TextMuted, false, TextAnchor.MiddleCenter));

        GUI.color = prevColor;
    }

    private void DrawFeatureCard(Rect rect, string heading, string body, Color accent)
    {
        ForgeBayTheme.DrawCard(rect,
            new Color(0.065f, 0.092f, 0.132f, 0.96f),
            ForgeBayTheme.BorderSubtle,
            accent);

        GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 22f), heading, cardTitleStyle);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 34f, rect.width - 24f, rect.height - 42f), body, cardBodyStyle);
    }
}
