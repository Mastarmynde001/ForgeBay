using UnityEngine;

public class LiveCodeInterpreter : MonoBehaviour
{
    private bool showIde = false;
    private bool isMinimized = false;
    private bool isMaximized = false;
    private int langTab = 0; // 0 = Arduino C++, 1 = MicroPython

    private Rect winRect = new Rect(16f, 155f, 600f, 510f);
    private Rect preMaxRect;
    private Vector2 codeScrollPos = Vector2.zero;

    private bool isResizing = false;
    private Vector2 resizeStartMouse;
    private Vector2 resizeStartSize;

    private GUIStyle codeAreaStyle;
    private GUIStyle headerTitleStyle;
    private GUIStyle logStyle;
    private GUIStyle serialStyle;
    private Texture2D darkBgTex;
    private Texture2D codeBgTex;
    private Texture2D headerTex;

    private McuVirtualMachine mcuVm;

    private string arduinoCode =
@"// Closed-Loop Ultrasonic Safety Cell (Arduino C++)
#include <Servo.h>
#include <Stepper.h>

Servo gateServo;
Stepper conveyorStepper(200, 5, 6, 7, 8);

void setup() {
  Serial.begin(115200);
  gateServo.attach(9);
  conveyorStepper.setSpeed(180);
  Serial.println(""System Booted: Waiting for sensor..."");
}

void loop() {
  float distance = readUltrasonic();

  if (distance < 30) {
    // Object detected close: swing gate to 150 deg & halt conveyor
    gateServo.write(150);
    conveyorStepper.step(0);
    Serial.println(""ALERT: Object < 30cm! Gate Closed."");
  } else {
    // Path clear: open gate to 30 deg & run conveyor
    gateServo.write(30);
    conveyorStepper.step(10);
    Serial.println(""CLEAR: Conveyor Running."");
  }
  delay(50);
}";

    private string microPythonCode =
@"# Closed-Loop Actuator Firmware (MicroPython)
import time

servoWrite(9, 45)
stepper.setSpeed(240)

while True:
    dist = readUltrasonic()
    if dist < 30:
        servoWrite(9, 160)
        stepper.step(0)
        print(""PROXIMITY STOP"")
    else:
        servoWrite(9, 35)
        stepper.step(10)
        print(""NOMINAL RUN"")
    time.sleep(0.05)";

    private string compileOutput = "MCU Flash: BLANK. Wire circuit with [W], write real code here, then click Compile & Flash.";

    private void Start()
    {
        mcuVm = GetComponent<McuVirtualMachine>();
        if (mcuVm == null) mcuVm = gameObject.AddComponent<McuVirtualMachine>();

        darkBgTex = MakeTex(new Color(0.09f, 0.11f, 0.14f, 0.96f));
        codeBgTex = MakeTex(new Color(0.05f, 0.06f, 0.08f, 0.98f));
        headerTex = MakeTex(new Color(0.14f, 0.18f, 0.24f, 1.0f));
    }

