using UnityEditor;
using UnityEngine;

public class V8MetallicPostprocessor : AssetPostprocessor
{
    Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        if (!assetPath.Contains("V8_Engine_Optimized"))
            return null;

        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0.92f);

        float smoothness = material.name.Contains("0.823529") ? 0.85f : 0.74f;
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);

        return null;
    }
}
