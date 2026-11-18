using System.Collections.Generic;
using UnityEngine;

public class PocketDimensionBay : MonoBehaviour
{
    public class PocketPart
    {
        public string rawName;
        public string displayName;
        public string baySource; // "Electronics" or "Robotics"
        public Transform root;
        public Transform movable;
        public Renderer rend;
        public bool isSummoned;
        public int slotIndex;
        public GameObject holoRing;
        public float bobPhase;
    }

    private readonly List<PocketPart> catalog = new List<PocketPart>();
    private readonly List<PocketPart> activeSlots = new List<PocketPart>();

    private bool catalogOpen = false;
    private int filterTab = 0; // 0 = All, 1 = Electronics, 2 = Robotics
    private Vector2 catalogScroll = Vector2.zero;

    private Material holoRingMat;

    private void Start()
    {
        // Disable legacy tabletop collision/grid script so PocketDimensionBay owns component staging cleanly
        LabLayoutAndCollisionGuard oldGuard = GetComponent<LabLayoutAndCollisionGuard>();
        if (oldGuard != null) oldGuard.enabled = false;

        holoRingMat = new Material(Shader.Find("Sprites/Default"))
        {
            color = new Color(0.18f, 0.85f, 0.98f, 0.45f)
        };

        RegisterPocketParts("Bay1_Electronics", "ElectronicPart", "Electronics");
        RegisterPocketParts("Bay2_Robotics", "RoboticsPart", "Robotics");

        // Stow all parts first, then summon the 4 Hero Showcase modules for an immediate live demo!
        foreach (PocketPart p in catalog)
            SendToPocketDimension(p);

        string[] heroKeys = { "arduino_uno", "mg996r", "nema_17", "hc-sr04" };
        foreach (string key in heroKeys)
        {
            PocketPart match = catalog.Find(p => p.rawName.ToLower().Contains(key));
            if (match != null) SummonPart(match);
        }

        // Ensure hero parts align with Bay1_Electronics camera after DesktopLabController positions camera
        Invoke(nameof(RepositionActiveSlots), 0.12f);
    }

    private Transform GetPartRoot(Transform t)
    {
        Transform p = t;
        while (p != null && p.parent != null)
        {
            string pName = p.parent.name;
            if (pName == "Real_Electronics_Lab" || pName == "Real_Robotics_Lab")
                return p;
            p = p.parent;
        }
        return t;
    }

    private void RegisterPocketParts(string bayName, string tag, string label)
    {
        GameObject bay = GameObject.Find(bayName);
        if (bay == null) return;

        Transform oldRack = bay.transform.Find("Real_Electronics_Lab/Component_Rack_Frame");
        if (oldRack != null) oldRack.gameObject.SetActive(false);

        HashSet<Transform> seen = new HashSet<Transform>();
        foreach (Transform t in bay.GetComponentsInChildren<Transform>(true))
        {
            if (!t.CompareTag(tag)) continue;
            Transform root = GetPartRoot(t);
            if (root.name.ToLower().Contains("robotic_arm") || root.name.ToLower().Contains("robotic arm") || root.name.ToLower().Contains("roboticarm"))
            {
                root.gameObject.SetActive(false);
                continue;
            }
            if (seen.Contains(root)) continue;
            seen.Add(root);

            MeshRenderer mr = root.GetComponentInChildren<MeshRenderer>();
            if (mr == null) continue;

            // Create subtle glowing CAD staging ring under each summoned part
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Holo_Staging_Pad";
            ring.transform.SetParent(root, false);
            ring.transform.localScale = new Vector3(0.14f, 0.001f, 0.14f);
            if (ring.TryGetComponent<Collider>(out Collider col)) Destroy(col);
            if (ring.TryGetComponent<Renderer>(out Renderer r)) r.sharedMaterial = holoRingMat;
            Destroy(ring);
            ring = null;

            catalog.Add(new PocketPart
            {
                rawName = root.name,
                displayName = root.name
                    .Replace("Electrical_", "")
                    .Replace("Rob_", "")
                    .Replace("_", " "),
                baySource = label,
                root = root,
                movable = mr.transform,
                rend = mr,
                isSummoned = false,
                slotIndex = -1,
                holoRing = ring,
                bobPhase = Random.Range(0f, Mathf.PI * 2f)
            });
        }

        catalog.Sort((a, b) => string.Compare(a.displayName, b.displayName, System.StringComparison.OrdinalIgnoreCase));
    }

