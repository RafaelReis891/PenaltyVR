using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class FirebaseManager : MonoBehaviour
{
	public static FirebaseManager Instance;

	[Header("Servidor Local")]
	[Tooltip("URL do servidor Node.js.")]
	public string localServerUrl = "http://localhost:3001";

	[Header("Configuração de IP")]
	private const string LOCAL_SERVER_PREFS = "FootballVR_LocalServerIP";

	private string serverIPInput = "";
	private bool showServerConfig;

	public string FIXED_ROOM_CODE = "ARENA01";
	public const int TURN_DURATION_SECONDS = 30;

	private bool isLocalSocketConfigured;
	private bool socketConnectRequested;
	private bool identityConfigured;
	private bool firebaseReadyEventSent;
	private bool initializationInProgress;
	private bool reconnectInProgress;

	private string lastPlayerName;
	private string roomCode;

	private long serverTimeOffset;
	private bool hasServerTimeOffset;

	private long lastRestartSignal;
	private bool restartSignalInitialized;

	public string playerId;

	// Mantido para compatibilidade com scripts antigos.
	// Não existe autenticação Firebase nesta versão.
	public string AuthUid => "";

	public bool IsInitialized { get; private set; }
	public bool IsConnected { get; private set; }
	public bool IsInRoom { get; private set; }
	public bool IsMyPlayerConnected { get; private set; }

	public bool InitializationInProgress => initializationInProgress;

	public bool HasRoomListener => isLocalSocketConfigured;
	public bool HasActionListener => isLocalSocketConfigured;

	public string LastError { get; private set; } = "";

	public long ServerTimeOffset => serverTimeOffset;
	public bool HasServerTimeOffset => hasServerTimeOffset;

	public string PlayerId => playerId;
	public string RoomCode => roomCode;

	public RoomState CurrentRoom { get; private set; }

	public event Action OnFirebaseReady;
	public event Action<RoomState> OnRoomUpdated;
	public event Action<string> OnError;
	public event Action OnRoomJoined;
	public event Action OnRoomLeft;
	public event Action OnShotProcessed;
	public event Action OnMatchRestartRequested;

	private QuestFootKick questFootKick;

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;

		questFootKick = FindAnyObjectByType<QuestFootKick>();

		LoadLocalServerIP();

		DontDestroyOnLoad(gameObject);
	}

	private async void Start()
	{
		if (questFootKick == null)
			questFootKick = FindAnyObjectByType<QuestFootKick>(FindObjectsInactive.Include);

		if (questFootKick != null && questFootKick.DEBUG_MODE)
		{
			Debug.Log("[FirebaseManager] DEBUG_MODE ativo. Inicialização do servidor ignorada.");
			return;
		}

		await InitializeFirebaseAsync();
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.F3))
		{
			showServerConfig = !showServerConfig;
		}
	}

	[Serializable]
	private class AdminSimulateShotPayload
	{
		public string room;
		public string commandId;
	}

	#region Configuração do servidor

	private void LoadLocalServerIP()
	{
		if (PlayerPrefs.HasKey(LOCAL_SERVER_PREFS))
		{
			string savedAddress =
				PlayerPrefs.GetString(LOCAL_SERVER_PREFS);

			localServerUrl = BuildServerUrl(savedAddress);
		}
		else
		{
			localServerUrl = BuildServerUrl(localServerUrl);
		}

		if (string.IsNullOrEmpty(localServerUrl))
			localServerUrl = "http://localhost:3001";

		serverIPInput = GetServerAddress(localServerUrl);

		Debug.Log(
			"[FootballVR] Servidor configurado: " +
			localServerUrl
		);
	}

	private void SaveLocalServerIP()
	{
		if (string.IsNullOrWhiteSpace(serverIPInput))
		{
			ReportError("Informe o endereço IP do servidor.");
			return;
		}

		string normalizedUrl = BuildServerUrl(serverIPInput);

		if (string.IsNullOrEmpty(normalizedUrl))
		{
			ReportError("Endereço do servidor inválido.");
			return;
		}

		localServerUrl = normalizedUrl;

		serverIPInput = GetServerAddress(localServerUrl);

		PlayerPrefs.SetString(
			LOCAL_SERVER_PREFS,
			serverIPInput
		);

		PlayerPrefs.Save();

		Debug.Log(
			"[FootballVR] Servidor salvo: " +
			localServerUrl
		);

		Debug.LogWarning(
			"[FootballVR] Reinicie a aplicação para aplicar " +
			"o novo endereço do servidor."
		);
	}

	private string BuildServerUrl(string address)
	{
		if (string.IsNullOrWhiteSpace(address))
			return "http://localhost:3001";

		address = address.Trim();

		address = address.Replace("ws://", "http://");
		address = address.Replace("wss://", "https://");

		address = address.TrimEnd('/');

		if (!address.Contains("://"))
			address = "http://" + address;

		if (!Uri.TryCreate(
			address,
			UriKind.Absolute,
			out Uri uri))
		{
			return "";
		}

		if (uri.Scheme != "http" &&
			uri.Scheme != "https")
		{
			return "";
		}

		UriBuilder builder = new UriBuilder(uri);

		if (uri.IsDefaultPort)
			builder.Port = 3001;

		builder.Path = "";
		builder.Query = "";
		builder.Fragment = "";

		return builder.Uri
			.GetLeftPart(UriPartial.Authority)
			.TrimEnd('/');
	}

	private string GetServerAddress(string url)
	{
		if (Uri.TryCreate(
			url,
			UriKind.Absolute,
			out Uri uri))
		{
			return uri.Authority;
		}

		return url
			.Replace("http://", "")
			.Replace("https://", "")
			.TrimEnd('/');
	}

	private void OnGUI()
	{
		if (!showServerConfig)
			return;

		float width = 420;
		float height = 190;

		Rect window = new Rect(
			(Screen.width - width) / 2,
			(Screen.height - height) / 2,
			width,
			height
		);

		GUI.Box(window, "");

		GUILayout.BeginArea(
			new Rect(
				window.x + 15,
				window.y + 15,
				width - 30,
				height - 30
			)
		);

		GUILayout.Label("<b>CONFIGURAÇÃO DO SERVIDOR LOCAL</b>");

		GUILayout.Space(15);

		GUILayout.Label("Endereço IP ou domínio do servidor:");

		serverIPInput = GUILayout.TextField(
			serverIPInput,
			100
		);

		GUILayout.Space(10);

		GUILayout.Label("Endereço atual: " + localServerUrl);

		GUILayout.Space(15);

		GUILayout.BeginHorizontal();

		if (GUILayout.Button("SALVAR IP", GUILayout.Height(35)))
		{
			SaveLocalServerIP();
		}

		if (GUILayout.Button("FECHAR", GUILayout.Height(35)))
		{
			showServerConfig = false;
		}

		GUILayout.EndHorizontal();

		GUILayout.EndArea();
	}

	#endregion

	#region Inicialização

	public async Task<bool> InitializeFirebaseAsync()
	{
		// Nome mantido por compatibilidade.
		// Esta função inicializa exclusivamente o servidor local.

		if (IsInitialized && IsConnected)
			return true;

		if (initializationInProgress)
		{
			for (int i = 0; i < 150; i++)
			{
				if (!initializationInProgress)
					break;

				if (IsInitialized && IsConnected)
					return true;

				await Task.Delay(100);
			}

			return IsInitialized && IsConnected;
		}

		initializationInProgress = true;

		try
		{
			return await InitializeLocalModeAsync();
		}
		catch (Exception ex)
		{
			ReportError(
				"Erro ao inicializar servidor local: " +
				ex.Message
			);

			return false;
		}
		finally
		{
			initializationInProgress = false;
		}
	}

	private async Task<bool> InitializeLocalModeAsync()
	{
		Debug.Log(
			"[FootballVR] Inicializando servidor Node.js..."
		);

		if (string.IsNullOrEmpty(localServerUrl))
		{
			ReportError("Endereço do servidor não configurado.");
			return false;
		}

		// Sincroniza o relógio com o Node.js.
		await SyncLocalTimeAsync();

		if (SocketIOManager.Instance == null ||
			SocketIOManager.Instance.Socket == null)
		{
			ReportError(
				"SocketIOManager ou Socket não encontrado na cena."
			);

			return false;
		}

		var socket = SocketIOManager.Instance.Socket;

		// Registra os eventos somente uma vez.
		if (!isLocalSocketConfigured)
		{
			socket.OnConnected += OnLocalConnected;
			socket.OnDisconnected += OnLocalDisconnected;
			socket.OnError += OnLocalSocketError;

			// Atualização normal dos dados da sala.
			socket.On("value", OnLocalValueReceived);

			// Comando recebido do painel administrativo HTML.
			socket.On(
				"admin:simulate-shot",
				OnAdminSimulateShotReceived
			);

			isLocalSocketConfigured = true;
		}

		// Se já estiver conectado, não solicita outra conexão.
		if (IsConnected)
		{
			IsInitialized = true;
			LastError = "";
			return true;
		}

		string wsUrl = localServerUrl
			.Replace("http://", "ws://")
			.Replace("https://", "wss://");

		Debug.Log(
			"[FootballVR Local] Conectando em: " +
			wsUrl
		);

		if (!socketConnectRequested)
		{
			socketConnectRequested = true;

			socket.Connect(wsUrl);
		}

		// Aguarda até 15 segundos pela conexão.
		// O callback OnLocalConnected também pode concluir
		// a inicialização caso a conexão demore mais.
		for (int i = 0; i < 150; i++)
		{
			if (IsConnected)
			{
				IsInitialized = true;
				LastError = "";

				Debug.Log(
					"[FootballVR] Servidor local inicializado."
				);

				return true;
			}

			await Task.Delay(100);
		}

		if (!IsConnected)
		{
			socketConnectRequested = false;

			ReportError(
				"Não foi possível conectar ao servidor local: " +
				wsUrl
			);

			return false;
		}

		return true;
	}

	private void OnAdminSimulateShotReceived(string msg)
	{
		try
		{
			if (string.IsNullOrEmpty(msg))
			{
				Debug.LogWarning(
					"[FootballVR Admin] Comando recebido sem payload."
				);

				return;
			}

			AdminSimulateShotPayload payload =
				JsonUtility.FromJson<AdminSimulateShotPayload>(msg);

			if (payload == null)
			{
				Debug.LogWarning(
					"[FootballVR Admin] Payload inválido."
				);

				return;
			}

			// Só aceita comandos enquanto estiver conectado
			// e dentro da sala correta.
			if (!IsConnected || !IsInRoom)
				return;

			string targetRoom = string.IsNullOrEmpty(roomCode)
				? FIXED_ROOM_CODE
				: roomCode;

			if (!string.Equals(
				payload.room,
				targetRoom,
				StringComparison.OrdinalIgnoreCase))
			{
				Debug.Log(
					"[FootballVR Admin] Comando ignorado. " +
					"Sala recebida: " + payload.room +
					" | Sala atual: " + targetRoom
				);

				return;
			}

			if (questFootKick == null)
			{
				questFootKick =
					FindAnyObjectByType<QuestFootKick>(
						FindObjectsInactive.Include
					);
			}

			if (questFootKick == null)
			{
				Debug.LogWarning(
					"[FootballVR Admin] QuestFootKick não encontrado."
				);

				return;
			}

			Debug.Log(
				"[FootballVR Admin] Comando de chute recebido. " +
				"ID: " + payload.commandId
			);

			// Chama o método que já existe no QuestFootKick.
			questFootKick.SimularChuteAleatorio();
		}
		catch (Exception ex)
		{
			Debug.LogError(
				"[FootballVR Admin] Erro ao processar comando: " +
				ex.Message
			);
		}
	}

	private void OnLocalConnected()
	{
		IsConnected = true;
		IsInitialized = true;
		socketConnectRequested = true;

		LastError = "";

		Debug.Log(
			"[FootballVR Local] Socket conectado!"
		);

		if (!firebaseReadyEventSent)
		{
			firebaseReadyEventSent = true;

			OnFirebaseReady?.Invoke();
		}

		// Se já estava em uma sala, recupera a sessão.
		if (IsInRoom &&
			!string.IsNullOrEmpty(lastPlayerName))
		{
			_ = ReconnectRoomAsync(lastPlayerName);
		}
	}

	private void OnLocalDisconnected()
	{
		IsConnected = false;

		Debug.LogWarning(
			"[FootballVR Local] Desconectado do servidor."
		);
	}

	private void OnLocalSocketError(
		SocketIOUnity.Runtime.SocketError err)
	{
		if (questFootKickIsPlayingAlone())
			return;

		Debug.LogError(
			"[FootballVR Local] Socket: " +
			err.Message
		);
	}

	private void OnLocalValueReceived(string msg)
	{
		try
		{
			var payload = JsonUtility.FromJson<SocketPayload>(msg);

			if (payload == null)
				return;

			if (payload.path != "Rooms/" + FIXED_ROOM_CODE &&
				payload.path != "Rooms")
			{
				return;
			}

			UpdateLocalRoomState(payload.value);
		}
		catch (Exception ex)
		{
			Debug.LogWarning(
				"[FootballVR Local] Erro ao processar socket: " +
				ex.Message
			);
		}
	}

	private async Task SyncLocalTimeAsync()
	{
		try
		{
			long localBefore =
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			using (UnityWebRequest req = UnityWebRequest.Get(
				$"{localServerUrl}/api/time"))
			{
				req.timeout = 5;

				await req.SendWebRequest();

				if (req.result != UnityWebRequest.Result.Success)
				{
					Debug.LogWarning(
						"[FootballVR Local] Falha ao sincronizar horário: " +
						req.error
					);

					return;
				}

				long localAfter =
					DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

				var json = JsonUtility.FromJson<TimeResponse>(
					req.downloadHandler.text
				);

				if (json == null)
					return;

				long roundTrip = localAfter - localBefore;

				long estimatedLocalAtServer =
					localBefore + (roundTrip / 2);

				serverTimeOffset =
					json.serverTime - estimatedLocalAtServer;

				hasServerTimeOffset = true;

				Debug.Log(
					"[FootballVR Local] Horário sincronizado: " +
					serverTimeOffset + "ms"
				);
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning(
				"[FootballVR Local] Falha ao sincronizar horário: " +
				ex.Message
			);
		}
	}

	#endregion

	#region Modelos JSON

	[Serializable]
	private class TimeResponse
	{
		public long serverTime;
	}

	[Serializable]
	private class ShotResponse
	{
		public bool success;
		public bool processed;
		public string error;
	}

	[Serializable]
	private class JoinResponse
	{
		public bool success;
		public string error;
	}

	[Serializable]
	private class JoinPayload
	{
		public string playerId;
		public string playerName;
	}

	[Serializable]
	private class ShotPayload
	{
		public string playerId;
		public string shotId;
		public bool goal;
		public int turnNumber;
	}

	[Serializable]
	private class LeavePayload
	{
		public string playerId;
	}

	[Serializable]
	public class SocketPayload
	{
		public string path;
		public SocketRoomData value;
	}

	[Serializable]
	public class SocketRoomData
	{
		public SocketMatchData Match;
		public SocketScoreData Score;
		public SocketPlayersData Players;
	}

	[Serializable]
	public class SocketMatchData
	{
		public string Status;
		public string CurrentTurnPlayer;
		public int TurnNumber;
		public int ShotsPerPlayer;
		public string Winner;
		public long TurnStartedAt;
		public int TurnDuration;
		public long RestartSignal;
	}

	[Serializable]
	public class SocketScoreData
	{
		public int Jogador1;
		public int Jogador2;
	}

	[Serializable]
	public class SocketPlayersData
	{
		public SocketPlayerData Jogador1;
		public SocketPlayerData Jogador2;
	}

	[Serializable]
	public class SocketPlayerData
	{
		public string Name;
		public bool Connected;
	}

	#endregion

	#region Identidade

	public void ConfigureIdentity(string fixedPlayerId)
	{
		if (IsInRoom)
		{
			if (playerId == fixedPlayerId)
				return;

			Debug.LogError(
				"[FootballVR] Não é possível trocar a identidade " +
				"durante a partida."
			);

			return;
		}

		if (fixedPlayerId != "Jogador1" &&
			fixedPlayerId != "Jogador2")
		{
			ReportError(
				"Identidade inválida. Use Jogador1 ou Jogador2."
			);

			identityConfigured = false;
			return;
		}

		playerId = fixedPlayerId;
		identityConfigured = true;

		Debug.Log(
			"[FootballVR] Identidade configurada: " +
			playerId
		);
	}

	#endregion

	#region Sala

	public async Task JoinRoom(string playerName)
	{
		if (!IsInitialized || !IsConnected || !identityConfigured)
		{
			ReportError(
				"Sistema não inicializado, sem conexão ou identidade não configurada."
			);

			return;
		}

		if (IsInRoom)
			return;

		lastPlayerName = string.IsNullOrWhiteSpace(playerName)
			? playerId
			: playerName.Trim();

		roomCode = FIXED_ROOM_CODE;

		await JoinRoomLocalAsync(lastPlayerName);
	}

	private async Task JoinRoomLocalAsync(string playerName)
	{
		try
		{
			string jsonPayload = JsonUtility.ToJson(
				new JoinPayload
				{
					playerId = playerId,
					playerName = playerName
				}
			);

			using (UnityWebRequest req = new UnityWebRequest(
				$"{localServerUrl}/api/match/join",
				"POST"))
			{
				byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

				req.uploadHandler = new UploadHandlerRaw(bodyRaw);
				req.downloadHandler = new DownloadHandlerBuffer();

				req.SetRequestHeader(
					"Content-Type",
					"application/json"
				);

				req.timeout = 10;

				await req.SendWebRequest();

				if (req.result != UnityWebRequest.Result.Success)
				{
					ReportError(
						"Falha ao entrar na sala: " +
						req.error
					);

					return;
				}

				var response = JsonUtility.FromJson<JoinResponse>(
					req.downloadHandler.text
				);

				if (response == null || !response.success)
				{
					ReportError(
						"Não foi possível entrar: " +
						(response != null
							? response.error
							: "Resposta inválida.")
					);

					return;
				}
			}

			IsInRoom = true;

			// Identifica o jogador no Socket.IO e acompanha a sala.
			if (isLocalSocketConfigured &&
				SocketIOManager.Instance?.Socket != null &&
				IsConnected)
			{
				var socket = SocketIOManager.Instance.Socket;

				socket.Emit(
					"identify",
					new { playerId = playerId }
				);

				socket.Emit(
					"subscribe",
					new
					{
						path = "Rooms/" + FIXED_ROOM_CODE
					}
				);

				Debug.Log(
					"[FootballVR Local] Inscrito na sala " +
					FIXED_ROOM_CODE
				);
			}

			restartSignalInitialized = false;
			lastRestartSignal = 0;

			OnRoomJoined?.Invoke();

			LastError = "";

			Debug.Log(
				"[FootballVR Local] Entrou na sala como " +
				playerId
			);
		}
		catch (Exception ex)
		{
			IsInRoom = false;

			ReportError(
				"Erro ao entrar na sala local: " +
				ex.Message
			);
		}
	}

	private void UpdateLocalRoomState(SocketRoomData roomData)
	{
		if (roomData == null)
			return;

		var match = roomData.Match;
		var score = roomData.Score;
		var players = roomData.Players;

		var myPlayer = playerId == "Jogador1"
			? players?.Jogador1
			: players?.Jogador2;

		IsMyPlayerConnected =
			myPlayer != null && myPlayer.Connected;

		var p1 = players?.Jogador1;
		var p2 = players?.Jogador2;

		CurrentRoom = new RoomState
		{
			Code = roomCode,

			Status = match != null
				? match.Status
				: "Waiting",

			CurrentTurnPlayer = match != null
				? match.CurrentTurnPlayer
				: "",

			TurnNumber = match != null
				? match.TurnNumber
				: 0,

			ShotsPerPlayer = match != null
				? Mathf.Max(1, match.ShotsPerPlayer)
				: 3,

			Player1Score = score != null
				? score.Jogador1
				: 0,

			Player2Score = score != null
				? score.Jogador2
				: 0,

			Player1Connected = p1 != null && p1.Connected,
			Player2Connected = p2 != null && p2.Connected,

			Winner = match != null
				? match.Winner
				: "",

			TurnStartedAt = match != null
				? match.TurnStartedAt
				: 0,

			TurnDuration = match != null
				? Mathf.Max(1, match.TurnDuration)
				: TURN_DURATION_SECONDS,

			RestartSignal = match != null
				? match.RestartSignal
				: 0
		};

		// Detecta o sinal de reinicialização enviado pelo servidor.
		if (!restartSignalInitialized)
		{
			lastRestartSignal = CurrentRoom.RestartSignal;

			restartSignalInitialized = true;
		}
		else if (CurrentRoom.RestartSignal > lastRestartSignal)
		{
			lastRestartSignal = CurrentRoom.RestartSignal;

			OnMatchRestartRequested?.Invoke();
		}
		else if (CurrentRoom.RestartSignal < lastRestartSignal)
		{
			lastRestartSignal = CurrentRoom.RestartSignal;
		}

		OnRoomUpdated?.Invoke(CurrentRoom);
	}

	public bool IsMyTurn()
	{
		return IsConnected &&
			IsInRoom &&
			IsMyPlayerConnected &&
			CurrentRoom != null &&
			CurrentRoom.Status == "Playing" &&
			CurrentRoom.CurrentTurnPlayer == playerId;
	}

	#endregion

	#region Chutes

	public async Task<bool> SubmitShot(bool goal)
	{
		if (!IsConnected ||
			!IsInRoom ||
			!IsMyPlayerConnected ||
			CurrentRoom == null ||
			!IsMyTurn())
		{
			return false;
		}

		return await SubmitShotLocalAsync(goal);
	}

	private async Task<bool> SubmitShotLocalAsync(bool goal)
	{
		try
		{
			string shotId = Guid.NewGuid().ToString("N");

			string payload = JsonUtility.ToJson(
				new ShotPayload
				{
					playerId = playerId,
					shotId = shotId,
					goal = goal,
					turnNumber = CurrentRoom.TurnNumber
				}
			);

			using (UnityWebRequest req = new UnityWebRequest(
				$"{localServerUrl}/api/match/shot",
				"POST"))
			{
				byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);

				req.uploadHandler = new UploadHandlerRaw(bodyRaw);
				req.downloadHandler = new DownloadHandlerBuffer();

				req.SetRequestHeader(
					"Content-Type",
					"application/json"
				);

				req.timeout = 10;

				await req.SendWebRequest();

				if (req.result == UnityWebRequest.Result.Success)
				{
					var response = JsonUtility.FromJson<ShotResponse>(
						req.downloadHandler.text
					);

					if (response != null &&
						response.success &&
						response.processed)
					{
						OnShotProcessed?.Invoke();

						return true;
					}

					if (response != null &&
						!string.IsNullOrEmpty(response.error))
					{
						ReportError(response.error);
					}
					else if (response != null && !response.processed)
					{
						Debug.LogWarning(
							"[FootballVR Local] O servidor não processou " +
							"o chute. Verifique turno e estado da partida."
						);
					}
				}
				else
				{
					ReportError(
						"Erro ao enviar chute: " +
						req.error
					);
				}
			}

			return false;
		}
		catch (Exception ex)
		{
			ReportError(
				"Erro ao enviar chute local: " +
				ex.Message
			);

			return false;
		}
	}

	#endregion

	#region Reconexão

	public async Task<bool> ForceReconnectAsync(string playerName)
	{
		return await ReconnectRoomAsync(playerName);
	}

	public async Task<bool> ReconnectRoomAsync(string playerName)
	{
		if (reconnectInProgress)
			return false;

		reconnectInProgress = true;

		try
		{
			if (!IsConnected)
			{
				ReportError(
					"Sem conexão com o servidor local."
				);

				return false;
			}

			if (!identityConfigured ||
				string.IsNullOrEmpty(playerId))
			{
				ReportError(
					"Identidade não configurada."
				);

				return false;
			}

			string name = string.IsNullOrWhiteSpace(playerName)
				? playerId
				: playerName.Trim();

			lastPlayerName = name;
			roomCode = FIXED_ROOM_CODE;

			await JoinRoomLocalAsync(name);

			if (!IsInRoom || !IsConnected)
				return false;

			LastError = "";

			Debug.Log(
				"[FootballVR Local] Sessão restaurada."
			);

			return true;
		}
		catch (Exception ex)
		{
			ReportError(
				"Falha ao recuperar conexão: " +
				ex.Message
			);

			return false;
		}
		finally
		{
			reconnectInProgress = false;
		}
	}

	#endregion

	#region Sair da sala

	public async Task LeaveRoom()
	{
		if (!IsInRoom)
			return;

		try
		{
			string payload = JsonUtility.ToJson(
				new LeavePayload
				{
					playerId = playerId
				}
			);

			using (UnityWebRequest req = new UnityWebRequest(
				$"{localServerUrl}/api/match/leave",
				"POST"))
			{
				byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);

				req.uploadHandler = new UploadHandlerRaw(bodyRaw);
				req.downloadHandler = new DownloadHandlerBuffer();

				req.SetRequestHeader(
					"Content-Type",
					"application/json"
				);

				req.timeout = 10;

				await req.SendWebRequest();

				if (req.result != UnityWebRequest.Result.Success)
				{
					Debug.LogWarning(
						"[FootballVR Local] Erro HTTP ao sair: " +
						req.error
					);
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning(
				"[FootballVR Local] Erro ao sair: " +
				ex.Message
			);
		}
		finally
		{
			if (isLocalSocketConfigured &&
				SocketIOManager.Instance?.Socket != null)
			{
				var socket = SocketIOManager.Instance.Socket;

				socket.Emit(
					"unsubscribe",
					new
					{
						path = "Rooms/" + FIXED_ROOM_CODE
					}
				);
			}

			roomCode = null;

			IsInRoom = false;
			IsMyPlayerConnected = false;

			CurrentRoom = null;

			restartSignalInitialized = false;
			lastRestartSignal = 0;

			OnRoomLeft?.Invoke();

			Debug.Log(
				"[FootballVR Local] Saiu da sala."
			);
		}
	}

	#endregion

	#region Erros e encerramento

	private void ReportError(string message)
	{
		// Evita mensagens de erro durante o modo solo.
		if (questFootKickIsPlayingAlone())
			return;

		LastError = message;

		Debug.LogError(
			"[FootballVR] " + message
		);

		OnError?.Invoke(message);
	}

	private bool questFootKickIsPlayingAlone()
	{
		return questFootKick != null &&
			questFootKick.PLAY_ALONE;
	}

	private void OnDestroy()
	{
		if (isLocalSocketConfigured &&
			SocketIOManager.Instance?.Socket != null)
		{
			var socket = SocketIOManager.Instance.Socket;

			socket.OnConnected -= OnLocalConnected;
			socket.OnDisconnected -= OnLocalDisconnected;
			socket.OnError -= OnLocalSocketError;

			// O listener "value" é mantido durante a vida
			// do SocketIOManager para evitar registros duplicados.
		}

		if (Instance == this)
			Instance = null;
	}

	#endregion

	#region Estado da partida

	[Serializable]
	public class RoomState
	{
		public string Code;
		public string Status;
		public string CurrentTurnPlayer;
		public string Winner;

		public int TurnNumber;
		public int ShotsPerPlayer;

		public int Player1Score;
		public int Player2Score;

		public bool Player1Connected;
		public bool Player2Connected;

		public int TurnDuration;

		public long TurnStartedAt;
		public long RestartSignal;
	}

	#endregion
}
