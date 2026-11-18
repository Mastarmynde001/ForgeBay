using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LandingWorkbenchArranger
{
    [MenuItem("Tools/ForgeBay/Stage Clean Landing Workbench")]
    public static void StageCleanLandingWorkbench()
    {
        // 1. Delete any "Robotic Arm" object anywhere in the scene
        List<GameObject> toDestroy = new List<GameObject>();
        foreach (Transform t in Object.FindObjectsByType<Transform>())
        {
            string low = t.name.ToLower();
            if (low.Contains("robotic_arm") || low.Contains("robotic arm") || low.Contains("roboticarm"))
            {
                toDestroy.Add(t.gameObject);
            }
            else if (t.name == "Component_Rack_Frame")
            {
                toDestroy.Add(t.gameObject);
            }
            else if (t.name == "Sphere" || t.name.StartsWith("Sphere ("))
            {
                toDestroy.Add(t.gameObject);
            }
        }

        foreach (GameObject go in toDestroy)
        {
            if (go != null)
                Undo.DestroyObjectImmediate(go);
        }

        // 2. Arrange Bay1_Electronics flush on its Workbench in neat CAD rows
        ArrangeBayOnWorkbench("Bay1_Electronics", "Real_Electronics_Lab", "ElectronicPart", 6, 0.22f, 0.19f);

        // 3. Arrange Bay2_Robotics flush on its Workbench in neat CAD rows
        ArrangeBayOnWorkbench("Bay2_Robotics", "Real_Robotics_Lab", "RoboticsPart", 5, 0.28f, 0.25f);

                // 4. Boost Studio Key Lighting so PCB traces, gold pins & brushed aluminum pop
        RenderSettings.ambientLight = new Color(0.32f, 0.36f, 0.42f);
        GameObject keyLight = GameObject.Find("ForgeBay_Studio_KeyLight");
        if (keyLight == null)
        {
            keyLight = new GameObject("ForgeBay_Studio_KeyLight");
            Light l = keyLight.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.45f;
            l.color = new Color(0.96f, 0.98f, 1.0f);
            keyLight.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }

        // 5. Mark scene dirty so Ctrl+S saves this clean landing layout permanently
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Staged clean landing workbench: removed Robotic Arm, removed obsolete racks/spheres, and seated all components flush on workbenches!");
    }

    private static void ArrangeBayOnWorkbench(string bayName, string labContainerName, string tag, int cols, float spacingX, float spacingZ)
    {
        GameObject bay = GameObject.Find(bayName);
        if (bay == null) return;

        // Find the main Workbench tabletop bounds
        Renderer benchRend = null;
        foreach (Renderer r in bay.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.name.ToLower().Contains("workbench"))
            {
                benchRend = r;
                break;
            }
        }

        if (benchRend == null) return;

        float surfaceY = benchRend.bounds.max.y;
        Vector3 benchCenter = benchRend.bounds.center;

        // Collect unique component roots in this bay
        List<Transform> partRoots = new List<Transform>();
        HashSet<Transform> seen = new HashSet<Transform>();

        foreach (Transform t in bay.GetComponentsInChildren<Transform>(true))
        {
            if (!t.CompareTag(tag)) continue;

            Transform root = t;
            while (root.parent != null && root.parent.name != labContainerName && root.parent.name != bayName)
                root = root.parent;

            if (seen.Contains(root)) continue;
            seen.Add(root);
            partRoots.Add(root);
        }

        partRoots.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

        int rows = Mathf.CeilToInt(partRoots.Count / (float)cols);
        float totalW = (cols - 1) * spacingX;
        float totalD = (rows - 1) * spacingZ;

        for (int i = 0; i < partRoots.Count; i++)
        {
            Transform root = partRoots[i];
            Undo.RecordObject(root, "Arrange Landing Component");

            // Reset child mesh local offset if any
            MeshRenderer mr = root.GetComponentInChildren<MeshRenderer>();
            if (mr != null && mr.transform != root)
            {
                Undo.RecordObject(mr.transform, "Reset Child Mesh Offset");
                mr.transform.localPosition = Vector3.zero;
            }

            Physics.SyncTransforms();

            int r = i / cols;
            int c = i % cols;
            int colsInRow = Mathf.Min(cols, partRoots.Count - (r * cols));
            float rowWidth = (colsInRow - 1) * spacingX;

            float targetX = benchCenter.x - (rowWidth * 0.5f) + (c * spacingX);
            float targetZ = benchCenter.z - (totalD * 0.5f) + (r * spacingZ);

            if (mr != null)
            {
                Bounds b = mr.bounds;
                float liftY = surfaceY - b.min.y + 0.0005f;
                root.position += new Vector3(targetX - b.center.x, liftY, targetZ - b.center.z);
            }
            else
            {
                root.position = new Vector3(targetX, surfaceY + 0.01f, targetZ);
            }
        }
    }
}
