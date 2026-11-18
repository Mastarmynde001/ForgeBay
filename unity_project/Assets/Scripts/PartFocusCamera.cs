using UnityEngine;

public class PartFocusCamera : MonoBehaviour
{
    private bool isFocused = false;
    private bool isReturning = false;
    private Vector3 savedCamPos;
    private Quaternion savedCamRot;

    private Vector3 targetCamPos;
    private Quaternion targetCamRot;
    private Transform focusedPart;
    private string focusedLabel = "";

    private void Update()
    {
        // Ignore [F] if user is actively typing inside the Firmware IDE text box
        if (GUIUtility.keyboardControl != 0) return;

        // Cancel focus immediately if user switches bays (1, 2, 3)
        if ((isFocused || isReturning) &&
            (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Alpha3)))
        {
            isFocused = false;
            isReturning = false;
            focusedPart = null;
            return;
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            if (isFocused)
            {
                isFocused = false;
                isReturning = true;
            }
            else
            {
                TryFocusHoveredPart();
            }
        }
    }

    private void LateUpdate()
    {
        if (Camera.main == null) return;

        // Run in LateUpdate so it overrides DesktopLabController's bay camera lock
        if (isFocused && focusedPart != null)
        {
            Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, targetCamPos, Time.deltaTime * 12f);
            Camera.main.transform.rotation = Quaternion.Slerp(Camera.main.transform.rotation, targetCamRot, Time.deltaTime * 12f);
        }
        else if (isReturning)
        {
            Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, savedCamPos, Time.deltaTime * 12f);
            Camera.main.transform.rotation = Quaternion.Slerp(Camera.main.transform.rotation, savedCamRot, Time.deltaTime * 12f);

            if (Vector3.Distance(Camera.main.transform.position, savedCamPos) < 0.015f)
            {
                isReturning = false;
                focusedPart = null;
            }
        }
    }

    private void TryFocusHoveredPart()
    {
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 25f)) return;

        Transform root = ResolvePartRoot(hit.transform);
        if (root == null) return;

        Bounds b = GetBounds(root);
        float maxExtent = Mathf.Max(b.size.x, b.size.y, b.size.z);
        float zoomDist = Mathf.Clamp(maxExtent * 2.3f, 0.25f, 0.55f);

        if (!isReturning)
        {
            savedCamPos = Camera.main.transform.position;
            savedCamRot = Camera.main.transform.rotation;
        }

        Vector3 viewDir = Camera.main.transform.position - b.center;
        viewDir.y = 0f;
        if (viewDir.sqrMagnitude < 0.001f) viewDir = -Vector3.forward;
        viewDir.Normalize();

        targetCamPos = b.center + (viewDir * zoomDist) + (Vector3.up * (zoomDist * 0.75f));
        targetCamRot = Quaternion.LookRotation((b.center + Vector3.up * 0.008f) - targetCamPos, Vector3.up);

        focusedPart = root;
        focusedLabel = root.name.Replace("Electrical_", "").Replace("Rob_", "").Replace("_", " ");
        isFocused = true;
        isReturning = false;
    }

    private Transform ResolvePartRoot(Transform t)
    {
        if (t == null) return null;
        string low = t.name.ToLower();
        if (low.Contains("floor") || low.Contains("wall") || low.Contains("workbench") ||
            low.Contains("shelf") || low.Contains("upright") || low.Contains("ceiling") ||
            low.Contains("pad") || low.Contains("girder") || low.Contains("safety"))
            return null;

        Transform p = t;
        while (p != null && p.parent != null)
        {
            if (p.parent.name == "Real_Electronics_Lab" || p.parent.name == "Real_Robotics_Lab")
                return p;
            p = p.parent;
        }
        return t;
    }

    private Bounds GetBounds(Transform root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(root.position, Vector3.one * 0.1f);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    private void Start()
    {
        // Hide any standalone debug sphere mesh in the lab (e.g. probe preview orb)
        foreach (MeshFilter mf in FindObjectsByType<MeshFilter>())
        {
            if (mf.sharedMesh != null && mf.sharedMesh.name == "Sphere")
            {
                if (!mf.CompareTag("ElectronicPart") && !mf.CompareTag("RoboticsPart"))
                    mf.gameObject.SetActive(false);
            }
        }
    }

    private void OnGUI()
    {
        if (!isFocused) return;

        GUI.depth = -80;
        Rect pill = new Rect((Screen.width - 380f) * 0.5f, 14f, 380f, 30f);
        if (ForgeBayTheme.DrawPillButton(pill, $"🔍  INSPECTING: {focusedLabel.ToUpper()}   •   [F] EXIT ZOOM", true, ForgeBayTheme.AccentCyan))
        {
            isFocused = false;
            isReturning = true;
        }
    }
}
