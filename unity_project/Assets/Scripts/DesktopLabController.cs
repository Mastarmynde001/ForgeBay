using System.Collections.Generic;
using UnityEngine;

public class DesktopLabController : MonoBehaviour
{
    [Header("Grab & Snap Settings")]
    public float dragSmoothSpeed = 22f;
    public float socketSnapRadius = 0.35f;
    public float homeRejoinRadius = 0.22f;
    public float keyDepthSpeed = 1.2f;

    [Header("Camera Orbit Settings")]
    public float orbitSpeed = 3.5f;
    public float keyOrbitSpeed = 75f;
    public float zoomSpeed = 1.5f;

    private struct PartHome
    {
        public int bayIndex;
        public Transform parent;
        public Vector3 localPos;
        public Quaternion localRot;
    }

    private readonly Dictionary<Transform, PartHome> partHomes = new Dictionary<Transform, PartHome>();
    private Rigidbody grabbedRb;
    private Transform grabbedTransform;
    private Vector3 grabOffset;
    private float currentGrabDepth;
    private readonly bool[] bayExploded = { false, false, false };

    private int activeBayIndex = 1;
    private readonly string[] bayNames = { "1: Mechanical (V8)", "2: Electronics", "3: Robotics" };
    private readonly float[] bayXPositions = { 0f, 10f, 20f };

    private Vector3 focusPoint = new Vector3(0f, 1.15f, 1.5f);
    private float yaw = 0f;
    private float pitch = 25f;
    private float viewDistance = 1.65f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttachToMainCamera()
    {
        if (Camera.main != null && Camera.main.GetComponent<DesktopLabController>() == null)
        {
            Camera.main.gameObject.AddComponent<DesktopLabController>();
        }
    }

    void Start()
    {
        RegisterAllLabParts();
        PositionSocketClearOfEngine();
        SwitchBay(0);
    }

    private void RegisterAllLabParts()
    {
        partHomes.Clear();
        string[] bays = { "Bay0_Mechanical", "Bay1_Electronics", "Bay2_Robotics" };

        for (int i = 0; i < bays.Length; i++)
        {
            GameObject bay = GameObject.Find(bays[i]);
            if (bay == null) continue;

            foreach (MeshCollider mc in bay.GetComponentsInChildren<MeshCollider>())
            {
                GameObject obj = mc.gameObject;
                if (obj.name == "Workbench" || obj.name == "Floor" || obj.name.StartsWith("Socket_"))
                    continue;

                if (!partHomes.ContainsKey(obj.transform))
                {
                    partHomes[obj.transform] = new PartHome
                    {
                        bayIndex = i,
                        parent = obj.transform.parent,
                        localPos = obj.transform.localPosition,
                        localRot = obj.transform.localRotation
                    };
                }
            }
        }
        Debug.Log($"DesktopLabController ready: {partHomes.Count} grabbable lab parts registered across 3 bays.");
    }