    public void SummonPart(PocketPart p)
    {
        if (p == null || p.isSummoned) return;

        p.isSummoned = true;
        p.slotIndex = activeSlots.Count;
        activeSlots.Add(p);

        p.movable.localPosition = Vector3.zero;
        

        RepositionActiveSlots();
    }

    public void StowPart(PocketPart p)
    {
        if (p == null || !p.isSummoned) return;

        p.isSummoned = false;
        p.slotIndex = -1;
        activeSlots.Remove(p);

        SendToPocketDimension(p);
        RepositionActiveSlots();
    }

    public void StowAllParts()
    {
        foreach (PocketPart p in activeSlots)
        {
            p.isSummoned = false;
            p.slotIndex = -1;
            SendToPocketDimension(p);
        }
        activeSlots.Clear();
    }

    private void SendToPocketDimension(PocketPart p)
    {
        if (p.holoRing != null) p.holoRing.SetActive(false);
        // Isolated pocket dimension well below the lab floor so parts never collide or block view
        p.root.position = new Vector3(0f, -50f, 0f);
    }

    private void RepositionActiveSlots()
    {
        if (Camera.main == null || activeSlots.Count == 0) return;

        // Arrange summoned parts in clean, non-overlapping mid-air CAD arcs/rows right in front of the user
        int maxCols = 4;
        float spacingX = 0.28f;
        float spacingY = 0.20f;

        Vector3 camPos = Camera.main.transform.position;
        Vector3 forward = Camera.main.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 stageCenter = camPos + (forward * 0.95f) + (Vector3.down * 0.06f);

        int rows = Mathf.CeilToInt(activeSlots.Count / (float)maxCols);

        for (int i = 0; i < activeSlots.Count; i++)
        {
            PocketPart p = activeSlots[i];
            int r = i / maxCols;
            int c = i % maxCols;
            int colsInRow = Mathf.Min(maxCols, activeSlots.Count - (r * maxCols));

            float offsetX = (c - (colsInRow - 1) * 0.5f) * spacingX;
            float offsetY = ((rows - 1) * 0.5f - r) * spacingY;

            Vector3 slotWorld = stageCenter + (right * offsetX) + (Vector3.up * offsetY);

            p.movable.localPosition = Vector3.zero;
            Physics.SyncTransforms();

            Bounds b = p.rend.bounds;
            p.root.position += (slotWorld - b.center);

            if (p.holoRing != null)
            {
                p.holoRing.transform.position = new Vector3(slotWorld.x, p.rend.bounds.min.y - 0.006f, slotWorld.z);
            }
        }
    }

    private void Update()
    {
        if (GUIUtility.keyboardControl == 0 && Input.GetKeyDown(KeyCode.B))
        {
            catalogOpen = !catalogOpen;
            if (catalogOpen)
            {
                LiveCodeInterpreter ide = GetComponent<LiveCodeInterpreter>();
                if (ide != null) ide.DockOrCloseFromCatalog();
            }
        }

        // Hide summoned electronics/robotics when inspecting Bay0_Mechanical (X < 4.5), restore in Electronics/Robotics bays
        bool inMechanicalBay = Camera.main != null && Camera.main.transform.position.x < 4.5f;
        if (inMechanicalBay)
        {
            for (int i = 0; i < activeSlots.Count; i++)
                activeSlots[i].root.position = new Vector3(0f, -50f, 0f);
            return;
        }
        else if (activeSlots.Count > 0 && activeSlots[0].root.position.y < -10f)
        {
            RepositionActiveSlots();
        }

        // Subtle mid-air anti-gravity levitation hover on summoned parts
        float t = Time.time;
        for (int i = 0; i < activeSlots.Count; i++)
        {
            PocketPart p = activeSlots[i];
            float bob = Mathf.Sin(t * 2.1f + p.bobPhase) * 0.0035f;
            p.movable.localPosition = new Vector3(0f, bob, 0f);
        }
    }

    public void CollapseCatalog() { catalogOpen = false; }

