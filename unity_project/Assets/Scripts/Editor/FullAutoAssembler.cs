using System;
using UnityEditor;
using UnityEngine;

public class FullAutoAssembler : EditorWindow
{
    [MenuItem("Tools/Assemble Complete Lab Automatically")]
    public static void AssembleEverything()
    {
        AddTag("MechanicalPart");
        AddTag("ElectronicPart");
        AddTag("RoboticsPart");

        if (GameObject.Find("Floor") == null)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(5, 1, 5);
        }

        GameObject bay0 = CreateBay("Bay0_Mechanical", new Vector3(0, 0, 0));
        GameObject bay1 = CreateBay("Bay1_Electronics", new Vector3(10, 0, 0));
        GameObject bay2 = CreateBay("Bay2_Robotics", new Vector3(20, 0, 0));

        PopulateFolderModels("Assets/Models/W1_Mechanical", bay0.transform, new Vector3(-0.35f, 1.02f, 1.5f), "MechanicalPart", true);
        PopulateFolderModels("Assets/Models/W2_Electronics", bay1.transform, new Vector3(-0.35f, 1.02f, 1.5f), "ElectronicPart", false);
        PopulateFolderModels("Assets/Models/W3_Robotics", bay2.transform, new Vector3(-0.35f, 1.02f, 1.5f), "RoboticsPart", true);

        CreateSnapZone(bay0.transform, "Socket_Mechanical", new Vector3(0.55f, 1.15f, 1.5f), "MechanicalPart");
        CreateSnapZone(bay1.transform, "Socket_Electronic", new Vector3(0.55f, 1.15f, 1.5f), "ElectronicPart");
        CreateSnapZone(bay2.transform, "Socket_Robotics", new Vector3(0.55f, 1.15f, 1.5f), "RoboticsPart");

        if (Camera.main != null)
        {
            Camera.main.transform.position = new Vector3(0f, 1.85f, 0.15f);
            Camera.main.transform.LookAt(new Vector3(0f, 1.15f, 1.5f));
        }

        GameObject net = GameObject.Find("NetworkManager");
        if (net == null) net = new GameObject("NetworkManager");
        AttachScriptByName(net, "UDPBridge");