    private void PositionSocketClearOfEngine()
    {
        GameObject socket = GameObject.Find("Socket_Mechanical");
        if (socket != null)
        {
            socket.transform.localPosition = new Vector3(0.75f, 1.15f, 1.5f);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H)) showLabMenu = !showLabMenu;
        HandleHotkeys();
        HandleCameraOrbit();
        HandleTouchpadGrab();
    }

    private void HandleHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchBay(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchBay(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchBay(2);
        if (Input.GetKeyDown(KeyCode.E)) ToggleExplodeView();
        if (Input.GetKeyDown(KeyCode.R)) RejoinActiveBayParts();
    }

    public void SwitchBay(int index)
    {
        activeBayIndex = Mathf.Clamp(index, 0, bayXPositions.Length - 1);
        focusPoint = new Vector3(bayXPositions[activeBayIndex], 1.15f, 1.5f);
        yaw = 0f;
        pitch = 25f;
        viewDistance = 1.65f;
        UpdateCameraTransform();
    }

    public void ToggleExplodeView()
    {
        bayExploded[activeBayIndex] = !bayExploded[activeBayIndex];
        if (!bayExploded[activeBayIndex])
        {
            RejoinActiveBayParts();
            return;
        }

        Vector3 bayCenter = new Vector3(bayXPositions[activeBayIndex] - 0.12f, 1.22f, 1.5f);
        foreach (var kvp in partHomes)
        {
            if (kvp.Value.bayIndex != activeBayIndex) continue;
            Transform part = kvp.Key;
            if (part == null) continue;

            Rigidbody rb = part.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            Vector3 dir = part.position - bayCenter;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
            part.position += dir.normalized * 0.24f;
        }
        Debug.Log($"Exploded {bayNames[activeBayIndex]} — drag any part to inspect or socket.");
    }

    public void RejoinActiveBayParts()
    {
        bayExploded[activeBayIndex] = false;
        foreach (var kvp in partHomes)
        {
            if (kvp.Value.bayIndex != activeBayIndex) continue;
            Transform part = kvp.Key;
            if (part == null) continue;

            Rigidbody rb = part.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            part.SetParent(kvp.Value.parent);
            part.localPosition = kvp.Value.localPos;
            part.localRotation = kvp.Value.localRot;
        }
        Debug.Log($"Rejoined all parts in {bayNames[activeBayIndex]}.");
    }

    public void SyncArrangedHomes()
    {
        List<Transform> keys = new List<Transform>(partHomes.Keys);
        foreach (Transform part in keys)
        {
            if (part == null) continue;
            PartHome h = partHomes[part];
            h.localPos = part.localPosition;
            h.localRot = part.localRotation;
            partHomes[part] = h;
        }
    }

    private void HandleCameraOrbit()
    {
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * orbitSpeed;
            pitch -= Input.GetAxis("Mouse Y") * orbitSpeed;
        }

        if (Input.GetKey(KeyCode.LeftArrow)) yaw -= keyOrbitSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.RightArrow)) yaw += keyOrbitSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.UpArrow)) pitch += keyOrbitSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.DownArrow)) pitch -= keyOrbitSpeed * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, 5f, 80f);

        float scroll = ForgeBayTheme.IsPointerOverUI ? 0f : Input.GetAxis("Mouse ScrollWheel");
        float depthDelta = scroll * zoomSpeed;
        // [W] reserved for TinkercadWireSystem; use Mouse Scroll or Up/Down arrows for depth
        if (Input.GetKey(KeyCode.S)) depthDelta -= keyDepthSpeed * Time.deltaTime;

        if (Mathf.Abs(depthDelta) > 0.0001f)
        {
            if (grabbedTransform != null)
                currentGrabDepth = Mathf.Clamp(currentGrabDepth + depthDelta, 0.4f, 4.0f);
            else
                viewDistance = Mathf.Clamp(viewDistance - depthDelta, 0.6f, 4.5f);
        }

        UpdateCameraTransform();
    }

    private void UpdateCameraTransform()
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rot * new Vector3(0f, 0f, -viewDistance);
        transform.position = focusPoint + offset;
        transform.LookAt(focusPoint);
    }

    private void HandleTouchpadGrab()
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;

        if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftAlt))
        {
            TinkercadWireSystem wireSys = FindAnyObjectByType<TinkercadWireSystem>();
            if (wireSys != null && wireSys.IsWireModeActive) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f))
            {
                Transform hitTransform = hit.collider.transform;
                if (partHomes.ContainsKey(hitTransform))
                {
                    grabbedTransform = hitTransform;
                    grabbedRb = hitTransform.GetComponent<Rigidbody>();
                    if (grabbedRb != null)
                    {
                        grabbedRb.isKinematic = true;
                        grabbedRb.linearVelocity = Vector3.zero;
                    }
                    currentGrabDepth = hit.distance;
                    grabOffset = grabbedTransform.position - hit.point;
                }
            }
        }

        if (Input.GetMouseButton(0) && grabbedTransform != null)
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 targetPos = ray.GetPoint(currentGrabDepth) + grabOffset;
            grabbedTransform.position = Vector3.Lerp(grabbedTransform.position, targetPos, Time.deltaTime * dragSmoothSpeed);
        }

        if (Input.GetMouseButtonUp(0) && grabbedTransform != null)
        {
            TrySnapOrRejoin(grabbedTransform);
            grabbedTransform = null;
            grabbedRb = null;
        }
    }

    private void TrySnapOrRejoin(Transform part)
    {
        if (partHomes.TryGetValue(part, out PartHome home) && home.parent != null)
        {
            Vector3 homeWorldPos = home.parent.TransformPoint(home.localPos);
            if (Vector3.Distance(part.position, homeWorldPos) <= homeRejoinRadius)
            {
                part.localPosition = home.localPos;
                part.localRotation = home.localRot;
                if (grabbedRb != null) grabbedRb.isKinematic = true;
                Debug.Log($"Rejoined {part.name} back to home assembly!");
                return;
            }
        }

        Collider[] nearby = Physics.OverlapSphere(part.position, socketSnapRadius);
        foreach (Collider col in nearby)
        {
            if (col.isTrigger && col.name.StartsWith("Socket_"))
            {
                part.position = col.transform.position;
                part.rotation = col.transform.rotation;
                if (grabbedRb != null) grabbedRb.isKinematic = true;
                Debug.Log($"Snapped {part.name} into {col.name}!");
                return;
            }
        }

        if (grabbedRb != null) grabbedRb.isKinematic = true;
        Debug.Log($"Separated {part.name} for inspection.");
    }

    private bool showLabMenu = true;

    private void OnGUI()
    {
        if (!showLabMenu)
        {
            if (GUI.Button(new Rect(12f, 12f, 135f, 26f), "[H] Show Lab Menu"))
                showLabMenu = true;
            return;
        }
        if (GUI.Button(new Rect(392f, 12f, 26f, 24f), "-"))
            showLabMenu = false;

        GUI.Box(new Rect(15, 15, 500, 105), "ForgeBay Mechatronics Lab — Disassembly & Snap Trainer");
        GUI.Label(new Rect(25, 38, 480, 22), "Drag: Separate Part | Drop on Assembly/Socket: Rejoin | W/S: Depth | Arrows: Orbit");

        for (int i = 0; i < bayNames.Length; i++)
        {
            if (GUI.Button(new Rect(25 + (i * 160), 62, 152, 24), bayNames[i]))
            {
                SwitchBay(i);
            }
        }

        if (GUI.Button(new Rect(25, 90, 232, 24), bayExploded[activeBayIndex] ? "Collapse Bay (E)" : "Explode Bay Parts (E)"))
        {
            ToggleExplodeView();
        }
        if (GUI.Button(new Rect(265, 90, 232, 24), "Rejoin All Parts (R)"))
        {
            RejoinActiveBayParts();
        }
    }
}
