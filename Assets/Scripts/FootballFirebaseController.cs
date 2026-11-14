using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Deve ficar em um GameObject ativo que NÃO seja desativado pela Timeline.
/// Centraliza identidade, conexão, entrada/reparo da sala e diagnóstico F2.
/// </summary>
public class FootballFirebaseController : MonoBehaviour
{
    [Header("Identidade")]
    [Tooltip("Se disponível, lê o papel e o nome diretamente do QuestFootKick, mesmo se ele estiver inativo.")]
    public bool readIdentityFromQuestFootKick = true;
    public QuestFootKick questFootKick;
    public QuestFootKick.PlayerRole playerRole = QuestFootKick.PlayerRole.Player1;
    public string playerDisplayName = "";

    [Header("Diagnóstico")]
    public KeyCode diagnosticKey = KeyCode.F2;
    public bool showDiagnosticOnStart = false;
    [Min(1f)] public float reconnectCheckInterval = 5f;
    [Min(5f)] public float connectionTimeout = 30f;

    private FirebaseManager manager;
    private bool showDiagnostic;
    private bool operationRunning;
    private bool destroying;
    private float nextRecoveryCheck;
    private string localError = "";
    private Vector2 scroll;

    private string PlayerId => playerRole == QuestFootKick.PlayerRole.Player1 ? "Jogador1" : "Jogador2";
    private string DisplayName => string.IsNullOrWhiteSpace(playerDisplayName) ? PlayerId : playerDisplayName.Trim();

    private void Awake()
    {
        showDiagnostic = showDiagnosticOnStart;
    }

    private void Start()
    {
        ResolveIdentity();
        manager = FirebaseManager.Instance;

        if (manager == null)
        {
            localError = "FirebaseManager não encontrado. Coloque-o na cena antes de iniciar.";
            Debug.LogError("[FootballFirebaseController] " + localError);
            return;
        }

        manager.ConfigureIdentity(PlayerId);
        manager.OnError += HandleFirebaseError;
        manager.OnRoomJoined += HandleRoomJoined;
        manager.OnRoomUpdated += HandleRoomUpdated;

        StartCoroutine(InitialConnectionRoutine());
    }

    private void ResolveIdentity()
    {
        if (readIdentityFromQuestFootKick && questFootKick == null)
            questFootKick = FindAnyObjectByType<QuestFootKick>(FindObjectsInactive.Include);

        if (readIdentityFromQuestFootKick && questFootKick != null)
        {
            playerRole = questFootKick.myRole;
            playerDisplayName = questFootKick.playerDisplayName;
        }

        if (string.IsNullOrWhiteSpace(playerDisplayName))
            playerDisplayName = playerRole == QuestFootKick.PlayerRole.Player1 ? "Jogador 1" : "Jogador 2";
    }

