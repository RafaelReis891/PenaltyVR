
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class AndroidDebugConsole : MonoBehaviour
{
    public static AndroidDebugConsole Instance { get; private set; }

    [Header("Configuração")]
	[Header("Configuração")]
	[SerializeField] private bool persistBetweenScenes = true;
	[SerializeField] private int maxEntries = 500;
	[SerializeField] private bool captureLogs = true;
	private const string ConsoleEnabledKey = "AndroidDebugConsole_Enabled";
	private bool consoleEnabled;

    [Header("Interface")]
    [SerializeField] private float panelWidthPercent = 0.96f;
    [SerializeField] private float panelHeightPercent = 0.88f;
    [SerializeField] private bool showStackTraceByDefault = false;
    [SerializeField] private int fontSize = 13;

    private enum LogFilter
    {
        All,
        Log,
        Warning,
        Error,
        Exception
    }

    private class LogEntry
    {
        public string message;
        public string stackTrace;
        public string timestamp;
        public LogType type;
        public int id;
    }

    private readonly List<LogEntry> entries = new List<LogEntry>();
    private readonly Queue<LogEntry> pendingEntries = new Queue<LogEntry>();
    private readonly object logLock = new object();

    private Vector2 scrollPosition;
    private LogFilter currentFilter = LogFilter.All;

    private bool isVisible;
    private bool showStackTrace;
    private bool autoScroll = true;

    private int nextId;
    private int logCount;
    private int warningCount;
    private int errorCount;
    private int exceptionCount;

    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle entryStyle;
    private GUIStyle stackStyle;
    private GUIStyle buttonStyle;
    private GUIStyle counterStyle;
    private GUIStyle toolbarStyle;

    private bool stylesInitialized;
    private float lastScreenWidth;
    private float lastScreenHeight;

    private const int DefaultMaxEntries = 500;

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;

		if (persistBetweenScenes)
			DontDestroyOnLoad(gameObject);

		// Por padrão, o console fica desativado.
		consoleEnabled = PlayerPrefs.GetInt(ConsoleEnabledKey, 0) == 0;

		isVisible = false;
		showStackTrace = showStackTraceByDefault;

		maxEntries = Mathf.Max(50, maxEntries);
		fontSize = Mathf.Clamp(fontSize, 10, 24);

		// Só captura logs se o jogador habilitou o console.
		if (consoleEnabled && captureLogs)
			Application.logMessageReceivedThreaded += HandleLog;
	}

	private void OnDestroy()
    {
        if (captureLogs)
            Application.logMessageReceivedThreaded -= HandleLog;

        if (Instance == this)
            Instance = null;
    }

	public void SetConsoleEnabled()
	{
		consoleEnabled = !consoleEnabled;

		PlayerPrefs.SetInt(ConsoleEnabledKey, consoleEnabled ? 1 : 0);
		PlayerPrefs.Save();

		if (captureLogs)
		{
			Application.logMessageReceivedThreaded -= HandleLog;

			if (consoleEnabled)
				Application.logMessageReceivedThreaded += HandleLog;
		}

		if (!consoleEnabled)
		{
			isVisible = false;
			ClearConsole();
		}
	}

	public bool IsConsoleEnabled()
	{
		return consoleEnabled;
	}

	private void Update()
    {
        lock (logLock)
        {
            while (pendingEntries.Count > 0)
            {
                LogEntry entry = pendingEntries.Dequeue();

                entries.Add(entry);

                switch (entry.type)
                {
                    case LogType.Warning:
                        warningCount++;
                        break;

                    case LogType.Error:
                    case LogType.Assert:
                        errorCount++;
                        break;

                    case LogType.Exception:
                        exceptionCount++;
                        break;

                    default:
                        logCount++;
                        break;
                }

                while (entries.Count > maxEntries)
                {
                    RemoveOldestEntry();
                }
            }
        }
    }

    private void HandleLog(
        string condition,
        string stackTrace,
        LogType type)
    {
        LogEntry entry = new LogEntry
        {
            message = condition,
            stackTrace = stackTrace,
            type = type,
            timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
            id = System.Threading.Interlocked.Increment(ref nextId)
        };

        lock (logLock)
        {
            pendingEntries.Enqueue(entry);

            // Protege a memória caso uma grande quantidade
            // de logs seja produzida em um intervalo curto.
            int queueLimit = Mathf.Max(maxEntries * 2, 1000);

            while (pendingEntries.Count > queueLimit)
                pendingEntries.Dequeue();
        }
    }

    private void RemoveOldestEntry()
    {
        if (entries.Count == 0)
            return;

        LogEntry entry = entries[0];
        entries.RemoveAt(0);

        switch (entry.type)
        {
            case LogType.Warning:
                warningCount = Mathf.Max(0, warningCount - 1);
                break;

            case LogType.Error:
            case LogType.Assert:
                errorCount = Mathf.Max(0, errorCount - 1);
                break;

            case LogType.Exception:
                exceptionCount = Mathf.Max(0, exceptionCount - 1);
                break;

            default:
                logCount = Mathf.Max(0, logCount - 1);
                break;
        }
    }

    private void ClearConsole()
    {
        lock (logLock)
        {
            entries.Clear();
            pendingEntries.Clear();

            logCount = 0;
            warningCount = 0;
            errorCount = 0;
            exceptionCount = 0;
        }

        scrollPosition = Vector2.zero;
    }

    private void CopyConsole()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("=== UNITY DEBUG CONSOLE ===");
        builder.AppendLine("Data: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Dispositivo: " + SystemInfo.deviceModel);
        builder.AppendLine("Sistema: " + SystemInfo.operatingSystem);
        builder.AppendLine("Unity: " + Application.unityVersion);
        builder.AppendLine("Plataforma: " + Application.platform);
        builder.AppendLine("FPS: " + (Time.deltaTime > 0f
            ? (1f / Time.deltaTime).ToString("F1")
            : "N/A"));

        builder.AppendLine();
        builder.AppendLine(
            $"Logs: {logCount} | Warnings: {warningCount} | " +
            $"Errors: {errorCount} | Exceptions: {exceptionCount}");

        builder.AppendLine();

        lock (logLock)
        {
            foreach (LogEntry entry in entries)
            {
                builder.AppendLine(
                    $"[{entry.timestamp}] [{GetTypeName(entry.type)}]");

                builder.AppendLine(entry.message);

                if (!string.IsNullOrEmpty(entry.stackTrace))
                {
                    builder.AppendLine("--- STACK TRACE ---");
                    builder.AppendLine(entry.stackTrace);
                }

                builder.AppendLine();
            }

            // Inclui mensagens que ainda não foram transferidas
            // para a lista principal.
            foreach (LogEntry entry in pendingEntries)
            {
                builder.AppendLine(
                    $"[{entry.timestamp}] [{GetTypeName(entry.type)}]");

                builder.AppendLine(entry.message);

                if (!string.IsNullOrEmpty(entry.stackTrace))
                {
                    builder.AppendLine("--- STACK TRACE ---");
                    builder.AppendLine(entry.stackTrace);
                }

                builder.AppendLine();
            }
        }

        GUIUtility.systemCopyBuffer = builder.ToString();
    }

    private string GetTypeName(LogType type)
    {
        switch (type)
        {
            case LogType.Warning:
                return "WARNING";

            case LogType.Error:
                return "ERROR";

            case LogType.Assert:
                return "ASSERT";

            case LogType.Exception:
                return "EXCEPTION";

            default:
                return "LOG";
        }
    }

    private bool MatchesFilter(LogType type)
    {
        switch (currentFilter)
        {
            case LogFilter.Log:
                return type == LogType.Log;

            case LogFilter.Warning:
                return type == LogType.Warning;

            case LogFilter.Error:
                return type == LogType.Error ||
                       type == LogType.Assert;

            case LogFilter.Exception:
                return type == LogType.Exception;

            default:
                return true;
        }
    }

    private void InitializeStyles()
    {
        if (stylesInitialized &&
            lastScreenWidth == Screen.width &&
            lastScreenHeight == Screen.height)
        {
            return;
        }

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 8, 8)
        };

        panelStyle.normal.background = MakeTexture(
            new Color(0.07f, 0.08f, 0.09f, 0.98f));

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 4,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false
        };
        titleStyle.normal.textColor = Color.white;

        entryStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            wordWrap = true,
            richText = false,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(4, 4, 4, 4)
        };

        stackStyle = new GUIStyle(entryStyle)
        {
            fontSize = Mathf.Max(10, fontSize - 1),
            wordWrap = true,
            padding = new RectOffset(10, 4, 2, 8)
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = fontSize,
            wordWrap = true,
            padding = new RectOffset(6, 6, 8, 8),
            fixedHeight = 40
        };

        counterStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };
        counterStyle.normal.textColor = Color.white;

        toolbarStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.Max(10, fontSize - 1),
            fixedHeight = 36,
            padding = new RectOffset(4, 4, 4, 4)
        };

        stylesInitialized = true;
    }

    private Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private Color GetLogColor(LogType type)
    {
        switch (type)
        {
            case LogType.Warning:
                return new Color(1f, 0.78f, 0.20f);

            case LogType.Error:
            case LogType.Assert:
                return new Color(1f, 0.35f, 0.35f);

            case LogType.Exception:
                return new Color(1f, 0.45f, 0.75f);

            default:
                return new Color(0.78f, 0.88f, 0.95f);
        }
    }

    private void OnGUI()
    {
		if (!consoleEnabled)
			return;

		InitializeStyles();

        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        if (!isVisible)
        {
            float buttonWidth = Mathf.Min(170f, screenWidth * 0.4f);
            float buttonHeight = 44f;

            if (GUI.Button(
                new Rect(8f, 100f, buttonWidth, buttonHeight),
                "ABRIR CONSOLE", buttonStyle))
            {
                isVisible = true;
            }

            return;
        }

        float width = Mathf.Clamp(
            screenWidth * panelWidthPercent, 280f, screenWidth);

        float height = Mathf.Clamp(
            screenHeight * panelHeightPercent, 300f, screenHeight);

        width = Mathf.Min(width, screenWidth - 8f);
        height = Mathf.Min(height, screenHeight - 8f);

        Rect panelRect = new Rect(
            (screenWidth - width) * 0.5f,
            (screenHeight - height) * 0.5f,
            width,
            height);

        GUILayout.BeginArea(panelRect, panelStyle);

        DrawHeader(width);
        DrawCounters();
        DrawToolbar();

        GUILayout.Space(5f);

        DrawEntries(width);

        GUILayout.Space(5f);
        DrawFooter();

        GUILayout.EndArea();
    }

    private void DrawHeader(float width)
    {
        GUILayout.BeginHorizontal();

        GUILayout.Label("DEBUG CONSOLE", titleStyle,
            GUILayout.Height(38f));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("FECHAR", buttonStyle,
            GUILayout.Width(Mathf.Min(100f, width * 0.30f))))
        {
            isVisible = false;
        }

        GUILayout.EndHorizontal();
    }

    private void DrawCounters()
    {
        lock (logLock)
        {
            GUILayout.Label(
                $"LOGS: {logCount}   |   WARN: {warningCount}   |   " +
                $"ERROR: {errorCount}   |   EXC: {exceptionCount}",
                counterStyle);
        }
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal();

        DrawFilterButton("TODOS", LogFilter.All);
        DrawFilterButton("LOGS", LogFilter.Log);
        DrawFilterButton("WARN", LogFilter.Warning);
        DrawFilterButton("ERROR", LogFilter.Error);
        DrawFilterButton("EXC", LogFilter.Exception);

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        bool newStackState = GUILayout.Toggle(
            showStackTrace,
            "Mostrar Stack Trace",
            GUI.skin.toggle,
            GUILayout.MinHeight(32f));

        if (newStackState != showStackTrace)
            showStackTrace = newStackState;

        autoScroll = GUILayout.Toggle(
            autoScroll,
            "Auto-scroll",
            GUI.skin.toggle,
            GUILayout.MinHeight(32f));

        GUILayout.EndHorizontal();
    }

    private void DrawFilterButton(string label, LogFilter filter)
    {
        bool selected = currentFilter == filter;

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = selected
            ? new Color(0.25f, 0.65f, 0.35f)
            : Color.white;

        if (GUILayout.Button(label, toolbarStyle,
            GUILayout.ExpandWidth(true)))
        {
            currentFilter = filter;
            scrollPosition = Vector2.zero;
        }

        GUI.backgroundColor = oldColor;
    }

    private void DrawEntries(float width)
    {
        float contentWidth = Mathf.Max(100f, width - 48f);

        lock (logLock)
        {
            int visibleCount = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                if (MatchesFilter(entries[i].type))
                    visibleCount++;
            }

            GUILayout.Label(
                $"REGISTROS ({visibleCount})",
                counterStyle,
                GUILayout.Height(24f));

            float viewHeight = Mathf.Max(
                60f,
                panelRectHeightForEntries());

            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition,
                true,
                true,
                GUILayout.ExpandHeight(true),
                GUILayout.MinHeight(viewHeight));

            for (int i = 0; i < entries.Count; i++)
            {
                LogEntry entry = entries[i];

                if (!MatchesFilter(entry.type))
                    continue;

                DrawEntry(entry, contentWidth);
            }

            GUILayout.EndScrollView();
        }

        if (autoScroll && Event.current.type == EventType.Repaint)
        {
            // O ajuste real acontece no próximo evento de layout.
            // Mantém o console no final durante a captura.
            scrollPosition.y = float.MaxValue;
        }
    }

    private float panelRectHeightForEntries()
    {
        return Mathf.Max(60f, Screen.height * 0.35f);
    }

    private void DrawEntry(LogEntry entry, float width)
    {
        Color oldColor = GUI.contentColor;
        GUI.contentColor = GetLogColor(entry.type);

        string header =
            $"[{entry.timestamp}] [{GetTypeName(entry.type)}]";

        GUILayout.Label(header, entryStyle,
            GUILayout.Width(width));

        GUI.contentColor = Color.white;

        GUILayout.Label(entry.message, entryStyle,
            GUILayout.Width(width));

        if (showStackTrace && !string.IsNullOrEmpty(entry.stackTrace))
        {
            GUI.contentColor = new Color(0.72f, 0.76f, 0.80f);

            GUILayout.Label(
                entry.stackTrace,
                stackStyle,
                GUILayout.Width(width));

            GUI.contentColor = Color.white;
        }

        GUI.contentColor = oldColor;

        GUILayout.Space(5f);
        GUILayout.Box("", GUILayout.ExpandWidth(true),
            GUILayout.Height(1f));
    }

	private void DrawFooter()
	{
		GUILayout.BeginHorizontal();

		if (GUILayout.Button("LIMPAR CONSOLE", buttonStyle,
			GUILayout.ExpandWidth(true)))
		{
			ClearConsole();
		}

		if (GUILayout.Button("COPIAR DADOS", buttonStyle,
			GUILayout.ExpandWidth(true)))
		{
			CopyConsole();
		}

		if (GUILayout.Button("ENVIAR LOG NO SITE", buttonStyle,
			GUILayout.ExpandWidth(true)))
		{
			Application.OpenURL("https://sgames.site/#contact");
		}

		GUILayout.EndHorizontal();
	}

	public void OpenConsole()
    {
        isVisible = true;
    }

    public void CloseConsole()
    {
        isVisible = false;
    }

    public void ToggleConsole()
    {
        isVisible = !isVisible;
    }

    public void AddDiagnostic(string message)
    {
        Debug.Log("[DIAGNOSTICO] " + message);
    }
}
