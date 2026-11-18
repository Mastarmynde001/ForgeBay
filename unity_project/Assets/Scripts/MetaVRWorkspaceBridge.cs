using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ForgeBay.VR
{
    public class MetaVRWorkspaceBridge : MonoBehaviour
    {
        public Vector3 hudWorldPosition = new Vector3(0f, 1.35f, 0.25f);
        public Vector3 hudWorldRotation = new Vector3(12f, 0f, 0f);
        public float worldCanvasScale = 0.00085f;
        public Vector3 benchButtonBarPosition = new Vector3(0f, 0.92f, -0.35f);

        private readonly HashSet<Canvas> _converted = new HashSet<Canvas>();

        private void Start()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb != null && (mb.GetType().Name == "DesktopLabController" || mb.GetType().Name == "CameraOrbitController"))
                    mb.enabled = false;
            }
            BuildBenchButtons();
            StartCoroutine(ConvertRoutine());
        }

        private IEnumerator ConvertRoutine()
        {
            ConvertCanvases();
            yield return new WaitForSeconds(0.5f);
            ConvertCanvases();
            yield return new WaitForSeconds(1.5f);
            ConvertCanvases();
        }

        private void ConvertCanvases()
        {
            int idx = 0;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas == null || _converted.Contains(canvas)) continue;
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    _converted.Add(canvas);
                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.worldCamera = Camera.main;

                    var rt = canvas.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(1280f, 720f);
                        rt.localScale = Vector3.one * worldCanvasScale;
                        float xShift = (idx == 0) ? 0f : (idx % 2 == 1 ? -0.55f : 0.55f);
                        rt.position = hudWorldPosition + new Vector3(xShift, idx == 0 ? 0f : 0.08f, 0.05f * idx);
                        rt.rotation = Quaternion.Euler(hudWorldRotation);
                    }
                    if (canvas.GetComponent<BoxCollider>() == null)
                    {
                        var col = canvas.gameObject.AddComponent<BoxCollider>();
                        col.size = new Vector3(1280f, 720f, 10f);
                    }
                    idx++;
                }
            }
        }

        private void BuildBenchButtons()
        {
            var root = new GameObject("ForgeBay_VR_BenchControlBar");
            root.transform.position = benchButtonBarPosition;
            root.transform.rotation = Quaternion.Euler(25f, 0f, 0f);

            string[] labels = { "ENTER LAB [M]", "CATALOG [B]", "WIRE TOOL [W]", "RUN IDE [I]" };
            string[] actions = { "M", "B", "W", "I" };
            Color[] colors = {
                new Color(0.06f, 0.85f, 0.55f),
                new Color(0.12f, 0.65f, 0.95f),
                new Color(0.95f, 0.65f, 0.12f),
                new Color(0.75f, 0.35f, 0.95f)
            };

            for (int i = 0; i < labels.Length; i++)
            {
                var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.name = "VR_PokeButton_" + actions[i];
                btn.transform.SetParent(root.transform, false);
                btn.transform.localPosition = new Vector3((i - 1.5f) * 0.16f, 0f, 0f);
                btn.transform.localScale = new Vector3(0.135f, 0.035f, 0.065f);
                btn.GetComponent<Renderer>().material.color = colors[i];
                btn.GetComponent<BoxCollider>().isTrigger = true;

                var trig = btn.AddComponent<VRBenchButtonTrigger>();
                trig.actionCode = actions[i];
                trig.baseColor = colors[i];

                var lblObj = new GameObject("Label");
                lblObj.transform.SetParent(btn.transform, false);
                lblObj.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                lblObj.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);
                lblObj.transform.localScale = new Vector3(0.08f, 0.3f, 0.15f);
                var tm = lblObj.AddComponent<TextMesh>();
                tm.text = labels[i];
                tm.fontSize = 36;
                tm.characterSize = 0.12f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Color.white;
            }
        }
    }

    public class VRBenchButtonTrigger : MonoBehaviour
    {
        public string actionCode;
        public Color baseColor;
        private float _cooldown;

        private void OnTriggerEnter(Collider other)
        {
            if (Time.time < _cooldown) return;
            _cooldown = Time.time + 0.45f;
            TriggerAction();
        }

        private void OnMouseDown() => TriggerAction();

        private void TriggerAction()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (actionCode == "M" && n == "ForgeBayLandingHero") mb.gameObject.SetActive(false);
                else if (actionCode == "B" && n == "PocketDimensionBay") mb.SendMessage("ToggleCatalog", SendMessageOptions.DontRequireReceiver);
                else if (actionCode == "W" && n == "TinkercadWireSystem") mb.SendMessage("ToggleWireMode", SendMessageOptions.DontRequireReceiver);
                else if (actionCode == "I" && (n.Contains("IDE") || n.Contains("Firmware"))) mb.SendMessage("ToggleIDE", SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