    private IEnumerator InitialConnectionRoutine()
    {
        while (manager != null && !manager.IsInitialized && manager.InitializationInProgress)
            yield return null;

        if (manager == null) yield break;

        if (!manager.IsInitialized)
        {
            Task<bool> initializeTask = manager.InitializeFirebaseAsync();
            yield return new WaitUntil(() => initializeTask.IsCompleted);
            if (!initializeTask.Result)
            {
                localError = string.IsNullOrEmpty(manager.LastError) ? "Não foi possível inicializar o Firebase." : manager.LastError;
                yield break;
            }
        }

        float elapsed = 0f;
        while (manager != null && !manager.IsConnected && elapsed < connectionTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (manager == null || !manager.IsConnected)
        {
            localError = "Firebase inicializado, mas não conectou ao Realtime Database dentro do tempo limite.";
            yield break;
        }

        yield return StartCoroutine(RecoverRoomRoutine(false));
    }

    private void Update()
    {
        if (Input.GetKeyDown(diagnosticKey))
            showDiagnostic = !showDiagnostic;

        if (manager == null || operationRunning || destroying) return;

        // Se o Admin limpar a estrutura da sala, o listener atualiza a presença
        // local para false. O controlador regrava o jogador sem reiniciar a cena.
        if (manager.IsConnected && manager.IsInRoom && !manager.IsMyPlayerConnected && Time.unscaledTime >= nextRecoveryCheck)
        {
            nextRecoveryCheck = Time.unscaledTime + reconnectCheckInterval;
            StartCoroutine(RecoverRoomRoutine(false));
        }
    }

    private IEnumerator RecoverRoomRoutine(bool forced)
    {
        if (operationRunning || manager == null) yield break;
        operationRunning = true;
        localError = "";

        Task<bool> task = forced
            ? manager.ForceReconnectAsync(DisplayName)
            : manager.ReconnectRoomAsync(DisplayName);
        yield return new WaitUntil(() => task.IsCompleted);

        bool success = false;
        try { success = task.Result; }
        catch (System.Exception ex) { localError = ex.Message; }

        if (!success && string.IsNullOrEmpty(localError))
            localError = string.IsNullOrEmpty(manager.LastError) ? "Não foi possível entrar ou recuperar a sala." : manager.LastError;

        if (success)
            Debug.Log("[FootballFirebaseController] Conectado como " + PlayerId + " na sala " + manager.RoomCode + ".");

        operationRunning = false;
    }

    private void HandleFirebaseError(string message)
    {
        localError = message;
    }

    private void HandleRoomJoined()
    {
        localError = "";
    }

    private void HandleRoomUpdated(FirebaseManager.RoomState room)
    {
        // Mantido para permitir diagnóstico e futuras métricas sem transferir
        // qualquer regra de gameplay para este componente.
    }

    private void ForceReconnect()
    {
        if (operationRunning || manager == null) return;
        StartCoroutine(RecoverRoomRoutine(true));
    }

    private void ReloadCurrentScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
    }


