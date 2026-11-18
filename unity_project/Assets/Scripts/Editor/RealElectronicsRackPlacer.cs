using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class RealElectronicsRackPlacer
{
    [MenuItem("Tools/Place Real CAD Electronics on Rack")]
    public static void PlaceRealCadComponents()
    {
        GameObject bay = GameObject.Find("Bay1_Electronics");
        if (bay == null)
        {
            Debug.LogError("Bay1_Electronics not found in scene!");
            return;
        }

        string folder = "Assets/Models/W2_Electronics";
        foreach (string oldName in new[] { "Electronics_Station", "Electronics_Organized_Station", "Real_Electronics_Lab" })
        {
            Transform old = bay.transform.Find(oldName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        GameObject root = new GameObject("Real_Electronics_Lab");
        root.transform.SetParent(bay.transform);
        root.transform.localPosition = new Vector3(-0.15f, 1.0f, 1.5f);

        Material matRackFrame = GetMat("Mat_Rack_Anodized", new Color(0.11f, 0.13f, 0.16f), 0.85f, 0.72f);
        Material matShelfMat = GetMat("Mat_Shelf_ESD", new Color(0.17f, 0.23f, 0.27f), 0.15f, 0.42f);
        Material matBreadboard = GetMat("Mat_CAD_Breadboard", new Color(0.86f, 0.85f, 0.80f), 0.08f, 0.35f);
        Material matArduinoTeal = GetMat("Mat_CAD_ArduinoTeal", new Color(0.05f, 0.42f, 0.50f), 0.30f, 0.62f);
        Material matPcbGreen = GetMat("Mat_CAD_PcbGreen", new Color(0.07f, 0.38f, 0.18f), 0.30f, 0.62f);
        Material matDarkAnodized = GetMat("Mat_CAD_DarkMetal", new Color(0.16f, 0.18f, 0.21f), 0.78f, 0.74f);
        Material matBrushedSteel = GetMat("Mat_CAD_BrushedSteel", new Color(0.74f, 0.77f, 0.81f), 0.90f, 0.80f);
        Material matBlueBoard = GetMat("Mat_CAD_RoyalBlue", new Color(0.08f, 0.28f, 0.68f), 0.30f, 0.62f);

        // Wide 2-Tier Rear Organizer Rack (1.36m wide so all 18 parts fit with clear spacing)
        GameObject rack = new GameObject("Component_Rack_Frame");
        rack.transform.SetParent(root.transform);
        rack.transform.localPosition = new Vector3(0f, 0f, 0.36f);
        CreateBox(rack.transform, "Upright_L", new Vector3(-0.66f, 0.19f, 0f), new Vector3(0.024f, 0.38f, 0.024f), matRackFrame);
        CreateBox(rack.transform, "Upright_R", new Vector3(0.66f, 0.19f, 0f), new Vector3(0.024f, 0.38f, 0.024f), matRackFrame);
        CreateBox(rack.transform, "Shelf_Tier1", new Vector3(0f, 0.15f, -0.04f), new Vector3(1.34f, 0.014f, 0.24f), matShelfMat);
        CreateBox(rack.transform, "Shelf_Tier2", new Vector3(0f, 0.33f, -0.04f), new Vector3(1.34f, 0.014f, 0.24f), matShelfMat);

        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
        List<string> tier1 = new List<string>();
        List<string> tier2 = new List<string>();
        string breadboardPath = null;

        foreach (string g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string low = Path.GetFileName(p).ToLower();
            if (low.Contains("blackboard"))
                breadboardPath = p;
            else if (low.Contains("arduino") || low.Contains("esp32") || low.Contains("pico") || low.Contains("pca9685") || low.Contains("l293d") || low.Contains("charger"))
                tier1.Add(p);
            else
                tier2.Add(p);
        }

        int placed = 0;
        if (!string.IsNullOrEmpty(breadboardPath))
        {
            SpawnPart(breadboardPath, root.transform, new Vector3(0f, 0.002f, -0.14f), 0.34f, matBreadboard, ref placed);
        }

        Material[] t1Mats = { matArduinoTeal, matDarkAnodized, matPcbGreen, matBlueBoard };
        DistributeShelf(tier1, root.transform, 0.158f, 0.31f, 1.18f, 0.12f, t1Mats, ref placed);

        Material[] t2Mats = { matBrushedSteel, matDarkAnodized, matPcbGreen, matArduinoTeal };
        DistributeShelf(tier2, root.transform, 0.338f, 0.31f, 1.18f, 0.105f, t2Mats, ref placed);

        Transform socket = bay.transform.Find("Socket_Electronics");
        if (socket != null) socket.localPosition = new Vector3(0.76f, 1.15f, 1.45f);

        Debug.Log($"Placed and rigged {placed} real CAD electronics components onto Bay1_Electronics!");
    }

    private static void DistributeShelf(List<string> paths, Transform parent, float y, float z, float span, float size, Material[] mats, ref int placed)
    {
        if (paths.Count == 0) return;
        float step = paths.Count == 1 ? 0f : span / (paths.Count - 1);
        float startX = paths.Count == 1 ? 0f : -span * 0.5f;
        for (int i = 0; i < paths.Count; i++)
        {
            SpawnPart(paths[i], parent, new Vector3(startX + i * step, y, z), size, mats[i % mats.Length], ref placed);
        }
    }

    private static void SpawnPart(string assetPath, Transform parent, Vector3 localPos, float targetSize, Material mat, ref int placed)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer != null)
        {
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle = 45f;
            importer.SaveAndReimport();
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) return;

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = Path.GetFileNameWithoutExtension(assetPath).Replace("Electrical_", "");
        inst.transform.SetParent(parent);
        inst.transform.localPosition = localPos;
        inst.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        Bounds b = GetBounds(inst);
        float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (maxDim > 0.0001f) inst.transform.localScale *= (targetSize / maxDim);

        Bounds scaledB = GetBounds(inst);
        float targetWorldY = parent.TransformPoint(localPos).y;
        inst.transform.position += new Vector3(0f, targetWorldY - scaledB.min.y, 0f);

        foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>())
        {
            GameObject part = mf.gameObject;
            part.tag = "ElectronicPart";
            if (part.TryGetComponent<MeshRenderer>(out MeshRenderer mr)) mr.sharedMaterial = mat;

            Collider c = part.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            BoxCollider mc = part.AddComponent<BoxCollider>();
            

            Rigidbody rb = part.GetComponent<Rigidbody>();
            if (rb == null) rb = part.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        placed++;
    }

    private static void CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.localPosition = pos;
        obj.transform.localScale = scale;
        if (obj.TryGetComponent<MeshRenderer>(out MeshRenderer mr)) mr.sharedMaterial = mat;
    }

    private static Bounds GetBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    private static Material GetMat(string name, Color c, float metallic, float smoothness)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        string path = $"Assets/Materials/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            bool isURP = GraphicsSettings.defaultRenderPipeline != null;
            Shader s = isURP ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) : Shader.Find("Standard");
            m = new Material(s) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
        }
        return m;
    }
}
