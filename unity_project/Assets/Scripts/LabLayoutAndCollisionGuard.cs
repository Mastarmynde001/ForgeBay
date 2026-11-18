using System.Collections.Generic;
using UnityEngine;

public class LabLayoutAndCollisionGuard : MonoBehaviour
{
    private class LabPartBody
    {
        public Transform root;
        public Transform movable;
        public Renderer rend;
        public Vector3 lastValidRootPos;
        public Vector3 lastValidMovablePos;
    }

    private readonly List<LabPartBody> electronicsParts = new List<LabPartBody>();
    private readonly List<LabPartBody> roboticsParts = new List<LabPartBody>();

    private float electronicsSurfaceY = 0.85f;
    private float roboticsSurfaceY = 0.85f;

    private void Start()
    {
        electronicsSurfaceY = DetectSurfaceTopY("Bay1_Electronics", 0.85f);
        roboticsSurfaceY = DetectSurfaceTopY("Bay2_Robotics", 0.85f);

        CollectParts("Bay1_Electronics", "ElectronicPart", electronicsParts);
        CollectParts("Bay2_Robotics", "RoboticsPart", roboticsParts);

        ArrangeAllCleanly();
    }

    private void Update()
    {
        // Press [L] to neatly re-arrange all components into aligned CAD rows
        if (GUIUtility.keyboardControl == 0 && Input.GetKeyDown(KeyCode.L))
        {
            ArrangeAllCleanly();
        }
    }

    private void LateUpdate()
    {
        EnforceSolidCollisions(electronicsParts, electronicsSurfaceY);
        EnforceSolidCollisions(roboticsParts, roboticsSurfaceY);
    }

    public void ArrangeAllCleanly()
    {
        ArrangeBayGrid("Bay1_Electronics", electronicsParts, electronicsSurfaceY, 6, 0.23f, 0.21f);
        ArrangeBayGrid("Bay2_Robotics", roboticsParts, roboticsSurfaceY, 5, 0.30f, 0.28f);

        DesktopLabController dlc = FindAnyObjectByType<DesktopLabController>();
        if (dlc != null) dlc.SyncArrangedHomes();
    }

    private float DetectSurfaceTopY(string bayName, float fallbackY)
    {
        GameObject bay = GameObject.Find(bayName);
        if (bay == null) return fallbackY;

        float bestY = fallbackY;
        bool found = false;

        foreach (Renderer r in bay.GetComponentsInChildren<Renderer>(true))
        {
            string low = r.gameObject.name.ToLower();
            if (low.Contains("workbench") || low.Contains("table") || low.Contains("desk"))
            {
                if (r.bounds.max.y > 0.4f && r.bounds.max.y < 1.35f)
                {
                    if (!found || r.bounds.max.y > bestY)
                    {
                        bestY = r.bounds.max.y;
                        found = true;
                    }
                }
            }
        }
        return bestY;
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

    private void CollectParts(string bayName, string tag, List<LabPartBody> list)
    {
        list.Clear();
        GameObject bay = GameObject.Find(bayName);
        if (bay == null) return;

        Transform oldRack = bay.transform.Find("Real_Electronics_Lab/Component_Rack_Frame");
        if (oldRack != null) oldRack.gameObject.SetActive(false);

        HashSet<Transform> seen = new HashSet<Transform>();
        foreach (Transform t in bay.GetComponentsInChildren<Transform>(true))
        {
            if (!t.CompareTag(tag)) continue;
            Transform root = GetPartRoot(t);
            if (seen.Contains(root)) continue;
            seen.Add(root);

            MeshRenderer mr = root.GetComponentInChildren<MeshRenderer>();
            if (mr == null) continue;

            list.Add(new LabPartBody
            {
                root = root,
                movable = mr.transform,
                rend = mr,
                lastValidRootPos = root.position,
                lastValidMovablePos = mr.transform.position
            });
        }

        list.Sort((a, b) => string.Compare(a.root.name, b.root.name, System.StringComparison.OrdinalIgnoreCase));
    }

    private void ArrangeBayGrid(string bayName, List<LabPartBody> parts, float surfaceY, int cols, float spacingX, float spacingZ)
    {
        GameObject bay = GameObject.Find(bayName);
        if (bay == null || parts.Count == 0) return;

        Vector3 center = bay.transform.position;
        foreach (Renderer r in bay.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.name.ToLower().Contains("workbench"))
            {
                center = r.bounds.center;
                break;
            }
        }

        int rows = Mathf.CeilToInt(parts.Count / (float)cols);
        float totalW = (cols - 1) * spacingX;
        float totalD = (rows - 1) * spacingZ;

        for (int i = 0; i < parts.Count; i++)
        {
            LabPartBody p = parts[i];
            int r = i / cols;
            int c = i % cols;

            float x = center.x - (totalW * 0.5f) + (c * spacingX);
            float z = center.z - (totalD * 0.5f) + (r * spacingZ);

            p.movable.localPosition = Vector3.zero;
            Physics.SyncTransforms();

            Bounds b = p.rend.bounds;
            float liftY = surfaceY - b.min.y + 0.001f;
            p.root.position += new Vector3(x - b.center.x, liftY, z - b.center.z);

            p.lastValidRootPos = p.root.position;
            p.lastValidMovablePos = p.movable.position;
        }
    }