	private void OnGUI()
	{
		if (!showDiagnostic) return;

		// Escala automática conforme a resolução da tela.
		float scale = Mathf.Clamp(Screen.height / 900f, 1f, 2.2f);

		float panelWidth = 570f * scale;
		float panelHeight = 570f * scale;

		// Mantém o painel dentro da tela.
		float margin = 20f * scale;
		float panelX = margin;
		float panelY = margin;

		panelWidth = Mathf.Min(panelWidth, Screen.width - margin * 2f);
		panelHeight = Mathf.Min(panelHeight, Screen.height - margin * 2f);

		Matrix4x4 oldMatrix = GUI.matrix;

		GUI.matrix = Matrix4x4.TRS(
			Vector3.zero,
			Quaternion.identity,
			Vector3.one
		);

		GUIStyle boxStyle = new GUIStyle(GUI.skin.box);

		GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
		{
			fontSize = Mathf.RoundToInt(22 * scale),
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleCenter,
			wordWrap = true
		};

		GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
		{
			fontSize = Mathf.RoundToInt(17 * scale),
			wordWrap = true
		};

		GUIStyle sectionStyle = new GUIStyle(GUI.skin.label)
		{
			fontSize = Mathf.RoundToInt(18 * scale),
			fontStyle = FontStyle.Bold
		};

		GUIStyle errorStyle = new GUIStyle(GUI.skin.textArea)
		{
			fontSize = Mathf.RoundToInt(15 * scale),
			wordWrap = true
		};

		GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
		{
			fontSize = Mathf.RoundToInt(18 * scale),
			fontStyle = FontStyle.Bold
		};

		float padding = 20f * scale;
		float spacing = 8f * scale;

		GUI.Box(
			new Rect(panelX, panelY, panelWidth, panelHeight),
			GUIContent.none,
			boxStyle
		);

		GUILayout.BeginArea(new Rect(
			panelX + padding,
			panelY + padding,
			panelWidth - padding * 2f,
			panelHeight - padding * 2f
		));

		GUILayout.Label("FOOTBALL VR — FIREBASE", titleStyle);
		GUILayout.Label("PAINEL DE DIAGNÓSTICO", sectionStyle);

		GUILayout.Space(spacing);

		if (manager == null)
		{
			GUILayout.Label("FirebaseManager: NÃO ENCONTRADO", labelStyle);
		}
		else
		{
			GUILayout.Label(
				"Firebase SDK: " +
				(manager.IsInitialized ? "Inicializado" :
				manager.InitializationInProgress ? "Inicializando..." :
				"Não inicializado"),
				labelStyle
			);

			GUILayout.Label(
				"Realtime Database: " +
				(manager.IsConnected ? "CONECTADO" : "DESCONECTADO"),
				labelStyle
			);

			GUILayout.Label(
				"Sala: " + (manager.IsInRoom ? manager.RoomCode : "Não entrou"),
				labelStyle
			);

			GUILayout.Label(
				"Identidade: " + PlayerId + " | " + DisplayName,
				labelStyle
			);

			GUILayout.Label(
				"Presença: " + (manager.IsMyPlayerConnected ? "ONLINE" : "OFFLINE"),
				labelStyle
			);

			GUILayout.Label(
				"Room Listener: " + (manager.HasRoomListener ? "Ativo" : "Inativo"),
				labelStyle
			);

			GUILayout.Label(
				"Action Listener: " + (manager.HasActionListener ? "Ativo" : "Inativo"),
				labelStyle
			);

			GUILayout.Label(
				"Auth UID: " +
				(string.IsNullOrEmpty(manager.AuthUid) ? "—" : manager.AuthUid),
				labelStyle
			);

			GUILayout.Label(
				"Status da partida: " +
				(manager.CurrentRoom != null ? manager.CurrentRoom.Status : "—"),
				labelStyle
			);
		}

		string error = !string.IsNullOrEmpty(localError)
			? localError
			: manager != null ? manager.LastError : "";

		GUILayout.Space(spacing);

		GUILayout.Label("ÚLTIMO ERRO:", sectionStyle);

		float errorHeight = 75f * scale;

		scroll = GUILayout.BeginScrollView(
			scroll,
			GUILayout.Height(errorHeight)
		);

		GUILayout.TextArea(
			string.IsNullOrEmpty(error) ? "Nenhum erro registrado." : error,
			errorStyle,
			GUILayout.ExpandHeight(true)
		);

		GUILayout.EndScrollView();

		GUILayout.Space(spacing);

		bool matchIsPlaying =
			manager != null &&
			manager.CurrentRoom != null &&
			manager.CurrentRoom.Status == "Playing";

		GUI.enabled = !operationRunning && manager != null && !matchIsPlaying;

		if (GUILayout.Button(
			operationRunning ? "RECONECTANDO..." : "FORCE RECONNECT",
			buttonStyle,
			GUILayout.Height(48f * scale)))
		{
			ForceReconnect();
		}

		GUI.enabled = true;

		if (matchIsPlaying)
		{
			GUILayout.Label(
				"Force Reconnect bloqueado durante a partida.",
				labelStyle
			);
		}

		GUILayout.Space(spacing);

		if (GUILayout.Button(
			"RELOAD CURRENT SCENE",
			buttonStyle,
			GUILayout.Height(44f * scale)))
		{
			ReloadCurrentScene();
		}

		GUILayout.FlexibleSpace();

		GUILayout.Label("F2: ABRIR / FECHAR PAINEL", sectionStyle);

		GUILayout.EndArea();

		GUI.matrix = oldMatrix;
	}


	private void OnDestroy()
    {
        destroying = true;
        if (manager != null)
        {
            manager.OnError -= HandleFirebaseError;
            manager.OnRoomJoined -= HandleRoomJoined;
            manager.OnRoomUpdated -= HandleRoomUpdated;
        }
    }
}
