using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WireSystemInstaller
{
    [MenuItem("Tools/Install Tinkercad Wire & Holo Label Systems")]
    public static void InstallAll()
    {
        GameObject mainLab = GameObject.Find("MainLab");
        if (mainLab == null)
        {
            Debug.LogError("Could not find 'MainLab' root GameObject in the active scene.");
            return;
        }

        EnsureComponent<TinkercadWireSystem>(mainLab);
        EnsureComponent<LiveCodeInterpreter>(mainLab);
        EnsureComponent<McuVirtualMachine>(mainLab);
        EnsureComponent<PartFocusCamera>(mainLab);
        EnsureComponent<PocketDimensionBay>(mainLab);
        EnsureComponent<ForgeBayLandingHero>(mainLab);

        // Optional Holo label script if present
        System.Type holoType = System.Type.GetType("HoloPartLabelSystem, Assembly-CSharp");
        if (holoType != null && mainLab.GetComponent(holoType) == null)
            mainLab.AddComponent(holoType);

        EditorUtility.SetDirty(mainLab);
        EditorSceneManager.MarkSceneDirty(mainLab.scene);
        Debug.Log("Installed Wire, Holo Label, Live Code IDE, [F] Part Focus Camera, and LabLayoutAndCollisionGuard onto MainLab!");
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T comp = target.GetComponent<T>();
        return comp != null ? comp : target.AddComponent<T>();
    }
}
