using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class McuVirtualMachine : MonoBehaviour
{
    public bool IsFlashed { get; private set; } = false;
    public bool IsRunning { get; private set; } = false;
    public string ActiveLanguage { get; private set; } = "NONE";
    public float VirtualUltrasonicCm = 45f;

    private readonly Dictionary<int, float> servoTargetAngles = new Dictionary<int, float>();
    private readonly Dictionary<int, float> motorAngularVelocities = new Dictionary<int, float>();

    private string activeSourceCode = "";
    private bool isPythonMode = false;
    private int boundServoPin = 9;
    private int boundStepperPin = 5;
    private float configuredStepperSpeed = 180f;

    private string lastSerialLine = "[MCU IDLE] Flash memory empty. Awaiting compiled firmware.";
    private readonly List<string> serialHistory = new List<string>();
    private float serialTimer = 0f;

    public void CompileAndFlash(string sourceCode, bool isPython, out string compilerMessage)
    {
        EraseFlash();

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            compilerMessage = "COMPILER ERROR: Source buffer is empty.";
            AppendSerial("[ERR] Upload aborted: empty source file.");
            return;
        }

        // Strict front-end syntax & semantic verification before flashing
        string syntaxError = isPython
            ? ValidateMicroPythonSyntax(sourceCode)
            : ValidateArduinoCppSyntax(sourceCode);

        if (!string.IsNullOrEmpty(syntaxError))
        {
            IsFlashed = false;
            IsRunning = false;
            compilerMessage = syntaxError;
            AppendSerial("[COMPILE FAILED] " + syntaxError);
            return;
        }

        activeSourceCode = sourceCode;
        isPythonMode = isPython;
        ActiveLanguage = isPython ? "MicroPython" : "Arduino C++";

        ParsePinBindings(sourceCode, isPython);

        IsFlashed = true;
        IsRunning = true;

        int bytesUsed = System.Text.Encoding.UTF8.GetByteCount(sourceCode);
        compilerMessage = isPython
            ? $"✓ Syntax OK ({bytesUsed} B) — Flashed MicroPython bytecode to MCU!"
            : $"✓ avr-g++ OK ({bytesUsed} B / 32256 B flash) — Flashed binary to MCU!";

        serialHistory.Clear();
        AppendSerial($"[BOOT] {ActiveLanguage} firmware verified & flashed.");
        EvaluateProgramStep(true);
    }

    public void EraseFlash()
    {
        IsFlashed = false;
        IsRunning = false;
        ActiveLanguage = "NONE";
        servoTargetAngles.Clear();
        motorAngularVelocities.Clear();
        serialHistory.Clear();
        lastSerialLine = "[MCU ERASED] Flash cleared. All GPIO outputs Hi-Z (benign).";
    }

    public float GetServoTargetAngle(int gpioPin)
    {
        if (!IsFlashed || !IsRunning) return 0f;
        return servoTargetAngles.TryGetValue(gpioPin, out float deg) ? deg : 0f;
    }

    public float GetMotorAngularVelocity(int gpioPin)
    {
        if (!IsFlashed || !IsRunning) return 0f;
        return motorAngularVelocities.TryGetValue(gpioPin, out float vel) ? vel : 0f;
    }

    public string GetSerialLog()
    {
        return serialHistory.Count > 0 ? string.Join("\n", serialHistory) : lastSerialLine;
    }

    // =========================================================================
    // 1. STRICT ARDUINO C++ LEXER & SYNTAX VALIDATOR
    // =========================================================================
    private string ValidateArduinoCppSyntax(string src)
    {
        // Strip block comments /* ... */ while preserving line numbers
        string cleaned = Regex.Replace(src, @"/\*[\s\S]*?\*/", m =>
        {
            int newlines = m.Value.Split('\n').Length - 1;
            return new string('\n', newlines);
        });

        string[] rawLines = cleaned.Split('\n');
        int braceDepth = 0;
        int parenDepth = 0;
        int bracketDepth = 0;
        bool hasSetup = false;
        bool hasLoop = false;

        for (int i = 0; i < rawLines.Length; i++)
        {
            int lineNum = i + 1;
            string line = StripCppLineCommentAndStrings(rawLines[i], out bool unclosedString).Trim();

            if (unclosedString)
                return $"sketch.ino:{lineNum}: error: missing terminating '\"' character";

            if (string.IsNullOrEmpty(line)) continue;

            if (Regex.IsMatch(rawLines[i], @"\bvoid\s+setup\s*\(")) hasSetup = true;
            if (Regex.IsMatch(rawLines[i], @"\bvoid\s+loop\s*\(")) hasLoop = true;

            foreach (char ch in line)
            {
                if (ch == '{') braceDepth++;
                else if (ch == '}')
                {
                    braceDepth--;
                    if (braceDepth < 0)
                        return $"sketch.ino:{lineNum}: error: extraneous closing brace '}}' without matching '{{'";
                }
                else if (ch == '(') parenDepth++;
                else if (ch == ')')
                {
                    parenDepth--;
                    if (parenDepth < 0)
                        return $"sketch.ino:{lineNum}: error: extraneous closing parenthesis ')'";
                }
                else if (ch == '[') bracketDepth++;
                else if (ch == ']')
                {
                    bracketDepth--;
                    if (bracketDepth < 0)
                        return $"sketch.ino:{lineNum}: error: extraneous closing bracket ']'";
                }
            }

            // Preprocessor lines (#include, #define) do not take semicolons
            if (line.StartsWith("#")) continue;

            // Block openers/closers and control-flow headers
            if (line.EndsWith("{") || line.EndsWith("}") || line.EndsWith(":")) continue;
            if (Regex.IsMatch(line, @"^(if|else\s+if|while|for|switch)\s*\(.*\)$")) continue;
            if (line == "else") continue;

            // Function signature header whose '{' might be on the next line
            if (Regex.IsMatch(line, @"^(void|int|float|double|bool|char|long|unsigned|String)\s+[A-Za-z_]\w*\s*\([^;]*\)$"))
                continue;

            // Every other C++ statement MUST end with a semicolon ';'
            if (!line.EndsWith(";"))
            {
                return $"sketch.ino:{lineNum}: error: expected ';' at end of statement -> \"{rawLines[i].Trim()}\"";
            }
        }

        if (parenDepth != 0)
            return "sketch.ino: error: unclosed '(' parenthesis in source file";
        if (bracketDepth != 0)
            return "sketch.ino: error: unclosed '[' bracket in source file";
        if (braceDepth != 0)
            return $"sketch.ino: error: unclosed '{{' block ({braceDepth} missing '}}')";
        if (!hasSetup)
            return "sketch.ino: error: undefined reference to `setup()' — required by Arduino core";
        if (!hasLoop)
            return "sketch.ino: error: undefined reference to `loop()' — required by Arduino core";

        return null;
    }

    private string StripCppLineCommentAndStrings(string line, out bool unclosedString)
    {
        unclosedString = false;
        bool inDouble = false;
        bool inSingle = false;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            char next = (i + 1 < line.Length) ? line[i + 1] : '\0';

            if (!inDouble && !inSingle && c == '/' && next == '/')
                break;

            if (!inSingle && c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inDouble = !inDouble;
                sb.Append('"');
                continue;
            }
            if (!inDouble && c == '\'' && (i == 0 || line[i - 1] != '\\'))
            {
                inSingle = !inSingle;
                sb.Append('\'');
                continue;
            }

            if (!inDouble && !inSingle)
                sb.Append(c);
        }

        if (inDouble || inSingle) unclosedString = true;
        return sb.ToString();
    }

    // =========================================================================
    // 2. STRICT MICROPYTHON SYNTAX & INDENTATION VALIDATOR
    // =========================================================================
    private string ValidateMicroPythonSyntax(string src)
    {
        string[] rawLines = src.Split('\n');
        int parenDepth = 0;
        int bracketDepth = 0;
        Stack<int> indentStack = new Stack<int>();
        indentStack.Push(0);
        bool expectIndentedBlock = false;

        for (int i = 0; i < rawLines.Length; i++)
        {
            int lineNum = i + 1;
            string raw = rawLines[i].Replace("\t", "    ");
            string strippedCode = StripPythonCommentAndStrings(raw, out bool unclosedString);

            if (unclosedString)
                return $"main.py:{lineNum}: SyntaxError: EOL while scanning string literal";

            string trimmed = strippedCode.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            int indent = 0;
            while (indent < raw.Length && raw[indent] == ' ') indent++;

            if (expectIndentedBlock)
            {
                if (indent <= indentStack.Peek())
                    return $"main.py:{lineNum}: IndentationError: expected an indented block after ':'";
                indentStack.Push(indent);
                expectIndentedBlock = false;
            }
            else
            {
                if (indent > indentStack.Peek())
                    return $"main.py:{lineNum}: IndentationError: unexpected indent";
                while (indent < indentStack.Peek())
                    indentStack.Pop();
                if (indent != indentStack.Peek())
                    return $"main.py:{lineNum}: IndentationError: unindent does not match any outer indentation level";
            }

            foreach (char ch in trimmed)
            {
                if (ch == '(') parenDepth++;
                else if (ch == ')')
                {
                    parenDepth--;
                    if (parenDepth < 0)
                        return $"main.py:{lineNum}: SyntaxError: unmatched ')'";
                }
                else if (ch == '[') bracketDepth++;
                else if (ch == ']')
                {
                    bracketDepth--;
                    if (bracketDepth < 0)
                        return $"main.py:{lineNum}: SyntaxError: unmatched ']'";
                }
            }

            // Reject C-style braces in Python statements
            if (trimmed.Contains("{") || trimmed.Contains("}"))
                return $"main.py:{lineNum}: SyntaxError: invalid C-style '{{}}' block delimiter in Python";

            // Compound headers (if, elif, else, while, for, def, try, except) MUST end with ':'
            if (Regex.IsMatch(trimmed, @"^(if|elif|else|while|for|def|try|except)\b"))
            {
                if (!trimmed.EndsWith(":"))
                    return $"main.py:{lineNum}: SyntaxError: expected ':' at end of '{trimmed.Split(' ')[0]}' header";
                expectIndentedBlock = true;
            }
        }

        if (expectIndentedBlock)
            return "main.py: IndentationError: expected an indented block at end of file";
        if (parenDepth != 0)
            return "main.py: SyntaxError: '(' was never closed";
        if (bracketDepth != 0)
            return "main.py: SyntaxError: '[' was never closed";

        return null;
    }

    private string StripPythonCommentAndStrings(string line, out bool unclosedString)
    {
        unclosedString = false;
        bool inDouble = false;
        bool inSingle = false;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (!inDouble && !inSingle && c == '#')
                break;

            if (!inSingle && c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inDouble = !inDouble;
                sb.Append('"');
                continue;
            }
            if (!inDouble && c == '\'' && (i == 0 || line[i - 1] != '\\'))
            {
                inSingle = !inSingle;
                sb.Append('\'');
                continue;
            }

            if (!inDouble && !inSingle)
                sb.Append(c);
        }

        if (inDouble || inSingle) unclosedString = true;
        return sb.ToString();
    }

    // =========================================================================
    // 3. REAL-TIME VIRTUAL EXECUTION ENGINE
    // =========================================================================
    private void ParsePinBindings(string code, bool isPython)
    {
        boundServoPin = 9;
        boundStepperPin = 5;
        configuredStepperSpeed = isPython ? 220f : 180f;

        Match mAttach = Regex.Match(code, @"attach\s*\(\s*(\d+)\s*\)");
        if (mAttach.Success && int.TryParse(mAttach.Groups[1].Value, out int sPin))
            boundServoPin = sPin;

        Match mPyServo = Regex.Match(code, @"servoWrite\s*\(\s*(\d+)\s*,");
        if (mPyServo.Success && int.TryParse(mPyServo.Groups[1].Value, out int pyPin))
            boundServoPin = pyPin;

        Match mStepper = Regex.Match(code, @"Stepper\s+\w+\s*\(\s*\d+\s*,\s*(\d+)");
        if (mStepper.Success && int.TryParse(mStepper.Groups[1].Value, out int stPin))
            boundStepperPin = stPin;

        Match mSpeed = Regex.Match(code, @"setSpeed\s*\(\s*(\d+)\s*\)");
        if (mSpeed.Success && float.TryParse(mSpeed.Groups[1].Value, out float spd))
            configuredStepperSpeed = spd;
    }

    private void Update()
    {
        if (!IsFlashed || !IsRunning) return;

        serialTimer += Time.deltaTime;
        bool logTick = serialTimer >= 0.65f;
        if (logTick) serialTimer = 0f;

        EvaluateProgramStep(logTick);
    }

    private void EvaluateProgramStep(bool emitSerial)
    {
        float thresholdCm = 30f;
        Match mThresh = Regex.Match(activeSourceCode, @"distance\s*<\s*(\d+(\.\d+)?)|dist\s*<\s*(\d+(\.\d+)?)");
        if (mThresh.Success)
        {
            string valStr = !string.IsNullOrEmpty(mThresh.Groups[1].Value) ? mThresh.Groups[1].Value : mThresh.Groups[3].Value;
            if (float.TryParse(valStr, out float parsed)) thresholdCm = parsed;
        }

        bool obstacleDetected = VirtualUltrasonicCm < thresholdCm;

        if (obstacleDetected)
        {
            servoTargetAngles[boundServoPin] = 150f;
            motorAngularVelocities[boundStepperPin] = 0f;
            if (emitSerial)
                AppendSerial($"[{VirtualUltrasonicCm:F0}cm < {thresholdCm:F0}cm] ALERT: Gate Closed (150°) | Motor HALTED");
        }
        else
        {
            servoTargetAngles[boundServoPin] = 30f;
            motorAngularVelocities[boundStepperPin] = configuredStepperSpeed;
            if (emitSerial)
                AppendSerial($"[{VirtualUltrasonicCm:F0}cm >= {thresholdCm:F0}cm] CLEAR: Gate Open (30°) | Motor @ {configuredStepperSpeed:F0} deg/s");
        }
    }

    private void AppendSerial(string line)
    {
        lastSerialLine = line;
        serialHistory.Add(line);
        if (serialHistory.Count > 3)
            serialHistory.RemoveAt(0);
    }
}
