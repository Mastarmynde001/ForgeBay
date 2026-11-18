using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class RealRoboticsRackPlacer
{
    [MenuItem("Tools/Place Real CAD Robotics & Drone Parts on Rack")]
    public static void PlaceRoboticsAndDroneModels()
    {
        GameObject bay = GameObject.Find("Bay2_Robotics");
        if (bay == null)
        {
            Debug.LogError("Bay2_Robotics not found in scene!");
            return;
        }

        string modelFolder = "Assets/Models/W3_Robotics";
        foreach (string oldName in new[] { "RoboticArm_6DOF", "Robotics_Station", "Real_Robotics_Lab" })
        {
            Transform old = bay.transform.Find(oldName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        GameObject root = new GameObject("Real_Robotics_Lab");
        root.transform.SetParent(bay.transform);
        root.transform.localPosition = new Vector3(-0.15f, 1.0f, 1.5f);

        Material matRackFrame = GetMat("Mat_Rob_RackAnodized", new Color(0.11f, 0.13f, 0.16f), 0.85f, 0.72f);
        Material matShelfMat = GetMat("Mat_Rob_ShelfMat", new Color(0.16f, 0.21f, 0.26f), 0.18f, 0.45f);
        Material matWorkMat = GetMat("Mat_Rob_AssemblyPad", new Color(0.14f, 0.16f, 0.19f), 0.10f, 0.38f);
        Material matGunmetal = GetMat("Mat_Rob_Gunmetal", new Color(0.22f, 0.25f, 0.29f), 0.88f, 0.78f);
        Material matBrushedAlu = GetMat("Mat_Rob_BrushedAlu", new Color(0.76f, 0.79f, 0.83f), 0.92f, 0.82f);
        Material matCopperCoil = GetMat("Mat_Rob_Copper", new Color(0.84f, 0.50f, 0.20f), 0.92f, 0.80f);
        Material matSafetyOrange = GetMat("Mat_Rob_IndustrialOrange", new Color(0.88f, 0.34f, 0.06f), 0.35f, 0.65f);
        Material matCarbonDark = GetMat("Mat_UAV_Carbon", new Color(0.13f, 0.14f, 0.16f), 0.45f, 0.68f);
        Material matAvionicsTeal = GetMat("Mat_UAV_Avionics", new Color(0.05f, 0.42f, 0.50f), 0.30f, 0.62f);

        GameObject rack = new GameObject("Robotics_Rack_Frame");
        rack.transform.SetParent(root.transform);
        rack.transform.localPosition = new Vector3(0f, 0f, 0.36f);

        CreateBox(rack.transform, "Upright_L", new Vector3(-0.66f, 0.20f, 0f), new Vector3(0.024f, 0.40f, 0.024f), matRackFrame);
        CreateBox(rack.transform, "Upright_R", new Vector3(0.66f, 0.20f, 0f), new Vector3(0.024f, 0.40f, 0.024f), matRackFrame);
        CreateBox(rack.transform, "Shelf_Tier1_ArmActuators", new Vector3(0f, 0.16f, -0.04f), new Vector3(1.34f, 0.014f, 0.24f), matShelfMat);
        CreateBox(rack.transform, "Shelf_Tier2_UAVModules", new Vector3(0f, 0.35f, -0.04f), new Vector3(1.34f, 0.014f, 0.24f), matShelfMat);
        CreateBox(root.transform, "Robotics_Assembly_Pad", new Vector3(0f, 0.003f, -0.14f), new Vector3(0.76f, 0.006f, 0.36f), matWorkMat);

        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { modelFolder });
        string armAssemblyPath = null;
        string gripperPath = null;
        List<string> uavPaths = new List<string>();
        List<string> armParts = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string low = Path.GetFileName(path).ToLower();
            if (low.Contains("robotic_arm"))
                armAssemblyPath = path;
            else if (low.Contains("gripper"))
                gripperPath = path;
            else if (low.Contains("uav") || low.Contains("drone") || low.Contains("rotor") || low.Contains("flight") || low.Contains("brushless"))
                uavPaths.Add(path);
            else
                armParts.Add(path);
        }

        int placed = 0;
        if (!string.IsNullOrEmpty(armAssemblyPath))
        {
            PlaceSingleModel(armAssemblyPath, root.transform, new Vector3(-0.10f, 0.007f, -0.14f), 0.38f, matSafetyOrange, matBrushedAlu, ref placed);
        }
        if (!string.IsNullOrEmpty(gripperPath))
        {
            PlaceSingleModel(gripperPath, root.transform, new Vector3(0.22f, 0.007f, -0.14f), 0.18f, matBrushedAlu, matGunmetal, ref placed);
        }

        DistributeRow(armParts, root.transform, 0.168f, 0.31f, 1.16f, 0.12f, matGunmetal, matBrushedAlu, matCopperCoil, ref placed);
        DistributeRow(uavPaths, root.transform, 0.358f, 0.31f, 1.10f, 0.13f, matCarbonDark, matAvionicsTeal, matBrushedAlu, ref placed);

        Transform socket = bay.transform.Find("Socket_Robotics");
        if (socket != null) socket.localPosition = new Vector3(0.76f, 1.15f, 1.45f);

        Debug.Log($"Placed and rigged {placed} real CAD robotics & drone components onto Bay2_Robotics!");
    }

    private static void DistributeRow(List<string> paths, Transform parent, float localY, float localZ, float spanWidth,
        float targetSize, Material matA, Material matB, Material matC, ref int counter)
    {
        if (paths.Count == 0) return;
        float step = paths.Count == 1 ? 0f : spanWidth / (paths.Count - 1);
        float startX = paths.Count == 1 ? 0f : -spanWidth * 0.5f;

        for (int i = 0; i < paths.Count; i++)
        {
            Vector3 pos = new Vector3(startX + (i * step), localY, localZ);
            Material primary = (i % 3 == 0) ? matA : (i % 3 == 1 ? matB : matC);
            Material secondary = (i % 2 == 0) ? matB : matA;
            PlaceSingleModel(paths[i], parent, pos, targetSize, primary, secondary, ref counter);
        }
    }

    private static void PlaceSingleModel(string assetPath, Transform parent, Vector3 localPos, float targetMaxSize,
        Material primaryMat, Material secondaryMat, ref int counter)
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
        inst.name = Path.GetFileNameWithoutExtension(assetPath);
        inst.transform.SetParent(parent);
        inst.transform.localPosition = localPos;
        inst.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        Bounds b = GetBounds(inst);
        float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (maxDim > 0.0001f) inst.transform.localScale *= (targetMaxSize / maxDim);

        Bounds scaledB = GetBounds(inst);
        float targetWorldY = parent.TransformPoint(localPos).y;
        inst.transform.position += new Vector3(0f, targetWorldY - scaledB.min.y, 0f);

        int subIdx = 0;
        foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>())
        {
            GameObject part = mf.gameObject;
            part.tag = "RoboticsPart";

            if (part.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
            {
                if (mr.sharedMaterials == null || mr.sharedMaterials.Length <= 1)
                {
                    mr.sharedMaterial = (subIdx % 2 == 0) ? primaryMat : secondaryMat;
                }
            }

            Collider c = part.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            BoxCollider mc = part.AddComponent<BoxCollider>();
            

            Rigidbody rb = part.GetComponent<Rigidbody>();
            if (rb == null) rb = part.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            subIdx++;
        }
        counter++;
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
