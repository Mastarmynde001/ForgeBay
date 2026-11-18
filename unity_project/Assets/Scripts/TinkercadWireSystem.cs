using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class TinkercadWireSystem : MonoBehaviour
{
    private class HardwarePin
    {
        public string pinName;
        public string electricalRole; // "VCC", "GND", "SIG"
        public int gpioNumber;
        public Transform ownerPart;
        public Transform movableMesh;
        public Renderer meshRenderer;
        public Vector3 localOffsetFrac;
    }

    private class ComponentPinout
    {
        public Transform ownerPart;
        public Transform movableMesh;
        public Renderer meshRenderer;
        public string displayName;
        public string categoryTag;
        public bool isMechanicalOnly;
        public List<HardwarePin> pins = new List<HardwarePin>();
    }

    private class JumperWire
    {
        public GameObject wireObj;
        public GameObject startBoot;
        public GameObject endBoot;
        public LineRenderer line;
        public HardwarePin startPin;
        public HardwarePin endPin;
        public Color color;
    }

    private class WiredActuator
    {
        public Transform target;
        public bool isServoOrArm;
        public int boundGpioPin;
        public Vector3 baseLocalEuler;
    }

    private readonly List<ComponentPinout> boardPinouts = new List<ComponentPinout>();
    private readonly List<JumperWire> wires = new List<JumperWire>();
    private readonly List<WiredActuator> closedLoopActuators = new List<WiredActuator>();

    private bool wireMode = false;
    public bool IsWireModeActive => wireMode;
    private bool manualColorLocked = false;
    private bool hasShortCircuit = false;
    private HardwarePin pendingStartPin = null;
    private ComponentPinout inspectedBoard = null;

    private LineRenderer previewLine;
    private Material wireMaterial;
    private Material bootMaterial;
    private GUIStyle pinHeaderStyle;
    private GUIStyle pinBtnStyle;
    private Texture2D darkHudTex;

    private readonly Color[] wirePalette = new Color[]
    {
        new Color(0.92f, 0.16f, 0.16f), // 0: Red (5V / VCC / VMOT)
        new Color(0.12f, 0.13f, 0.15f), // 1: Black (GND)
        new Color(0.95f, 0.78f, 0.10f), // 2: Yellow (PWM / D9 / SIG)
        new Color(0.10f, 0.80f, 0.35f), // 3: Green (STEP / DATA / SDA)
        new Color(0.14f, 0.55f, 0.98f)  // 4: Blue (ECHO / DIR / SCL)
    };
    private int colorIndex = 0;
    private string statusBanner = "Wire Tool [W]: OFF | All 32 Electronics & Robotics Modules Mapped to Datasheet Pinouts";

    private void Start()
    {
        CreateMaterials();
        CreatePreviewLine();
        BuildAuthenticPinouts("Bay1_Electronics", "ElectronicPart");
        BuildAuthenticPinouts("Bay2_Robotics", "RoboticsPart");
    }

    private void CreateMaterials()
    {
        bool isURP = GraphicsSettings.defaultRenderPipeline != null;
        Shader unlit = isURP
            ? (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"))
            : Shader.Find("Sprites/Default");
        wireMaterial = new Material(unlit);

        Shader lit = isURP
            ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            : Shader.Find("Standard");
        bootMaterial = new Material(lit) { color = new Color(0.08f, 0.08f, 0.10f) };

        darkHudTex = new Texture2D(1, 1);
        darkHudTex.SetPixel(0, 0, new Color(0.05f, 0.08f, 0.11f, 0.94f));
        darkHudTex.Apply();
    }

    private void CreatePreviewLine()
    {
        GameObject go = new GameObject("DuPont_Wire_Preview");
        go.transform.SetParent(transform);
        previewLine = go.AddComponent<LineRenderer>();
        previewLine.material = wireMaterial;
        previewLine.startWidth = 0.0042f;
        previewLine.endWidth = 0.0042f;
        previewLine.positionCount = 24;
        previewLine.useWorldSpace = true;
        previewLine.enabled = false;
    }

    private Transform GetComponentRoot(Transform t)
    {
        Transform p = t;
        while (p != null && p.parent != null)
        {
            string pName = p.parent.name;
            if (pName == "Real_Electronics_Lab" || pName == "Real_Robotics_Lab")
                return p;
            p = p.parent;
        }
        return t;
    }

    private void BuildAuthenticPinouts(string bayName, string partTag)
    {
        GameObject bay = GameObject.Find(bayName);
        if (bay == null) return;

        HashSet<Transform> processed = new HashSet<Transform>();
        foreach (Transform t in bay.GetComponentsInChildren<Transform>(true))
        {
            if (!t.CompareTag(partTag)) continue;
            Transform root = GetComponentRoot(t);
            if (processed.Contains(root)) continue;
            processed.Add(root);

            MeshRenderer mr = root.GetComponentInChildren<MeshRenderer>();
            if (mr == null) continue;

            ComponentPinout cp = new ComponentPinout
            {
                ownerPart = root,
                movableMesh = mr.transform,
                meshRenderer = mr,
                displayName = root.name.Replace("Electrical_", "").Replace("Rob_", "").Replace("_", " ")
            };

            AssignCompleteSilkscreenPins(cp);
            boardPinouts.Add(cp);
        }
    }

    private void AssignCompleteSilkscreenPins(ComponentPinout cp)
    {
        string low = cp.ownerPart.name.ToLower();

        // 1. Mechanical Coupling & Gear Accessories (No Electrical Pins)
        if (low.Contains("horn") || low.Contains("linkage") || low.Contains("spur_gear") || low.Contains("gear_"))
        {
            cp.isMechanicalOnly = true;
            cp.categoryTag = "Mechanical Transmission / Linkage Sub-Assembly (No Electrical Pins)";
            return;
        }

        // 2. Arduino Nano & Arduino Nano Expansion Shield (Full 24-Pin Header)
        if (low.Contains("nano") && low.Contains("arduino"))
        {
            cp.categoryTag = low.Contains("expansion")
                ? "Arduino Nano I/O Expansion Shield (24-Pin Sensor/Servo Bus)"
                : "Arduino Nano CH340 ATmega328P (24-Pin Complete Header)";
            AddPin(cp, "TX1",      "SIG",  1, new Vector3(-0.44f, 0.5f, -0.36f));
            AddPin(cp, "RX0",      "SIG",  0, new Vector3(-0.36f, 0.5f, -0.36f));
            AddPin(cp, "RST",      "SIG", -1, new Vector3(-0.28f, 0.5f, -0.36f));
            AddPin(cp, "GND1",     "GND", -1, new Vector3(-0.20f, 0.5f, -0.36f));
            AddPin(cp, "D2",       "SIG",  2, new Vector3(-0.12f, 0.5f, -0.36f));
            AddPin(cp, "D3~",      "SIG",  3, new Vector3(-0.04f, 0.5f, -0.36f));
            AddPin(cp, "D4",       "SIG",  4, new Vector3( 0.04f, 0.5f, -0.36f));
            AddPin(cp, "D5~",      "SIG",  5, new Vector3( 0.12f, 0.5f, -0.36f));
            AddPin(cp, "D6~",      "SIG",  6, new Vector3( 0.20f, 0.5f, -0.36f));
            AddPin(cp, "D7",       "SIG",  7, new Vector3( 0.28f, 0.5f, -0.36f));
            AddPin(cp, "D8",       "SIG",  8, new Vector3( 0.36f, 0.5f, -0.36f));
            AddPin(cp, "D9~",      "SIG",  9, new Vector3( 0.44f, 0.5f, -0.36f));

            AddPin(cp, "D10~",     "SIG", 10, new Vector3(-0.44f, 0.5f,  0.36f));
            AddPin(cp, "D11~",     "SIG", 11, new Vector3(-0.36f, 0.5f,  0.36f));
            AddPin(cp, "D12",      "SIG", 12, new Vector3(-0.28f, 0.5f,  0.36f));
            AddPin(cp, "D13",      "SIG", 13, new Vector3(-0.20f, 0.5f,  0.36f));
            AddPin(cp, "3V3",      "VCC", -1, new Vector3(-0.12f, 0.5f,  0.36f));
            AddPin(cp, "A0",       "SIG", 14, new Vector3(-0.04f, 0.5f,  0.36f));
            AddPin(cp, "A1",       "SIG", 15, new Vector3( 0.04f, 0.5f,  0.36f));
            AddPin(cp, "A2",       "SIG", 16, new Vector3( 0.12f, 0.5f,  0.36f));
            AddPin(cp, "A3",       "SIG", 17, new Vector3( 0.20f, 0.5f,  0.36f));
            AddPin(cp, "A4 (SDA)", "SIG", 18, new Vector3( 0.28f, 0.5f,  0.36f));
            AddPin(cp, "A5 (SCL)", "SIG", 19, new Vector3( 0.36f, 0.5f,  0.36f));
            AddPin(cp, "5V / VIN", "VCC", -1, new Vector3( 0.44f, 0.5f,  0.36f));
            return;
        }

        // 3. Arduino Uno R3 (Full 24-Pin Header)
        if (low.Contains("arduino"))
        {
            cp.categoryTag = "Arduino Uno R3 ATmega328P (24-Pin Complete Header)";
            AddPin(cp, "VIN",      "VCC", -1, new Vector3(-0.42f, 0.5f, -0.38f));
            AddPin(cp, "GND1",     "GND", -1, new Vector3(-0.32f, 0.5f, -0.38f));
            AddPin(cp, "GND2",     "GND", -1, new Vector3(-0.22f, 0.5f, -0.38f));
            AddPin(cp, "5V",       "VCC", -1, new Vector3(-0.12f, 0.5f, -0.38f));
            AddPin(cp, "3.3V",     "VCC", -1, new Vector3(-0.02f, 0.5f, -0.38f));
            AddPin(cp, "RESET",    "SIG", -1, new Vector3( 0.08f, 0.5f, -0.38f));

            AddPin(cp, "D2",       "SIG",  2, new Vector3(-0.42f, 0.5f,  0.38f));
            AddPin(cp, "D3~",      "SIG",  3, new Vector3(-0.34f, 0.5f,  0.38f));
            AddPin(cp, "D4",       "SIG",  4, new Vector3(-0.26f, 0.5f,  0.38f));
            AddPin(cp, "D5~",      "SIG",  5, new Vector3(-0.18f, 0.5f,  0.38f));
            AddPin(cp, "D6~",      "SIG",  6, new Vector3(-0.10f, 0.5f,  0.38f));
            AddPin(cp, "D7",       "SIG",  7, new Vector3(-0.02f, 0.5f,  0.38f));
            AddPin(cp, "D8",       "SIG",  8, new Vector3( 0.06f, 0.5f,  0.38f));
            AddPin(cp, "D9~",      "SIG",  9, new Vector3( 0.14f, 0.5f,  0.38f));
            AddPin(cp, "D10~",     "SIG", 10, new Vector3( 0.22f, 0.5f,  0.38f));
            AddPin(cp, "D11~",     "SIG", 11, new Vector3( 0.30f, 0.5f,  0.38f));
            AddPin(cp, "D12",      "SIG", 12, new Vector3( 0.38f, 0.5f,  0.38f));
            AddPin(cp, "D13",      "SIG", 13, new Vector3( 0.44f, 0.5f,  0.38f));

            AddPin(cp, "A0",       "SIG", 14, new Vector3( 0.16f, 0.5f, -0.38f));
            AddPin(cp, "A1",       "SIG", 15, new Vector3( 0.22f, 0.5f, -0.38f));
            AddPin(cp, "A2",       "SIG", 16, new Vector3( 0.28f, 0.5f, -0.38f));
            AddPin(cp, "A3",       "SIG", 17, new Vector3( 0.34f, 0.5f, -0.38f));
            AddPin(cp, "A4 (SDA)", "SIG", 18, new Vector3( 0.40f, 0.5f, -0.38f));
            AddPin(cp, "A5 (SCL)", "SIG", 19, new Vector3( 0.45f, 0.5f, -0.38f));
            return;
        }

        // 4. ESP32-S3 MINI Nano (N16R8) (16-Pin GPIO)
        if (low.Contains("esp32"))
        {
            cp.categoryTag = "ESP32-S3 MINI Nano N16R8 (16-Pin Complete Header)";
            AddPin(cp, "3V3",        "VCC", -1, new Vector3(-0.42f, 0.5f, -0.36f));
            AddPin(cp, "5V (VBUS)",  "VCC", -1, new Vector3(-0.30f, 0.5f, -0.36f));
            AddPin(cp, "GND1",       "GND", -1, new Vector3(-0.18f, 0.5f, -0.36f));
            AddPin(cp, "GND2",       "GND", -1, new Vector3(-0.06f, 0.5f, -0.36f));
            AddPin(cp, "GPIO2",      "SIG",  2, new Vector3( 0.06f, 0.5f, -0.36f));
            AddPin(cp, "GPIO4",      "SIG",  4, new Vector3( 0.18f, 0.5f, -0.36f));
            AddPin(cp, "GPIO5",      "SIG",  5, new Vector3( 0.30f, 0.5f, -0.36f));
            AddPin(cp, "GPIO8 (SDA)","SIG",  8, new Vector3( 0.42f, 0.5f, -0.36f));
            AddPin(cp, "GPIO9 (SCL)","SIG",  9, new Vector3(-0.42f, 0.5f,  0.36f));
            AddPin(cp, "GPIO10",     "SIG", 10, new Vector3(-0.30f, 0.5f,  0.36f));
            AddPin(cp, "GPIO11",     "SIG", 11, new Vector3(-0.18f, 0.5f,  0.36f));
            AddPin(cp, "GPIO12",     "SIG", 12, new Vector3(-0.06f, 0.5f,  0.36f));
            AddPin(cp, "GPIO13",     "SIG", 13, new Vector3( 0.06f, 0.5f,  0.36f));
            AddPin(cp, "GPIO14",     "SIG", 14, new Vector3( 0.18f, 0.5f,  0.36f));
            AddPin(cp, "TX (GPIO43)","SIG", 43, new Vector3( 0.30f, 0.5f,  0.36f));
            AddPin(cp, "RX (GPIO44)","SIG", 44, new Vector3( 0.42f, 0.5f,  0.36f));
            return;
        }

        // 5. Raspberry Pi Pico 2 (RP2350) (16-Pin Header)
        if (low.Contains("pico") || low.Contains("raspberry"))
        {
            cp.categoryTag = "Raspberry Pi Pico 2 RP2350 (16-Pin Complete Header)";
            AddPin(cp, "VBUS (5V)",  "VCC", -1, new Vector3(-0.42f, 0.5f, -0.36f));
            AddPin(cp, "VSYS",       "VCC", -1, new Vector3(-0.30f, 0.5f, -0.36f));
            AddPin(cp, "3V3 OUT",    "VCC", -1, new Vector3(-0.18f, 0.5f, -0.36f));
            AddPin(cp, "GND1",       "GND", -1, new Vector3(-0.06f, 0.5f, -0.36f));
            AddPin(cp, "GND2",       "GND", -1, new Vector3( 0.06f, 0.5f, -0.36f));
            AddPin(cp, "GP0 (TX)",   "SIG",  0, new Vector3( 0.18f, 0.5f, -0.36f));
            AddPin(cp, "GP1 (RX)",   "SIG",  1, new Vector3( 0.30f, 0.5f, -0.36f));
            AddPin(cp, "GP2",        "SIG",  2, new Vector3( 0.42f, 0.5f, -0.36f));
            AddPin(cp, "GP4 (SDA)",  "SIG",  4, new Vector3(-0.42f, 0.5f,  0.36f));
            AddPin(cp, "GP5 (SCL)",  "SIG",  5, new Vector3(-0.30f, 0.5f,  0.36f));
            AddPin(cp, "GP9 (PWM)",  "SIG",  9, new Vector3(-0.18f, 0.5f,  0.36f));
            AddPin(cp, "GP14",       "SIG", 14, new Vector3(-0.06f, 0.5f,  0.36f));
            AddPin(cp, "GP15",       "SIG", 15, new Vector3( 0.06f, 0.5f,  0.36f));
            AddPin(cp, "GP26 (A0)",  "SIG", 26, new Vector3( 0.18f, 0.5f,  0.36f));
            AddPin(cp, "GP27 (A1)",  "SIG", 27, new Vector3( 0.30f, 0.5f,  0.36f));
            AddPin(cp, "RUN",        "SIG", -1, new Vector3( 0.42f, 0.5f,  0.36f));
            return;
        }

        // 6. L293D Dual H-Bridge IC (16-Pin DIP)
        if (low.Contains("l293d"))
        {
            cp.categoryTag = "L293D Quadruple Half-H Driver (16-Pin DIP)";
            AddPin(cp, "1: 1,2EN",     "SIG", -1, new Vector3(-0.42f, 0.5f, -0.35f));
            AddPin(cp, "2: 1A (IN1)",  "SIG", -1, new Vector3(-0.30f, 0.5f, -0.35f));
            AddPin(cp, "3: 1Y (OUT1)", "SIG", -1, new Vector3(-0.18f, 0.5f, -0.35f));
            AddPin(cp, "4: GND1",      "GND", -1, new Vector3(-0.06f, 0.5f, -0.35f));
            AddPin(cp, "5: GND2",      "GND", -1, new Vector3( 0.06f, 0.5f, -0.35f));
            AddPin(cp, "6: 2Y (OUT2)", "SIG", -1, new Vector3( 0.18f, 0.5f, -0.35f));
            AddPin(cp, "7: 2A (IN2)",  "SIG", -1, new Vector3( 0.30f, 0.5f, -0.35f));
            AddPin(cp, "8: VCC2(VMOT)","VCC", -1, new Vector3( 0.42f, 0.5f, -0.35f));

            AddPin(cp, "9: 3,4EN",     "SIG", -1, new Vector3(-0.42f, 0.5f,  0.35f));
            AddPin(cp, "10: 3A (IN3)", "SIG", -1, new Vector3(-0.30f, 0.5f,  0.35f));
            AddPin(cp, "11: 3Y (OUT3)","SIG", -1, new Vector3(-0.18f, 0.5f,  0.35f));
            AddPin(cp, "12: GND3",     "GND", -1, new Vector3(-0.06f, 0.5f,  0.35f));
            AddPin(cp, "13: GND4",     "GND", -1, new Vector3( 0.06f, 0.5f,  0.35f));
            AddPin(cp, "14: 4Y (OUT4)","SIG", -1, new Vector3( 0.18f, 0.5f,  0.35f));
            AddPin(cp, "15: 4A (IN4)", "SIG", -1, new Vector3( 0.30f, 0.5f,  0.35f));
            AddPin(cp, "16: VCC1 (5V)","VCC", -1, new Vector3( 0.42f, 0.5f,  0.35f));
            return;
        }

        // 7. PCA9685 16-Channel PWM Servo Controller Board
        if (low.Contains("pca9685"))
        {
            cp.categoryTag = "PCA9685 16-Ch 12-Bit I2C PWM Servo Driver";
            AddPin(cp, "VCC (Logic)", "VCC", -1, new Vector3(-0.42f, 0.5f, -0.35f));
            AddPin(cp, "GND",         "GND", -1, new Vector3(-0.26f, 0.5f, -0.35f));
            AddPin(cp, "SDA",         "SIG", 18, new Vector3(-0.10f, 0.5f, -0.35f));
            AddPin(cp, "SCL",         "SIG", 19, new Vector3( 0.06f, 0.5f, -0.35f));
            AddPin(cp, "OE (Enable)", "SIG", -1, new Vector3( 0.22f, 0.5f, -0.35f));
            AddPin(cp, "V+ (Servo)",  "VCC", -1, new Vector3( 0.38f, 0.5f, -0.35f));
            AddPin(cp, "CH0 PWM",     "SIG",  0, new Vector3(-0.42f, 0.5f,  0.35f));
            AddPin(cp, "CH1 PWM",     "SIG",  1, new Vector3(-0.26f, 0.5f,  0.35f));
            AddPin(cp, "CH2 PWM",     "SIG",  2, new Vector3(-0.10f, 0.5f,  0.35f));
            AddPin(cp, "CH3 PWM",     "SIG",  3, new Vector3( 0.06f, 0.5f,  0.35f));
            AddPin(cp, "CH4 PWM",     "SIG",  4, new Vector3( 0.22f, 0.5f,  0.35f));
            AddPin(cp, "CH5 PWM",     "SIG",  5, new Vector3( 0.38f, 0.5f,  0.35f));
            return;
        }

        // 8. Adafruit DS3231 Precision RTC STEMMA QT Module
        if (low.Contains("ds3231") || low.Contains("rtc"))
        {
            cp.categoryTag = "Adafruit DS3231 Precision I2C RTC Module (8-Pin)";
            AddPin(cp, "VIN (3-5V)", "VCC", -1, new Vector3(-0.42f, 0.5f, 0f));
            AddPin(cp, "GND",        "GND", -1, new Vector3(-0.30f, 0.5f, 0f));
            AddPin(cp, "SCL",        "SIG", 19, new Vector3(-0.18f, 0.5f, 0f));
            AddPin(cp, "SDA",        "SIG", 18, new Vector3(-0.06f, 0.5f, 0f));
            AddPin(cp, "VBAT",       "VCC", -1, new Vector3( 0.06f, 0.5f, 0f));
            AddPin(cp, "32K OUT",    "SIG", -1, new Vector3( 0.18f, 0.5f, 0f));
            AddPin(cp, "SQW / INT",  "SIG", -1, new Vector3( 0.30f, 0.5f, 0f));
            AddPin(cp, "RST",        "SIG", -1, new Vector3( 0.42f, 0.5f, 0f));
            return;
        }

        // 9. GY-63 MS5611 Barometric Pressure & Altimeter Sensor
        if (low.Contains("ms5611") || low.Contains("gy-63") || low.Contains("pressure"))
        {
            cp.categoryTag = "GY-63 MS5611 High-Res Barometric Pressure Sensor (7-Pin)";
            AddPin(cp, "VCC (3-5V)", "VCC", -1, new Vector3(-0.42f, 0.5f, 0f));
            AddPin(cp, "GND",        "GND", -1, new Vector3(-0.28f, 0.5f, 0f));
            AddPin(cp, "SCL (SCLK)", "SIG", 19, new Vector3(-0.14f, 0.5f, 0f));
            AddPin(cp, "SDA (SDI)",  "SIG", 18, new Vector3( 0.00f, 0.5f, 0f));
            AddPin(cp, "CSB",        "SIG", -1, new Vector3( 0.14f, 0.5f, 0f));
            AddPin(cp, "SDO",        "SIG", -1, new Vector3( 0.28f, 0.5f, 0f));
            AddPin(cp, "PS (I2C/SPI)","SIG",-1, new Vector3( 0.42f, 0.5f, 0f));
            return;
        }

        // 10. HC-SR04 Ultrasonic Sensor
        if (low.Contains("ultrasonic") || low.Contains("hc-sr04"))
        {
            cp.categoryTag = "HC-SR04 Ultrasonic Distance Sensor (4-Pin)";
            AddPin(cp, "VCC (5V)", "VCC", -1, new Vector3(-0.33f, 0.5f, -0.30f));
            AddPin(cp, "TRIG",     "SIG", -1, new Vector3(-0.11f, 0.5f, -0.30f));
            AddPin(cp, "ECHO",     "SIG", -1, new Vector3( 0.11f, 0.5f, -0.30f));
            AddPin(cp, "GND",      "GND", -1, new Vector3( 0.33f, 0.5f, -0.30f));
            return;
        }

        // 11. Waveshare MG996R & Servo Actuator Assembly
        if (low.Contains("mg996") || low.Contains("servo_actuator") || low.Contains("servo"))
        {
            cp.categoryTag = "High-Torque PWM Servo Actuator (3-Pin JR/Futaba Header)";
            AddPin(cp, "VCC (5V-6V)", "VCC", -1, new Vector3(-0.28f, 0.5f, 0f));
            AddPin(cp, "GND",         "GND", -1, new Vector3( 0.00f, 0.5f, 0f));
            AddPin(cp, "PWM (50Hz)",  "SIG", -1, new Vector3( 0.28f, 0.5f, 0f));
            return;
        }

        // 12. NEMA-17, NEMA-23 & Bipolar Stepper Motors
        if (low.Contains("nema") || low.Contains("stepper"))
        {
            cp.categoryTag = "Bipolar Hybrid Stepper Motor (6-Pin Coil & Drive Header)";
            AddPin(cp, "VMOT (12-24V)","VCC", -1, new Vector3(-0.40f, 0.5f, 0f));
            AddPin(cp, "GND",         "GND", -1, new Vector3(-0.24f, 0.5f, 0f));
            AddPin(cp, "A+ (COIL 1A)","SIG", -1, new Vector3(-0.08f, 0.5f, 0f));
            AddPin(cp, "A- (COIL 1B)","SIG", -1, new Vector3( 0.08f, 0.5f, 0f));
            AddPin(cp, "B+ (COIL 2A)","SIG", -1, new Vector3( 0.24f, 0.5f, 0f));
            AddPin(cp, "B- (COIL 2B)","SIG", -1, new Vector3( 0.40f, 0.5f, 0f));
            return;
        }

        // 13. 120uF 450V Axial Electrolytic Capacitor
        if (low.Contains("120uf") || low.Contains("450v") || low.Contains("cap"))
        {
            cp.categoryTag = "120µF 450V Axial Electrolytic Capacitor (2-Pin Polarized)";
            AddPin(cp, "POSITIVE (+)", "VCC", -1, new Vector3(-0.30f, 0.5f, 0f));
            AddPin(cp, "NEGATIVE (-)", "GND", -1, new Vector3( 0.30f, 0.5f, 0f));
            return;
        }

        // 14. 5mm LED
        if (low.Contains("led"))
        {
            cp.categoryTag = "5mm Diffused Indicator LED (2-Pin Polarized)";
            AddPin(cp, "ANODE (+)",   "SIG", -1, new Vector3(-0.22f, 0.5f, 0f));
            AddPin(cp, "CATHODE (-)", "GND", -1, new Vector3( 0.22f, 0.5f, 0f));
            return;
        }

        // 15. Active Buzzer TMB12A05
        if (low.Contains("buzzer") || low.Contains("tmb12a05"))
        {
            cp.categoryTag = "TMB12A05 5V Active Electromagnetic Buzzer (2-Pin)";
            AddPin(cp, "VCC (+5V)", "SIG", -1, new Vector3(-0.22f, 0.5f, 0f));
            AddPin(cp, "GND (-)",   "GND", -1, new Vector3( 0.22f, 0.5f, 0f));
            return;
        }

        // 16. 1103T Tactile Switch Module
        if (low.Contains("tact") || low.Contains("switch") || low.Contains("1103t"))
        {
            cp.categoryTag = "1103T Tactile Pushbutton Switch Module (3-Pin)";
            AddPin(cp, "VCC (5V)",   "VCC", -1, new Vector3(-0.28f, 0.5f, 0f));
            AddPin(cp, "SIG (OUT)",  "SIG", -1, new Vector3( 0.00f, 0.5f, 0f));
            AddPin(cp, "GND",        "GND", -1, new Vector3( 0.28f, 0.5f, 0f));
            return;
        }

        // 17. USB LiIon / LiPoly Charger v1.2
        if (low.Contains("charger") || low.Contains("liion") || low.Contains("lipoly"))
        {
            cp.categoryTag = "USB Li-Ion / LiPo Charger v1.2 (6-Pin Power Management)";
            AddPin(cp, "USB 5V IN",  "VCC", -1, new Vector3(-0.40f, 0.5f, 0f));
            AddPin(cp, "GND1",       "GND", -1, new Vector3(-0.24f, 0.5f, 0f));
            AddPin(cp, "BAT+",       "VCC", -1, new Vector3(-0.08f, 0.5f, 0f));
            AddPin(cp, "BAT-",       "GND", -1, new Vector3( 0.08f, 0.5f, 0f));
            AddPin(cp, "SYS OUT+",   "VCC", -1, new Vector3( 0.24f, 0.5f, 0f));
            AddPin(cp, "SYS GND",    "GND", -1, new Vector3( 0.40f, 0.5f, 0f));
            return;
        }

        // 18. BlackBoard v2.0 Breadboard
        if (low.Contains("blackboard") || low.Contains("breadboard"))
        {
            cp.categoryTag = "BlackBoard v2.0 Solderless Breadboard Distribution";
            AddPin(cp, "+5V RAIL A", "VCC", -1, new Vector3(-0.40f, 0.5f, -0.38f));
            AddPin(cp, "GND RAIL A", "GND", -1, new Vector3(-0.20f, 0.5f, -0.38f));
            AddPin(cp, "BUS STRIP 1","SIG", -1, new Vector3( 0.00f, 0.5f,  0.00f));
            AddPin(cp, "BUS STRIP 2","SIG", -1, new Vector3( 0.20f, 0.5f,  0.00f));
            AddPin(cp, "BUS STRIP 3","SIG", -1, new Vector3( 0.40f, 0.5f,  0.00f));
            AddPin(cp, "+5V RAIL B", "VCC", -1, new Vector3(-0.40f, 0.5f,  0.38f));
            AddPin(cp, "GND RAIL B", "GND", -1, new Vector3(-0.20f, 0.5f,  0.38f));
            AddPin(cp, "BUS STRIP 4","SIG", -1, new Vector3( 0.00f, 0.5f,  0.22f));
            AddPin(cp, "BUS STRIP 5","SIG", -1, new Vector3( 0.20f, 0.5f,  0.22f));
            AddPin(cp, "BUS STRIP 6","SIG", -1, new Vector3( 0.40f, 0.5f,  0.22f));
            return;
        }

        // 19. UAV Flight Controller (Bay 3)
        if (low.Contains("flight"))
        {
            cp.categoryTag = "UAV Flight Controller STM32 Avionics (14-Pin Header)";
            AddPin(cp, "5V BEC",     "VCC", -1, new Vector3(-0.42f, 0.5f, -0.35f));
            AddPin(cp, "VBAT",       "VCC", -1, new Vector3(-0.28f, 0.5f, -0.35f));
            AddPin(cp, "GND1",       "GND", -1, new Vector3(-0.14f, 0.5f, -0.35f));
            AddPin(cp, "GND2",       "GND", -1, new Vector3( 0.00f, 0.5f, -0.35f));
            AddPin(cp, "TX1",        "SIG",  1, new Vector3( 0.14f, 0.5f, -0.35f));
            AddPin(cp, "RX1",        "SIG",  0, new Vector3( 0.28f, 0.5f, -0.35f));
            AddPin(cp, "I2C SDA",    "SIG", 18, new Vector3( 0.42f, 0.5f, -0.35f));
            AddPin(cp, "M1 (ESC1)",  "SIG",  3, new Vector3(-0.42f, 0.5f,  0.35f));
            AddPin(cp, "M2 (ESC2)",  "SIG",  5, new Vector3(-0.28f, 0.5f,  0.35f));
            AddPin(cp, "M3 (ESC3)",  "SIG",  6, new Vector3(-0.14f, 0.5f,  0.35f));
            AddPin(cp, "M4 (ESC4)",  "SIG",  9, new Vector3( 0.00f, 0.5f,  0.35f));
            AddPin(cp, "S1 (SERVO)", "SIG", 10, new Vector3( 0.14f, 0.5f,  0.35f));
            AddPin(cp, "S2 (GIMBAL)","SIG", 11, new Vector3( 0.28f, 0.5f,  0.35f));
            AddPin(cp, "I2C SCL",    "SIG", 19, new Vector3( 0.42f, 0.5f,  0.35f));
            return;
        }

        // 20. 3670 Brushless Motor & UAV Brushless Rotor (Bay 3)
        if (low.Contains("brushless") || low.Contains("3670") || low.Contains("rotor"))
        {
            cp.categoryTag = "3-Phase Brushless Motor & ESC Propulsion Unit (6-Pin)";
            AddPin(cp, "BEC +5V",      "VCC", -1, new Vector3(-0.40f, 0.5f, 0f));
            AddPin(cp, "GND",          "GND", -1, new Vector3(-0.24f, 0.5f, 0f));
            AddPin(cp, "THROTTLE PWM", "SIG", -1, new Vector3(-0.08f, 0.5f, 0f));
            AddPin(cp, "PHASE U (A)",  "SIG", -1, new Vector3( 0.08f, 0.5f, 0f));
            AddPin(cp, "PHASE V (B)",  "SIG", -1, new Vector3( 0.24f, 0.5f, 0f));
            AddPin(cp, "PHASE W (C)",  "SIG", -1, new Vector3( 0.40f, 0.5f, 0f));
            return;
        }

        // 21. Robotic Gripper (Bay 3)
        if (low.Contains("gripper"))
        {
            cp.categoryTag = "Parallel End-Effector Servo Gripper (4-Pin)";
            AddPin(cp, "VCC (5V-6V)", "VCC", -1, new Vector3(-0.33f, 0.5f, 0f));
            AddPin(cp, "GND",         "GND", -1, new Vector3(-0.11f, 0.5f, 0f));
            AddPin(cp, "GRIP PWM",    "SIG", -1, new Vector3( 0.11f, 0.5f, 0f));
            AddPin(cp, "FORCE SENSE", "SIG", -1, new Vector3( 0.33f, 0.5f, 0f));
            return;
        }

        // 22. Multi-DOF Robotic Arm (Bay 3)
        if (low.Contains("robotic_arm") || low.Contains("arm"))
        {
            cp.categoryTag = "Articulated Multi-DOF Robotic Manipulator (8-Pin Bus)";
            AddPin(cp, "VCC (6V)",     "VCC", -1, new Vector3(-0.42f, 0.5f, -0.25f));
            AddPin(cp, "GND",          "GND", -1, new Vector3(-0.14f, 0.5f, -0.25f));
            AddPin(cp, "BASE PWM",     "SIG", -1, new Vector3( 0.14f, 0.5f, -0.25f));
            AddPin(cp, "SHOULDER PWM", "SIG", -1, new Vector3( 0.42f, 0.5f, -0.25f));
            AddPin(cp, "ELBOW PWM",    "SIG", -1, new Vector3(-0.42f, 0.5f,  0.25f));
            AddPin(cp, "WRIST PWM",    "SIG", -1, new Vector3(-0.14f, 0.5f,  0.25f));
            AddPin(cp, "GRIPPER PWM",  "SIG", -1, new Vector3( 0.14f, 0.5f,  0.25f));
            AddPin(cp, "ENCODER FB",   "SIG", -1, new Vector3( 0.42f, 0.5f,  0.25f));
            return;
        }

        cp.categoryTag = "Mechatronic Module (4-Pin)";
        AddPin(cp, "VCC",  "VCC", -1, new Vector3(-0.30f, 0.5f, 0f));
        AddPin(cp, "GND",  "GND", -1, new Vector3(-0.10f, 0.5f, 0f));
        AddPin(cp, "SIG1", "SIG", -1, new Vector3( 0.10f, 0.5f, 0f));
        AddPin(cp, "SIG2", "SIG", -1, new Vector3( 0.30f, 0.5f, 0f));
    }

    private void AddPin(ComponentPinout cp, string name, string role, int gpio, Vector3 fracOffset)
    {
        cp.pins.Add(new HardwarePin
        {
            pinName = name,
            electricalRole = role,
            gpioNumber = gpio,
            ownerPart = cp.ownerPart,
            movableMesh = cp.movableMesh,
            meshRenderer = cp.meshRenderer,
            localOffsetFrac = fracOffset
        });
    }

    private Vector3 GetPinWorldPosition(HardwarePin pin)
    {
        if (pin == null || pin.meshRenderer == null) return Vector3.zero;
        Bounds b = pin.meshRenderer.bounds;
        return new Vector3(
            b.center.x + (pin.localOffsetFrac.x * b.size.x),
            b.max.y + 0.002f,
            b.center.z + (pin.localOffsetFrac.z * b.size.z)
        );
    }

    private System.Collections.IEnumerator BuildDefaultJudgeShowcase()
    {
        yield return new WaitForSeconds(0.25f);
        ComponentPinout uno = boardPinouts.Find(b => b.ownerPart != null && b.ownerPart.name.ToLower().Contains("arduino_uno"));
        ComponentPinout servo = boardPinouts.Find(b => b.ownerPart != null && b.ownerPart.name.ToLower().Contains("mg996r"));
        if (uno != null && servo != null)
        {
            HardwarePin u5v = uno.pins.Find(p => p.pinName == "5V");
            HardwarePin ugnd = uno.pins.Find(p => p.pinName == "GND");
            HardwarePin ud9 = uno.pins.Find(p => p.pinName.StartsWith("D9"));

            HardwarePin svcc = servo.pins.Find(p => p.electricalRole == "VCC");
            HardwarePin sgnd = servo.pins.Find(p => p.electricalRole == "GND");
            HardwarePin spwm = servo.pins.Find(p => p.electricalRole == "SIG");

            if (u5v != null && svcc != null) { pendingStartPin = u5v; OnPinButtonClicked(svcc); }
            if (ugnd != null && sgnd != null) { pendingStartPin = ugnd; OnPinButtonClicked(sgnd); }
            if (ud9 != null && spwm != null) { pendingStartPin = ud9; OnPinButtonClicked(spwm); }
        }

        McuVirtualMachine vm = GetComponent<McuVirtualMachine>();
        if (vm != null && !vm.IsFlashed)
        {
            string demoSketch =
                "#include <Servo.h>\n" +
                "Servo gateServo;\n" +
                "void setup() { gateServo.attach(9); }\n" +
                "void loop() { float d = readUltrasonic(); if (d < 30) { gateServo.write(150); } else { gateServo.write(30); } }";
            vm.CompileAndFlash(demoSketch, false, out _);
            RefreshStatusBanner();
        StartCoroutine(BuildDefaultJudgeShowcase());
        }
    }

    private void Update()
    {
        bool typingInIde = GUIUtility.keyboardControl != 0;

        if (!typingInIde && Input.GetKeyDown(KeyCode.W))
        {
            wireMode = !wireMode;
            pendingStartPin = null;
            manualColorLocked = false;
            previewLine.enabled = false;
            if (!wireMode) inspectedBoard = null;
            RefreshStatusBanner();
        }

        if (!typingInIde && wireMode && Input.GetKeyDown(KeyCode.C))
        {
            colorIndex = (colorIndex + 1) % wirePalette.Length;
            manualColorLocked = true;
            statusBanner = $"DuPont Wire Insulation: {GetColorName()}";
        }

        if (!typingInIde && Input.GetKeyDown(KeyCode.Z))
            UndoLastWire();

        if (wireMode && Input.GetMouseButtonDown(1))
            UndoLastWire();

        if (!typingInIde && Input.GetKeyDown(KeyCode.X) && (wires.Count > 0 || pendingStartPin != null))
            ClearAllWires();

        // Click-locked component pinout selection (prevents accidental hover switching while routing wires)
        if (wireMode && Input.GetMouseButtonDown(0) && Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 25f))
            {
                Transform root = GetComponentRoot(hit.transform);
                ComponentPinout found = boardPinouts.Find(b => b.ownerPart == root);
                if (found != null)
                {
                    inspectedBoard = found;
                    statusBanner = $"Selected {found.displayName} — Click a pin in its datasheet header below";
                }
            }
        }

        if (wireMode && pendingStartPin != null && Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 hoverPoint = Physics.Raycast(ray, out RaycastHit hit, 25f) ? hit.point : ray.GetPoint(1.5f);

            previewLine.enabled = true;
            Color c = wirePalette[colorIndex];
            previewLine.startColor = c;
            previewLine.endColor = c;
            UpdateBezierCurve(previewLine, GetPinWorldPosition(pendingStartPin), hoverPoint);
        }
    }

    private Transform CreateActuatorSpinner(Transform partRoot, Renderer bodyRenderer, bool isServoOrArm)
    {
        Transform existing = partRoot.Find("Actuator_Spinner_Pivot");
        if (existing != null) return existing;

        Bounds b = bodyRenderer != null ? bodyRenderer.bounds : new Bounds(partRoot.position, Vector3.one * 0.05f);
        Vector3 topCenter = new Vector3(b.center.x, b.max.y + 0.001f, b.center.z);

        GameObject pivot = new GameObject("Actuator_Spinner_Pivot");
        pivot.transform.SetParent(partRoot, true);
        pivot.transform.position = topCenter;
        pivot.transform.rotation = Quaternion.identity;

        bool isURP = GraphicsSettings.defaultRenderPipeline != null;
        Shader lit = isURP
            ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            : Shader.Find("Standard");

        if (isServoOrArm)
        {
            // Anodized Servo Output Spline + Horn Arm
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "Servo_Spline_Hub";
            hub.transform.SetParent(pivot.transform, false);
            hub.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            hub.transform.localScale = new Vector3(0.012f, 0.004f, 0.012f);
            if (hub.TryGetComponent<Collider>(out Collider c1)) Destroy(c1);
            if (hub.TryGetComponent<Renderer>(out Renderer r1))
                r1.material = new Material(lit) { color = new Color(0.85f, 0.87f, 0.90f) };

            GameObject horn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            horn.name = "Servo_Horn_Arm";
            horn.transform.SetParent(pivot.transform, false);
            horn.transform.localPosition = new Vector3(0.016f, 0.007f, 0f);
            horn.transform.localScale = new Vector3(0.034f, 0.0035f, 0.008f);
            if (horn.TryGetComponent<Collider>(out Collider c2)) Destroy(c2);
            if (horn.TryGetComponent<Renderer>(out Renderer r2))
                r2.material = new Material(lit) { color = new Color(0.95f, 0.25f, 0.15f) };
        }
        else
        {
            // Chrome Stepper / Brushless Drive Shaft + High-Contrast Rotor Flag
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Motor_Drive_Shaft";
            shaft.transform.SetParent(pivot.transform, false);
            shaft.transform.localPosition = new Vector3(0f, 0.010f, 0f);
            shaft.transform.localScale = new Vector3(0.007f, 0.010f, 0.007f);
            if (shaft.TryGetComponent<Collider>(out Collider c1)) Destroy(c1);
            if (shaft.TryGetComponent<Renderer>(out Renderer r1))
                r1.material = new Material(lit) { color = new Color(0.82f, 0.85f, 0.88f) };

            GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "Shaft_Keyway_Indicator";
            flag.transform.SetParent(pivot.transform, false);
            flag.transform.localPosition = new Vector3(0.009f, 0.017f, 0f);
            flag.transform.localScale = new Vector3(0.018f, 0.004f, 0.004f);
            if (flag.TryGetComponent<Collider>(out Collider c2)) Destroy(c2);
            if (flag.TryGetComponent<Renderer>(out Renderer r2))
                r2.material = new Material(lit) { color = new Color(0.15f, 0.85f, 0.95f) };
        }

        return pivot.transform;
    }

    private void LateUpdate()
    {
        McuVirtualMachine mcuVm = GetComponent<McuVirtualMachine>();

        if (!hasShortCircuit && mcuVm != null && mcuVm.IsFlashed && mcuVm.IsRunning)
        {
            foreach (WiredActuator act in closedLoopActuators)
            {
                if (act.target == null) continue;
                int gpio = act.boundGpioPin > 0 ? act.boundGpioPin : (act.isServoOrArm ? 9 : 5);

                if (act.isServoOrArm)
                {
                    float targetDeg = mcuVm.GetServoTargetAngle(gpio);
                    if (targetDeg <= 0.01f) targetDeg = mcuVm.GetServoTargetAngle(9);
                    float yawDeg = targetDeg - 90f;
                    Quaternion targetRot = Quaternion.Euler(0f, yawDeg, 0f);
                    act.target.localRotation = Quaternion.Slerp(act.target.localRotation, targetRot, Time.deltaTime * 12f);
                }
                else
                {
                    float vel = mcuVm.GetMotorAngularVelocity(gpio);
                    if (Mathf.Abs(vel) <= 0.01f) vel = mcuVm.GetMotorAngularVelocity(5);
                    if (Mathf.Abs(vel) > 0.01f)
                        act.target.Rotate(Vector3.up, vel * Time.deltaTime, Space.World);
                }
            }
        }

        foreach (JumperWire w in wires)
        {
            Vector3 p0 = GetPinWorldPosition(w.startPin);
            Vector3 p3 = GetPinWorldPosition(w.endPin);
            UpdateBezierCurve(w.line, p0, p3);

            if (w.startBoot != null) w.startBoot.transform.position = p0 + Vector3.up * 0.006f;
            if (w.endBoot != null) w.endBoot.transform.position = p3 + Vector3.up * 0.006f;
        }
    }

    private void OnPinButtonClicked(HardwarePin clickedPin)
    {
        if (pendingStartPin == null)
        {
            pendingStartPin = clickedPin;
            if (!manualColorLocked)
            {
                if (clickedPin.electricalRole == "VCC") colorIndex = 0;
                else if (clickedPin.electricalRole == "GND") colorIndex = 1;
                else colorIndex = 2;
            }
            statusBanner = $"Plugged {GetColorName()} DuPont lead into {clickedPin.ownerPart.name} [{clickedPin.pinName}] — Hover target part & click destination pin!";
        }
        else if (clickedPin != pendingStartPin)
        {
            CreateDuPontWire(pendingStartPin, clickedPin, wirePalette[colorIndex]);
            pendingStartPin = null;
            manualColorLocked = false;
            previewLine.enabled = false;
        }
    }

    private void CreateDuPontWire(HardwarePin a, HardwarePin b, Color wireColor)
    {
        Vector3 p0 = GetPinWorldPosition(a);
        Vector3 p3 = GetPinWorldPosition(b);

        GameObject go = new GameObject($"DuPontWire_{a.ownerPart.name}_{a.pinName}_to_{b.ownerPart.name}_{b.pinName}");
        go.transform.SetParent(transform);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.material = wireMaterial;
        lr.startColor = wireColor;
        lr.endColor = wireColor;
        lr.startWidth = 0.0045f;
        lr.endWidth = 0.0045f;
        lr.positionCount = 24;
        lr.useWorldSpace = true;
        UpdateBezierCurve(lr, p0, p3);

        GameObject bootA = CreateDuPontBoot(go.transform, p0 + Vector3.up * 0.006f);
        GameObject bootB = CreateDuPontBoot(go.transform, p3 + Vector3.up * 0.006f);

        wires.Add(new JumperWire
        {
            wireObj = go,
            startBoot = bootA,
            endBoot = bootB,
            line = lr,
            startPin = a,
            endPin = b,
            color = wireColor
        });

        RecomputeCircuitLogic();
    }

    private GameObject CreateDuPontBoot(Transform parent, Vector3 pos)
    {
        GameObject boot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boot.name = "DuPont_Connector_Housing";
        boot.transform.SetParent(parent);
        boot.transform.position = pos;
        boot.transform.localScale = new Vector3(0.0045f, 0.014f, 0.0045f);
        if (boot.TryGetComponent<Collider>(out Collider c)) Destroy(c);
        if (boot.TryGetComponent<Renderer>(out Renderer r)) r.sharedMaterial = bootMaterial;
        return boot;
    }

    private void UndoLastWire()
    {
        if (pendingStartPin != null)
        {
            pendingStartPin = null;
            manualColorLocked = false;
            previewLine.enabled = false;
            statusBanner = "Canceled DuPont wire placement.";
            return;
        }
        if (wires.Count == 0) return;

        int last = wires.Count - 1;
        if (wires[last].wireObj != null) Destroy(wires[last].wireObj);
        wires.RemoveAt(last);
        RecomputeCircuitLogic();
    }

    private void ClearAllWires()
    {
        foreach (JumperWire w in wires)
            if (w.wireObj != null) Destroy(w.wireObj);
        wires.Clear();

        foreach (WiredActuator act in closedLoopActuators)
            if (act.target != null) act.target.localEulerAngles = act.baseLocalEuler;
        closedLoopActuators.Clear();

        hasShortCircuit = false;
        pendingStartPin = null;
        manualColorLocked = false;
        previewLine.enabled = false;
        statusBanner = "All DuPont jumper wires cleared.";
    }

    private bool IsPowerOrController(Transform part)
    {
        if (part == null) return false;
        string low = part.name.ToLower();
        return low.Contains("arduino") || low.Contains("esp32") || low.Contains("pico") || low.Contains("flight") || low.Contains("charger");
    }

    private bool IsPassThroughHub(Transform part)
    {
        if (part == null) return false;
        string low = part.name.ToLower();
        return low.Contains("blackboard") || low.Contains("breadboard") || low.Contains("l293d") || low.Contains("pca9685") || low.Contains("expansion") || low.Contains("switch") || low.Contains("120uf");
    }

    private bool IsActuatorPart(Transform part, out bool isServoOrArm)
    {
        isServoOrArm = false;
        if (part == null) return false;
        string low = part.name.ToLower();
        isServoOrArm = low.Contains("servo") || low.Contains("mg996") || low.Contains("robotic_arm") || low.Contains("gripper");
        bool isMotor = !isServoOrArm && (low.Contains("stepper") || low.Contains("nema") || low.Contains("motor") || low.Contains("rotor") || low.Contains("brushless"));
        return isServoOrArm || isMotor;
    }

    private bool HasValidRailToSource(Transform startPart, string requiredRole, out int connectedMcuGpio)
    {
        connectedMcuGpio = -1;
        Queue<Transform> queue = new Queue<Transform>();
        HashSet<Transform> visited = new HashSet<Transform>();
        queue.Enqueue(startPart);
        visited.Add(startPart);

        while (queue.Count > 0)
        {
            Transform curr = queue.Dequeue();
            foreach (JumperWire w in wires)
            {
                if (w.startPin.electricalRole != requiredRole || w.endPin.electricalRole != requiredRole)
                    continue;

                Transform neighbor = null;
                HardwarePin neighborPin = null;
                if (w.startPin.ownerPart == curr) { neighbor = w.endPin.ownerPart; neighborPin = w.endPin; }
                else if (w.endPin.ownerPart == curr) { neighbor = w.startPin.ownerPart; neighborPin = w.startPin; }

                if (neighbor != null && !visited.Contains(neighbor))
                {
                    if (IsPowerOrController(neighbor))
                    {
                        if (neighborPin != null && neighborPin.gpioNumber > 0)
                            connectedMcuGpio = neighborPin.gpioNumber;
                        return true;
                    }
                    if (IsPassThroughHub(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
        return false;
    }

    public void RefreshStatusBanner() => RecomputeCircuitLogic();

    private void RecomputeCircuitLogic()
    {
        hasShortCircuit = false;
        foreach (WiredActuator act in closedLoopActuators)
            if (act.target != null) act.target.localEulerAngles = act.baseLocalEuler;
        closedLoopActuators.Clear();

        foreach (JumperWire w in wires)
        {
            if ((w.startPin.electricalRole == "VCC" && w.endPin.electricalRole == "GND") ||
                (w.startPin.electricalRole == "GND" && w.endPin.electricalRole == "VCC"))
            {
                hasShortCircuit = true;
            }
        }

        if (hasShortCircuit)
        {
            statusBanner = "WARNING: Direct 5V-to-GND Short Circuit! Press [Z] to undo.";
            return;
        }

        HashSet<Transform> evaluated = new HashSet<Transform>();
        string incompleteDiag = "";

        foreach (JumperWire w in wires)
        {
            foreach (HardwarePin hp in new[] { w.startPin, w.endPin })
            {
                Transform part = hp.ownerPart;
                if (part == null || evaluated.Contains(part)) continue;
                evaluated.Add(part);

                if (!IsActuatorPart(part, out bool isServoOrArm)) continue;

                bool hasVcc = HasValidRailToSource(part, "VCC", out _);
                bool hasGnd = HasValidRailToSource(part, "GND", out _);
                bool hasSig = HasValidRailToSource(part, "SIG", out int mcuPin);

                if (hasVcc && hasGnd && hasSig)
                {
                    Transform spinnerPivot = CreateActuatorSpinner(part, hp.meshRenderer, isServoOrArm);
                    closedLoopActuators.Add(new WiredActuator
                    {
                        target = spinnerPivot,
                        isServoOrArm = isServoOrArm,
                        boundGpioPin = mcuPin,
                        baseLocalEuler = Vector3.zero
                    });
                }
                else if (string.IsNullOrEmpty(incompleteDiag))
                {
                    List<string> missing = new List<string>();
                    if (!hasVcc) missing.Add("VCC/VMOT");
                    if (!hasGnd) missing.Add("GND");
                    if (!hasSig) missing.Add("PWM/COIL");
                    incompleteDiag = $"{part.name} open circuit — needs {string.Join(" + ", missing)}";
                }
            }
        }

        McuVirtualMachine mcuVm = GetComponent<McuVirtualMachine>();
        bool flashed = mcuVm != null && mcuVm.IsFlashed;

        if (closedLoopActuators.Count > 0 && !flashed)
            statusBanner = $"Circuit Closed ({closedLoopActuators.Count} actuator(s) wired) — BENIGN: Press [I] to flash MCU firmware!";
        else if (closedLoopActuators.Count > 0 && flashed)
            statusBanner = $"RUNNING ({mcuVm.ActiveLanguage}): {closedLoopActuators.Count} actuator(s) driven by live code!";
        else if (!string.IsNullOrEmpty(incompleteDiag))
            statusBanner = $"Wires: {wires.Count} | {incompleteDiag}";
        else
            statusBanner = wireMode
                ? $"Wire Tool [W]: ACTIVE — Click any floating component to lock its datasheet pinout below"
                : $"Wire Tool [W]: OFF | {wires.Count} DuPont wire(s) | [I] Firmware IDE";
    }

    private void UpdateBezierCurve(LineRenderer lr, Vector3 p0, Vector3 p3)
    {
        float dist = Vector3.Distance(p0, p3);
        float archHeight = Mathf.Clamp(dist * 0.35f, 0.04f, 0.22f);
        Vector3 p1 = p0 + Vector3.up * archHeight;
        Vector3 p2 = p3 + Vector3.up * archHeight;

        int count = lr.positionCount;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float u = 1f - t;
            lr.SetPosition(i, (u * u * u * p0) + (3f * u * u * t * p1) + (3f * u * t * t * p2) + (t * t * t * p3));
        }
    }

    private string GetColorName()
    {
        return colorIndex switch
        {
            0 => "Red (5V)",
            1 => "Black (GND)",
            2 => "Yellow (PWM)",
            3 => "Green (STEP)",
            _ => "Blue (DATA)"
        };
    }

    private void OnGUI()
    {
        GUI.depth = -70;
        if (pinHeaderStyle == null)
        {
            pinHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.35f, 1.0f, 0.62f) }
            };
        }
        if (pinBtnStyle == null)
        {
            pinBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
        }

        if (wireMode && inspectedBoard != null)
        {
            if (inspectedBoard.isMechanicalOnly)
            {
                Rect mechRect = new Rect((Screen.width - 520f) * 0.5f, Screen.height - 96f, 520f, 44f);
                GUI.DrawTexture(mechRect, darkHudTex);
                GUI.Box(mechRect, GUIContent.none);
                GUI.Label(mechRect, $"{inspectedBoard.displayName.ToUpper()} — {inspectedBoard.categoryTag}", pinHeaderStyle);
            }
            else
            {
                int totalPins = inspectedBoard.pins.Count;
                int maxCols = 8;
                int rows = Mathf.CeilToInt(totalPins / (float)maxCols);
                int colsInWideRow = Mathf.Min(totalPins, maxCols);

                float btnW = 102f;
                float btnH = 26f;
                float padX = 5f;
                float padY = 4f;

                float panelW = Mathf.Max(380f, (colsInWideRow * (btnW + padX)) + 24f);
                float panelH = 34f + (rows * (btnH + padY)) + 6f;
                Rect stripRect = new Rect((Screen.width - panelW) * 0.5f, Screen.height - 48f - panelH, panelW, panelH);

                GUI.DrawTexture(stripRect, darkHudTex);
                GUI.Box(stripRect, GUIContent.none);

                string headerText = pendingStartPin == null
                    ? $"{inspectedBoard.displayName.ToUpper()} — {inspectedBoard.categoryTag}"
                    : $"PLUGGING FROM {pendingStartPin.ownerPart.name} [{pendingStartPin.pinName}] → {inspectedBoard.displayName.ToUpper()}:";
                GUI.Label(new Rect(stripRect.x + 8f, stripRect.y + 4f, stripRect.width - 16f, 22f), headerText, pinHeaderStyle);

                for (int r = 0; r < rows; r++)
                {
                    int rowStartIdx = r * maxCols;
                    int itemsInThisRow = Mathf.Min(maxCols, totalPins - rowStartIdx);
                    float rowWidth = itemsInThisRow * (btnW + padX) - padX;
                    float startX = stripRect.x + (stripRect.width - rowWidth) * 0.5f;
                    float rowY = stripRect.y + 30f + r * (btnH + padY);

                    for (int c = 0; c < itemsInThisRow; c++)
                    {
                        HardwarePin hp = inspectedBoard.pins[rowStartIdx + c];
                        Color prevBg = GUI.backgroundColor;
                        if (hp.electricalRole == "VCC") GUI.backgroundColor = new Color(0.95f, 0.28f, 0.28f);
                        else if (hp.electricalRole == "GND") GUI.backgroundColor = new Color(0.32f, 0.36f, 0.42f);
                        else GUI.backgroundColor = new Color(0.18f, 0.85f, 0.50f);

                        if (GUI.Button(new Rect(startX + c * (btnW + padX), rowY, btnW, btnH), hp.pinName, pinBtnStyle))
                        {
                            OnPinButtonClicked(hp);
                        }
                        GUI.backgroundColor = prevBg;
                    }
                }
            }
        }

        GUIStyle barStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = hasShortCircuit ? Color.red : (wireMode ? Color.yellow : Color.white) }
        };
        GUI.Box(new Rect(14f, Screen.height - 42f, Screen.width - 28f, 28f), "  " + statusBanner, barStyle);

        if (wires.Count > 0)
        {
            McuVirtualMachine mcuVm = GetComponent<McuVirtualMachine>();
            bool flashed = mcuVm != null && mcuVm.IsFlashed;
            float currentMa = hasShortCircuit ? 2500f : (flashed ? (95f + closedLoopActuators.Count * 340f) : 35f);
            string mcuState = hasShortCircuit ? "FUSE TRIPPED" : (flashed ? mcuVm.ActiveLanguage : "BLANK (IDLE)");
            string telemetry =
                $"BUS TELEMETRY\n" +
                $"Rail: {(hasShortCircuit ? "0.0V" : "5.0V DC")}\n" +
                $"Load: {currentMa:F0} mA\n" +
                $"MCU: {mcuState}";
            GUI.Box(new Rect(Screen.width - 215f, 14f, 200f, 84f), telemetry);
        }
    }
}