    private Texture2D MakeTex(Color c)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private void InitStyles()
    {
        if (codeAreaStyle != null) return;

        codeAreaStyle = new GUIStyle(GUI.skin.textArea)
        {
            fontSize = 13,
            wordWrap = false,
            normal = { background = codeBgTex, textColor = new Color(0.86f, 0.96f, 0.90f) },
            focused = { background = codeBgTex, textColor = Color.white }
        };

        headerTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.35f, 1.0f, 0.65f) }
        };

        logStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            normal = { textColor = new Color(0.35f, 1.0f, 0.62f) }
        };

        serialStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 11,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            normal = { background = codeBgTex, textColor = new Color(0.45f, 0.92f, 1.0f) }
        };
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.I) && GUIUtility.keyboardControl == 0)
        {
            showIde = !showIde;
            if (showIde)
            {
                isMinimized = false;
                PocketDimensionBay pb = GetComponent<PocketDimensionBay>();
                if (pb != null) pb.CollapseCatalog();
            }
        }
    }

    public void DockOrCloseFromCatalog() { if (showIde) isMinimized = true; }

    private void OnGUI()
    {
        GUI.depth = -60;
        InitStyles();

        if (!showIde)
        {
            if (ForgeBayTheme.DrawPillButton(new Rect(164f, 14f, 220f, 30f), "⌨  [I] FIRMWARE IDE (C++ / PY)", false, ForgeBayTheme.AccentEmerald))
            {
                showIde = true;
                isMinimized = false;
                PocketDimensionBay pb = GetComponent<PocketDimensionBay>();
                if (pb != null) pb.CollapseCatalog();
            }
            return;
        }

        if (isMinimized)
        {
            Rect dockRect = new Rect(12f, 152f, 295f, 30f);
            GUI.Box(dockRect, GUIContent.none);
            GUI.Label(new Rect(dockRect.x + 10f, dockRect.y + 3f, 185f, 24f), "Firmware IDE (Minimized)", headerTitleStyle);
            if (GUI.Button(new Rect(dockRect.xMax - 95f, dockRect.y + 4f, 62f, 22f), "Restore"))
                isMinimized = false;
            if (GUI.Button(new Rect(dockRect.xMax - 28f, dockRect.y + 4f, 22f, 22f), "X"))
                showIde = false;
            return;
        }

        HandleResizeDrag();

        winRect.width = Mathf.Clamp(winRect.width, 420f, Screen.width - 24f);
        winRect.height = Mathf.Clamp(winRect.height, 360f, Screen.height - 65f);
        winRect.x = Mathf.Clamp(winRect.x, 8f, Screen.width - winRect.width - 8f);
        winRect.y = Mathf.Clamp(winRect.y, 8f, Screen.height - winRect.height - 48f);

        GUIStyle winStyle = new GUIStyle(GUI.skin.window)
        {
            normal = { background = darkBgTex },
            onNormal = { background = darkBgTex }
        };

        winRect = GUI.Window(9042, winRect, DrawIdeWindow, "", winStyle);
    }

    private void DrawIdeWindow(int windowID)
    {
        Rect titleBarRect = new Rect(0f, 0f, winRect.width, 30f);
        GUI.DrawTexture(titleBarRect, headerTex);
        GUI.Label(new Rect(12f, 3f, winRect.width - 110f, 24f), "ForgeBay HIL Firmware IDE — Real-Time MCU Execution", headerTitleStyle);

        float btnX = winRect.width - 84f;
        if (GUI.Button(new Rect(btnX, 4f, 24f, 22f), "_")) isMinimized = true;
        if (GUI.Button(new Rect(btnX + 27f, 4f, 24f, 22f), isMaximized ? "❐" : "□")) ToggleMaximize();
        if (GUI.Button(new Rect(btnX + 54f, 4f, 24f, 22f), "X")) showIde = false;

        // Language tabs
        if (GUI.Button(new Rect(12f, 36f, 135f, 25f), langTab == 0 ? "● Arduino C++" : "Arduino C++"))
            langTab = 0;
        if (GUI.Button(new Rect(154f, 36f, 145f, 25f), langTab == 1 ? "● MicroPython" : "MicroPython"))
            langTab = 1;

        // Virtual Sensor Stimulus Bar (tests real if/else sensor thresholds live!)
        if (mcuVm != null)
        {
            GUI.Label(new Rect(12f, 66f, 225f, 22f), $"HC-SR04 Distance: {mcuVm.VirtualUltrasonicCm:F0} cm");
            mcuVm.VirtualUltrasonicCm = GUI.HorizontalSlider(new Rect(235f, 70f, winRect.width - 250f, 18f), mcuVm.VirtualUltrasonicCm, 5f, 120f);
        }

        // Scrollable Code Editor
        float editorY = 92f;
        float footerH = 152f;
        float editorH = Mathf.Max(110f, winRect.height - editorY - footerH);
        float editorW = winRect.width - 24f;

        string activeText = (langTab == 0) ? arduinoCode : microPythonCode;
        Vector2 textSize = codeAreaStyle.CalcSize(new GUIContent(activeText));
        float innerW = Mathf.Max(editorW - 20f, textSize.x + 40f);
        float innerH = Mathf.Max(editorH - 8f, textSize.y + 40f);

        codeScrollPos = GUI.BeginScrollView(
            new Rect(12f, editorY, editorW, editorH),
            codeScrollPos,
            new Rect(0f, 0f, innerW, innerH)
        );

        string edited = GUI.TextArea(new Rect(0f, 0f, innerW, innerH), activeText, codeAreaStyle);
        if (langTab == 0) arduinoCode = edited;
        else microPythonCode = edited;

        GUI.EndScrollView();

        // Action Buttons
        float actionY = editorY + editorH + 8f;
        float halfW = (editorW - 10f) * 0.5f;

        if (ForgeBayTheme.DrawPillButton(new Rect(14f, actionY, halfW, 30f), "▶  COMPILE & FLASH TO MCU", true, ForgeBayTheme.AccentEmerald))
        {
            GUIUtility.keyboardControl = 0;
            mcuVm.CompileAndFlash(langTab == 0 ? arduinoCode : microPythonCode, langTab == 1, out string msg);
            compileOutput = msg;
            TinkercadWireSystem wireSys = FindAnyObjectByType<TinkercadWireSystem>();
            if (wireSys != null) wireSys.RefreshStatusBanner();
        }

        if (ForgeBayTheme.DrawPillButton(new Rect(14f + halfW + 10f, actionY, halfW, 30f), "■  ERASE FLASH (BENIGN STATE)", false, ForgeBayTheme.AccentCrimson))
        {
            GUIUtility.keyboardControl = 0;
            mcuVm.EraseFlash();
            compileOutput = "MCU Flash erased  •  Circuit returned to benign state.";
            TinkercadWireSystem wireSys = FindAnyObjectByType<TinkercadWireSystem>();
            if (wireSys != null) wireSys.RefreshStatusBanner();
        }

        // Compiler Diagnostic Line (Crimson on syntax error, Emerald on valid build)
        bool isErr = compileOutput.Contains("error:") || compileOutput.Contains("Error") || compileOutput.Contains("ERROR");
        Color diagColor = isErr ? ForgeBayTheme.AccentCrimson : ForgeBayTheme.AccentEmerald;
        GUI.Label(new Rect(14f, actionY + 34f, editorW, 22f),
            compileOutput, ForgeBayTheme.LabelStyle(12, diagColor, true));

        // Serial Monitor Console Card
        Rect serialRect = new Rect(14f, actionY + 58f, editorW - 22f, 56f);
        ForgeBayTheme.DrawCard(serialRect, ForgeBayTheme.BgCode, ForgeBayTheme.BorderSubtle);
        string serialText = mcuVm != null ? "SERIAL MONITOR (115200 BAUD):\n" + mcuVm.GetSerialLog() : "";
        GUI.Label(new Rect(serialRect.x + 8f, serialRect.y + 4f, serialRect.width - 16f, serialRect.height - 8f),
            serialText, ForgeBayTheme.LabelStyle(11, ForgeBayTheme.AccentCyan));

        // Resize Grip
        GUI.Label(new Rect(winRect.width - 22f, winRect.height - 22f, 20f, 20f),
            "◢", ForgeBayTheme.LabelStyle(13, ForgeBayTheme.AccentCyan, true, TextAnchor.MiddleCenter));

        GUI.DragWindow(new Rect(0f, 0f, winRect.width - 98f, 34f));
    }

    private void ToggleMaximize()
    {
        if (!isMaximized)
        {
            preMaxRect = winRect;
            winRect = new Rect(20f, 20f, Screen.width - 40f, Screen.height - 80f);
            isMaximized = true;
        }
        else
        {
            winRect = preMaxRect;
            isMaximized = false;
        }
    }

    private void HandleResizeDrag()
    {
        Event e = Event.current;
        if (e == null) return;

        Rect gripRect = new Rect(winRect.xMax - 24f, winRect.yMax - 24f, 24f, 24f);

        if (e.type == EventType.MouseDown && e.button == 0 && gripRect.Contains(e.mousePosition))
        {
            isResizing = true;
            isMaximized = false;
            resizeStartMouse = e.mousePosition;
            resizeStartSize = new Vector2(winRect.width, winRect.height);
            e.Use();
        }
        else if (isResizing && e.type == EventType.MouseDrag)
        {
            Vector2 delta = e.mousePosition - resizeStartMouse;
            winRect.width = Mathf.Clamp(resizeStartSize.x + delta.x, 420f, Screen.width - winRect.x - 8f);
            winRect.height = Mathf.Clamp(resizeStartSize.y + delta.y, 360f, Screen.height - winRect.y - 48f);
            e.Use();
        }
        else if (isResizing && e.type == EventType.MouseUp)
        {
            isResizing = false;
            e.Use();
        }
    }
}