        Selection.activeGameObject = bay0;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.LookAt(new Vector3(0f, 1.2f, 1.5f), Quaternion.Euler(25f, 0f, 0f), 2.2f);
        }

        Debug.Log("Full Lab Assembly Complete: V8 Engine centered on workbench & camera framed!");
    }

    private static void PopulateFolderModels(string folderPath, Transform parentBay, Vector3 targetSurfacePos, string partTag, bool startKinematic)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogWarning("Skipping missing folder: " + folderPath);
            return;
        }

        string[] files = System.IO.Directory.GetFiles(folderPath, "*.*", System.IO.SearchOption.AllDirectories);
        float xOffset = 0f;

        foreach (string rawPath in files)
        {
            string assetPath = rawPath.Replace("\\", "/");
            if (!assetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) && !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (modelPrefab == null) continue;

            Transform existing = parentBay.Find(modelPrefab.name);
            if (existing != null) DestroyImmediate(existing.gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, parentBay);
            instance.name = modelPrefab.name;

            NormalizeAndCenterOnSurface(instance, 0.75f, parentBay.TransformPoint(targetSurfacePos + new Vector3(xOffset, 0, 0)));

            foreach (MeshRenderer mr in instance.GetComponentsInChildren<MeshRenderer>())
            {
                GameObject subPart = mr.gameObject;
                subPart.tag = partTag;

                if (subPart.GetComponent<Collider>() == null)
                {
                    BoxCollider mc = subPart.AddComponent<BoxCollider>();
                    
                }

                Rigidbody rb = subPart.GetComponent<Rigidbody>();
                if (rb == null) rb = subPart.AddComponent<Rigidbody>();
                rb.mass = 0.5f;
                rb.useGravity = true;
                rb.isKinematic = startKinematic;

                AttachScriptByName(subPart, "HandGrabItem");
            }

            xOffset += 0.65f;
        }
    }

    private static void NormalizeAndCenterOnSurface(GameObject obj, float targetMaxDimension, Vector3 desiredBottomCenterWorld)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            obj.transform.position = desiredBottomCenterWorld;
            return;
        }

        Bounds bounds = new Bounds(renderers[0].bounds.center, Vector3.zero);
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

        float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxDim > 0.0001f)
        {
            float scaleFactor = targetMaxDimension / maxDim;
            obj.transform.localScale *= scaleFactor;
        }

        bounds = new Bounds(renderers[0].bounds.center, Vector3.zero);
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

        Vector3 currentBottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Vector3 shift = desiredBottomCenterWorld - currentBottomCenter;
        obj.transform.position += shift;
    }

    private static void CreateSnapZone(Transform parentBay, string zoneName, Vector3 localPos, string acceptedTag)
    {
        Transform existing = parentBay.Find(zoneName);
        if (existing != null) DestroyImmediate(existing.gameObject);

        GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        zone.name = zoneName;
        zone.transform.SetParent(parentBay);
        zone.transform.localPosition = localPos;
        zone.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

        SphereCollider sc = zone.GetComponent<SphereCollider>();
        if (sc != null) sc.isTrigger = true;

        MeshRenderer mr = zone.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            Material ghostMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            ghostMat.color = new Color(0.1f, 0.7f, 1.0f, 0.35f);
            mr.sharedMaterial = ghostMat;
        }

        Component snapComp = AttachScriptByName(zone, "AdvancedSnapZone") ?? AttachScriptByName(zone, "SnapZone");
        if (snapComp != null)
        {
            SerializedObject so = new SerializedObject(snapComp);
            SerializedProperty tagProp = so.FindProperty("acceptedTag") ?? so.FindProperty("requiredTag") ?? so.FindProperty("targetTag");
            if (tagProp != null && tagProp.propertyType == SerializedPropertyType.String)
            {
                tagProp.stringValue = acceptedTag;
                so.ApplyModifiedProperties();
            }
        }
    }

    private static Component AttachScriptByName(GameObject target, string className)
    {
        Type scriptType = FindTypeInAssemblies(className);
        if (scriptType == null)
        {
            Debug.LogWarning($"Script '{className}.cs' not found in project yet — skipped attaching to {target.name}.");
            return null;
        }

        Component existing = target.GetComponent(scriptType);
        return existing != null ? existing : target.AddComponent(scriptType);
    }

    private static Type FindTypeInAssemblies(string className)
    {
        foreach (Type t in TypeCache.GetTypesDerivedFrom<Component>())
        {
            if (t.FullName == className || t.Name == className)
                return t;
        }
        return Type.GetType(className);
    }

    private static GameObject CreateBay(string bayName, Vector3 origin)
    {
        GameObject bay = GameObject.Find(bayName);
        if (bay == null)
        {
            bay = new GameObject(bayName);
            bay.transform.position = origin;

            GameObject bench = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bench.name = "Workbench";
            bench.transform.SetParent(bay.transform);
            bench.transform.localPosition = new Vector3(0, 0.5f, 1.5f);
            bench.transform.localScale = new Vector3(2.4f, 1f, 1.2f);

            MeshRenderer mr = bench.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = GetOrCreateWorkbenchMaterial();
            }
        }
        return bay;
    }

    private static Material GetOrCreateWorkbenchMaterial()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        string matPath = "Assets/Materials/Mat_Workbench.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            Color slateColor = new Color(0.17f, 0.20f, 0.24f, 1f);
            mat.color = slateColor;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", slateColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.25f);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
        }
        return mat;
    }

    private static void AddTag(string tagName)
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName) return;

        tagsProp.InsertArrayElementAtIndex(0);
        tagsProp.GetArrayElementAtIndex(0).stringValue = tagName;
        tagManager.ApplyModifiedProperties();
    }
}
