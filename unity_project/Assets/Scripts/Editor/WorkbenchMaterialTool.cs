using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class WorkbenchMaterialTool
{
    [MenuItem("Tools/Apply Dark Workbench Material")]
    public static void ApplyDarkWorkbenchMaterial()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        string matPath = "Assets/Materials/Mat_Workbench.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            bool isURP = GraphicsSettings.defaultRenderPipeline != null;
            Shader shader = isURP
                ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                : Shader.Find("Standard");

            mat = new Material(shader);
            Color slateColor = new Color(0.15f, 0.17f, 0.21f, 1f);
            mat.color = slateColor;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", slateColor);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.05f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.20f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.20f);

            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
        }

        string[] bays = { "Bay0_Mechanical", "Bay1_Electronics", "Bay2_Robotics" };
        int updated = 0;
        foreach (string bayName in bays)
        {
            GameObject bay = GameObject.Find(bayName);
            if (bay == null) continue;
            Transform bench = bay.transform.Find("Workbench");
            if (bench != null && bench.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
            {
                mr.sharedMaterial = mat;
                EditorUtility.SetDirty(mr);
                updated++;
            }
        }

        Debug.Log($"Applied Mat_Workbench to {updated} workbenches.");
    }
}
