using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class LabEnvironmentBuilder
{
    [MenuItem("Tools/Build Industrial Lab Environment")]
    public static void BuildLabRoom()
    {
        GameObject mainLab = GameObject.Find("MainLab");
        Transform rootParent = mainLab != null ? mainLab.transform : null;

        GameObject oldEnv = GameObject.Find("Lab_Architecture");
        if (oldEnv != null) Object.DestroyImmediate(oldEnv);

        GameObject env = new GameObject("Lab_Architecture");
        if (rootParent != null) env.transform.SetParent(rootParent);
        env.transform.position = Vector3.zero;

        // Materials
        Material matFloor = GetMat("Mat_Lab_EpoxyFloor", new Color(0.13f, 0.15f, 0.18f), 0.25f, 0.55f);
        Material matWallLower = GetMat("Mat_Lab_WallDark", new Color(0.18f, 0.21f, 0.25f), 0.15f, 0.35f);
        Material matWallUpper = GetMat("Mat_Lab_WallConcrete", new Color(0.32f, 0.35f, 0.39f), 0.05f, 0.25f);
        Material matCeiling = GetMat("Mat_Lab_Ceiling", new Color(0.12f, 0.13f, 0.15f), 0.10f, 0.20f);
        Material matBeam = GetMat("Mat_Lab_SteelBeam", new Color(0.22f, 0.25f, 0.29f), 0.80f, 0.65f);
        Material matYellowStripe = GetMat("Mat_Lab_SafetyYellow", new Color(0.88f, 0.68f, 0.08f), 0.10f, 0.40f);
        Material matLedStrip = GetEmissiveMat("Mat_Lab_LED", new Color(0.92f, 0.96f, 1.0f), 2.2f);

        // Hide the bare default Floor plane if present so the textured lab floor replaces it cleanly
        GameObject oldFloor = GameObject.Find("Floor");
        if (oldFloor != null) oldFloor.SetActive(false);

        // 1. Industrial Epoxy Floor (spanning X = -4 to +24, Z = -4 to +8)
        CreateStaticPart(env.transform, "Lab_Floor", new Vector3(10f, -0.05f, 2f), new Vector3(28f, 0.1f, 12f), matFloor, true);

        // 2. Perimeter Walls (Back Z=7.8, Front Z=-3.8, Left X=-3.8, Right X=23.8)
        CreateStaticPart(env.transform, "Wall_Back_Lower", new Vector3(10f, 0.75f, 7.8f), new Vector3(28f, 1.5f, 0.2f), matWallLower, true);
        CreateStaticPart(env.transform, "Wall_Back_Upper", new Vector3(10f, 3.0f, 7.8f), new Vector3(28f, 3.0f, 0.2f), matWallUpper, true);
        CreateStaticPart(env.transform, "Wall_Front", new Vector3(10f, 2.25f, -3.8f), new Vector3(28f, 4.5f, 0.2f), matWallUpper, true);
        CreateStaticPart(env.transform, "Wall_Left", new Vector3(-3.8f, 2.25f, 2f), new Vector3(0.2f, 4.5f, 12f), matWallUpper, true);
        CreateStaticPart(env.transform, "Wall_Right", new Vector3(23.8f, 2.25f, 2f), new Vector3(0.2f, 4.5f, 12f), matWallUpper, true);

        // 3. Ceiling + Structural Steel Girders
        CreateStaticPart(env.transform, "Lab_Ceiling", new Vector3(10f, 4.55f, 2f), new Vector3(28f, 0.15f, 12f), matCeiling, false);
        for (float x = -2f; x <= 22f; x += 4f)
        {
            CreateStaticPart(env.transform, $"Ceiling_Girder_{x}", new Vector3(x, 4.35f, 2f), new Vector3(0.18f, 0.25f, 11.6f), matBeam, false);
        }

        // 4. Bay Divider Partitions & Safety Floor Zones at X=0, X=10, X=20
        float[] bayXs = { 0f, 10f, 20f };
        for (int i = 0; i < bayXs.Length; i++)
        {
            float bx = bayXs[i];

            // Overhead LED Panel above each workbench
            CreateStaticPart(env.transform, $"Overhead_LED_Fixture_Bay{i}", new Vector3(bx, 4.18f, 1.5f), new Vector3(2.2f, 0.06f, 0.45f), matLedStrip, false);

            // Subtle Point Light over each bay for crisp highlights on metal/PCBs
            GameObject lightGo = new GameObject($"Bay{i}_WorkLight");
            lightGo.transform.SetParent(env.transform);
            lightGo.transform.position = new Vector3(bx, 3.6f, 1.3f);
            Light pt = lightGo.AddComponent<Light>();
            pt.type = LightType.Point;
            pt.range = 8.5f;
            pt.intensity = 1.6f;
            pt.color = new Color(0.95f, 0.97f, 1.0f);

            // Safety yellow perimeter floor stripes around each workbench
            CreateStaticPart(env.transform, $"SafetyLine_Front_Bay{i}", new Vector3(bx, 0.002f, 0.35f), new Vector3(2.8f, 0.004f, 0.06f), matYellowStripe, false);
            CreateStaticPart(env.transform, $"SafetyLine_Left_Bay{i}", new Vector3(bx - 1.4f, 0.002f, 1.5f), new Vector3(0.06f, 0.004f, 2.3f), matYellowStripe, false);
            CreateStaticPart(env.transform, $"SafetyLine_Right_Bay{i}", new Vector3(bx + 1.4f, 0.002f, 1.5f), new Vector3(0.06f, 0.004f, 2.3f), matYellowStripe, false);
        }

        // Partial glass/frame partitions separating Bay 0 | Bay 1 | Bay 2 (X = 5 and X = 15)
        foreach (float divX in new[] { 5f, 15f })
        {
            CreateStaticPart(env.transform, $"BayDivider_Base_{divX}", new Vector3(divX, 0.6f, 4.8f), new Vector3(0.12f, 1.2f, 5.6f), matWallLower, false);
            CreateStaticPart(env.transform, $"BayDivider_Post_{divX}", new Vector3(divX, 2.25f, 2.05f), new Vector3(0.14f, 4.5f, 0.14f), matBeam, false);
        }

        // 5. Set Main Camera background to clean dark studio slate in case any corner is visible
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.08f, 0.10f, 0.13f);
        }

        // Refresh ReflectionProbe so metallic V8 and steel parts reflect the new lab interior
        ReflectionProbe probe = Object.FindAnyObjectByType<ReflectionProbe>();
        if (probe != null) probe.RenderProbe();

        Debug.Log("Built enclosed 3-bay Industrial Mechatronics Lab environment!");
    }

    private static void CreateStaticPart(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = pos;
        obj.transform.localScale = scale;
        obj.isStatic = true;

        if (obj.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
            mr.sharedMaterial = mat;

        if (!keepCollider && obj.TryGetComponent<Collider>(out Collider col))
            Object.DestroyImmediate(col);
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

    private static Material GetEmissiveMat(string name, Color c, float intensity)
    {
        Material m = GetMat(name, c, 0f, 0.8f);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
        return m;
    }
}
