using UnityEngine;
using UnityEngine.Rendering;

public class HolographicPartLabel : MonoBehaviour
{
    private Transform hoveredRoot = null;
    private string displayTitle = "";
    private string displayCategory = "";
    private Vector3 worldAnchorTop = Vector3.zero;
    private Vector3 worldSurfacePoint = Vector3.zero;

    private LineRenderer leaderLine;
    private GUIStyle holoBoxStyle;
    private GUIStyle titleStyle;
    private GUIStyle subStyle;
    private Texture2D holoBgTex;
    private Texture2D holoBorderTex;

    private void Start()
    {
        CreateLeaderLine();
        CreateHoloTextures();
    }

    private void CreateLeaderLine()
    {
        GameObject lineObj = new GameObject("Holo_Leader_Line");
        lineObj.transform.SetParent(transform);

        leaderLine = lineObj.AddComponent<LineRenderer>();
        bool isURP = GraphicsSettings.defaultRenderPipeline != null;
        Shader shader = isURP
            ? (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"))
            : Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.color = new Color(0.10f, 0.95f, 0.48f, 0.85f);
        leaderLine.material = mat;
        leaderLine.startColor = new Color(0.10f, 0.95f, 0.48f, 0.90f);
        leaderLine.endColor = new Color(0.10f, 0.95f, 0.48f, 0.25f);
        leaderLine.startWidth = 0.0025f;
        leaderLine.endWidth = 0.0012f;
        leaderLine.positionCount = 2;
        leaderLine.useWorldSpace = true;
        leaderLine.enabled = false;
    }

    private void CreateHoloTextures()
    {
        holoBgTex = new Texture2D(1, 1);
        holoBgTex.SetPixel(0, 0, new Color(0.03f, 0.22f, 0.12f, 0.85f));
        holoBgTex.Apply();

        holoBorderTex = new Texture2D(1, 1);
        holoBorderTex.SetPixel(0, 0, new Color(0.12f, 0.98f, 0.50f, 0.95f));
        holoBorderTex.Apply();
    }

    private void InitStyles()
    {
        if (holoBoxStyle != null) return;

        holoBoxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = holoBgTex }
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.35f, 1.0f, 0.62f, 1.0f) }
        };

        subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.75f, 0.98f, 0.85f, 0.90f) }
        };
    }

    private void Update()
    {
        if (Camera.main == null)
        {
            hoveredRoot = null;
            leaderLine.enabled = false;
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 25f))
        {
            Transform target = ResolveInteractiveRoot(hit.transform);
            if (target != null)
            {
                hoveredRoot = target;
                displayTitle = FormatCleanPartName(target.name);
                displayCategory = ResolveCategoryTag(target);

                Bounds b = GetCombinedBounds(target);
                worldSurfacePoint = new Vector3(b.center.x, b.max.y, b.center.z);
                float bob = Mathf.Sin(Time.time * 4.0f) * 0.004f;
                worldAnchorTop = worldSurfacePoint + Vector3.up * (0.065f + bob);

                leaderLine.enabled = true;
                leaderLine.SetPosition(0, worldSurfacePoint);
                leaderLine.SetPosition(1, worldAnchorTop);
                return;
            }
        }

        hoveredRoot = null;
        leaderLine.enabled = false;
    }

    private Transform ResolveInteractiveRoot(Transform hitTransform)
    {
        if (hitTransform == null) return null;

        string lower = hitTransform.name.ToLower();
        if (lower.Contains("floor") || lower.Contains("wall") || lower.Contains("workbench") ||
            lower.Contains("shelf") || lower.Contains("upright") || lower.Contains("pad") ||
            lower.Contains("ceiling") || lower.Contains("girder") || lower.Contains("safety") ||
            lower.Contains("socket"))
        {
            return null;
        }

        Transform current = hitTransform.name.StartsWith("Pin_") && hitTransform.parent != null
            ? hitTransform.parent
            : hitTransform;

        // Climb up if inside Real_Electronics_Lab or Real_Robotics_Lab to get the named prefab root
        Transform p = current;
        while (p != null && p.parent != null)
        {
            string parentName = p.parent.name;
            if (parentName == "Real_Electronics_Lab" || parentName == "Real_Robotics_Lab")
            {
                return p;
            }
            p = p.parent;
        }

        // If OBJ sub-mesh is named "default" or "PartBody", climb to its parent
        while (current.parent != null &&
               (current.name.Equals("default", System.StringComparison.OrdinalIgnoreCase) ||
                current.name.Equals("PartBody", System.StringComparison.OrdinalIgnoreCase) ||
                current.name.StartsWith("mesh")))
        {
            current = current.parent;
        }

        if (current.CompareTag("ElectronicPart") || current.CompareTag("RoboticsPart") || current.CompareTag("MechanicalPart"))
        {
            return current;
        }

        // Support V8 Engine sub-assemblies inside Bay0_Mechanical
        Transform check = hitTransform;
        while (check != null)
        {
            if (check.name.Contains("V8_Engine"))
                return current;
            check = check.parent;
        }

        return null;
    }

    private string ResolveCategoryTag(Transform t)
    {
        if (IsInsideBay(t, "Bay1_Electronics")) return "ELECTRONICS MODULE • DRAG / [W] WIRE";
        if (IsInsideBay(t, "Bay2_Robotics")) return "ROBOTICS / UAV MODULE • DRAG / [W] WIRE";
        return "MECHANICAL SUB-ASSEMBLY • DRAG / [E] EXPLODE";
    }

    private bool IsInsideBay(Transform t, string bayName)
    {
        Transform p = t;
        while (p != null)
        {
            if (p.name == bayName) return true;
            p = p.parent;
        }
        return false;
    }

    private string FormatCleanPartName(string raw)
    {
        string clean = raw
            .Replace("Electrical_", "")
            .Replace("Rob_", "")
            .Replace("UAV_", "")
            .Replace("_(n16r8)", "")
            .Replace("_public", "")
            .Replace("_", " ")
            .Trim();

        return clean.ToUpperInvariant();
    }

    private Bounds GetCombinedBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one * 0.05f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i].gameObject.name.StartsWith("Pin_")) continue;
            b.Encapsulate(renderers[i].bounds);
        }
        return b;
    }

    private void OnGUI()
    {
        GUI.depth = -100;
        if (hoveredRoot == null || Camera.main == null) return;

        InitStyles();

        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldAnchorTop);
        if (screenPos.z <= 0f) return;

        float guiX = screenPos.x;
        float guiY = Screen.height - screenPos.y;

        GUIContent titleContent = new GUIContent(displayTitle);
        GUIContent subContent = new GUIContent(displayCategory);

        float titleW = titleStyle.CalcSize(titleContent).x + 28f;
        float subW = subStyle.CalcSize(subContent).x + 24f;
        float boxW = Mathf.Clamp(Mathf.Max(titleW, subW), 185f, 380f);
        float boxH = 44f;

        Rect panelRect = new Rect(guiX - (boxW * 0.5f), guiY - boxH - 6f, boxW, boxH);
        panelRect.x = Mathf.Clamp(panelRect.x, 10f, Screen.width - boxW - 10f);
        panelRect.y = Mathf.Clamp(panelRect.y, 10f, Screen.height - boxH - 55f);

        GUI.Box(panelRect, GUIContent.none, holoBoxStyle);
        GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, panelRect.width, 2.5f), holoBorderTex);
        GUI.DrawTexture(new Rect(panelRect.x, panelRect.yMax - 2f, panelRect.width, 2f), holoBorderTex);
        GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, 2f, 10f), holoBorderTex);
        GUI.DrawTexture(new Rect(panelRect.xMax - 2f, panelRect.y, 2f, 10f), holoBorderTex);

        GUI.Label(new Rect(panelRect.x + 6f, panelRect.y + 4f, panelRect.width - 12f, 20f), titleContent, titleStyle);
        GUI.Label(new Rect(panelRect.x + 6f, panelRect.y + 22f, panelRect.width - 12f, 16f), subContent, subStyle);
    }
}