    private void EnforceSolidCollisions(List<LabPartBody> parts, float surfaceY)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            LabPartBody a = parts[i];
            if (a.rend == null) continue;

            // Detect which transform was moved by the drag controller (root vs child mesh)
            bool rootMoved = (a.root.position - a.lastValidRootPos).sqrMagnitude > 0.0000005f;
            bool childMoved = (a.movable.position - a.lastValidMovablePos).sqrMagnitude > 0.0000005f;
            Transform activeTransform = rootMoved ? a.root : a.movable;

            // 1. Solid Workbench Surface Clamp (never sink below surfaceY)
            Bounds ba = a.rend.bounds;
            if (ba.min.y < surfaceY)
            {
                float liftY = surfaceY - ba.min.y + 0.0005f;
                activeTransform.position += new Vector3(0f, liftY, 0f);
                ba = a.rend.bounds;
            }

            // 2. Solid Component-to-Component AABB Separation (never pass through neighbors)
            if (rootMoved || childMoved)
            {
                for (int iter = 0; iter < 2; iter++)
                {
                    for (int j = 0; j < parts.Count; j++)
                    {
                        if (i == j) continue;
                        LabPartBody b = parts[j];
                        if (b.rend == null) continue;

                        Bounds bb = b.rend.bounds;
                        Bounds paddedB = new Bounds(bb.center, bb.size + new Vector3(0.008f, 0.002f, 0.008f));

                        if (!ba.Intersects(paddedB)) continue;

                        float overlapX = Mathf.Min(ba.max.x - paddedB.min.x, paddedB.max.x - ba.min.x);
                        float overlapZ = Mathf.Min(ba.max.z - paddedB.min.z, paddedB.max.z - ba.min.z);

                        if (overlapX > 0f && overlapZ > 0f)
                        {
                            if (overlapX < overlapZ)
                            {
                                float dirX = (ba.center.x >= paddedB.center.x) ? 1f : -1f;
                                activeTransform.position += new Vector3(dirX * overlapX, 0f, 0f);
                            }
                            else
                            {
                                float dirZ = (ba.center.z >= paddedB.center.z) ? 1f : -1f;
                                activeTransform.position += new Vector3(0f, 0f, dirZ * overlapZ);
                            }
                            ba = a.rend.bounds;
                        }
                    }
                }
            }

            a.lastValidRootPos = a.root.position;
            a.lastValidMovablePos = a.movable.position;
        }
    }
}