    private void OnGUI()
    {
        GUI.depth = 10;

        if (!catalogOpen)
        {
            if (ForgeBayTheme.DrawPillButton(new Rect(392f, 14f, 232f, 30f), $"  [B] SUMMON PARTS ({activeSlots.Count} ACTIVE)", false, ForgeBayTheme.AccentCyan))
            {
                catalogOpen = true;
                LiveCodeInterpreter ide = GetComponent<LiveCodeInterpreter>();
                if (ide != null) ide.DockOrCloseFromCatalog();
            }
            return;
        }

        float panelW = 286f;
        float panelH = Mathf.Min(430f, Screen.height - 245f);
        Rect panelRect = new Rect(14f, 54f, panelW, panelH);

        ForgeBayTheme.BlockSceneScrollIfHovered(panelRect);
        ForgeBayTheme.DrawCard(panelRect, ForgeBayTheme.BgDeep, ForgeBayTheme.BorderSubtle, ForgeBayTheme.AccentCyan);
        GUI.Label(new Rect(panelRect.x + 12f, panelRect.y + 6f, 210f, 22f),
            "POCKET DIMENSION CATALOG [B]", ForgeBayTheme.LabelStyle(11, ForgeBayTheme.AccentCyan, true));

        if (ForgeBayTheme.DrawPillButton(new Rect(panelRect.xMax - 32f, panelRect.y + 5f, 24f, 22f), "✕", false, ForgeBayTheme.AccentCrimson))
            catalogOpen = false;

        // Filter Tabs: ALL | ELEC | ROBO
        float tabW = (panelW - 32f) / 3f;
        if (ForgeBayTheme.DrawPillButton(new Rect(panelRect.x + 10f, panelRect.y + 32f, tabW, 24f), "All", filterTab == 0, ForgeBayTheme.AccentCyan))
            filterTab = 0;
        if (ForgeBayTheme.DrawPillButton(new Rect(panelRect.x + 15f + tabW, panelRect.y + 32f, tabW, 24f), "Electronics", filterTab == 1, ForgeBayTheme.AccentEmerald))
            filterTab = 1;
        if (ForgeBayTheme.DrawPillButton(new Rect(panelRect.x + 20f + tabW * 2f, panelRect.y + 32f, tabW, 24f), "Robotics", filterTab == 2, ForgeBayTheme.AccentAmber))
            filterTab = 2;

        // Scrollable Component Summon List
        List<PocketPart> filtered = catalog.FindAll(p =>
            filterTab == 0 ||
            (filterTab == 1 && p.baySource == "Electronics") ||
            (filterTab == 2 && p.baySource == "Robotics"));

        float listY = panelRect.y + 62f;
        float listH = panelH - 102f;
        float rowH = 28f;
        float contentH = Mathf.Max(listH, filtered.Count * (rowH + 4f));

        catalogScroll = GUI.BeginScrollView(
            new Rect(panelRect.x + 8f, listY, panelW - 16f, listH),
            catalogScroll,
            new Rect(0f, 0f, panelW - 34f, contentH)
        );

        for (int i = 0; i < filtered.Count; i++)
        {
            PocketPart p = filtered[i];
            float y = i * (rowH + 4f);
            string badge = p.isSummoned ? "✓  " : "+  ";
            if (ForgeBayTheme.DrawPillButton(
                new Rect(2f, y, panelW - 38f, rowH),
                badge + p.displayName,
                p.isSummoned,
                p.isSummoned ? ForgeBayTheme.AccentEmerald : ForgeBayTheme.AccentCyan))
            {
                if (p.isSummoned) StowPart(p);
                else SummonPart(p);
            }
        }

        GUI.EndScrollView();

        // Bottom Footer Actions
        float footY = panelRect.yMax - 32f;
        if (ForgeBayTheme.DrawPillButton(new Rect(panelRect.x + 10f, footY, (panelW - 26f) * 0.5f, 24f), "Re-Center Stage", false, ForgeBayTheme.AccentCyan))
            RepositionActiveSlots();
        if (ForgeBayTheme.DrawPillButton(new Rect(panelRect.x + 16f + (panelW - 26f) * 0.5f, footY, (panelW - 26f) * 0.5f, 24f), "Stow All", false, ForgeBayTheme.AccentCrimson))
            StowAllParts();
    }
}
