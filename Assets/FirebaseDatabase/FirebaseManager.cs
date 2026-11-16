using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

#if !DISABLE_FIREBASE
using Firebase;
using Firebase.Auth;
using Firebase.Database;
#endif

public class FirebaseManager : MonoBehaviour
{
	public static FirebaseManager Instance;

	[Header("Configuração de Modo")]
	[Tooltip("Se verdadeiro, usa o servidor Node.js local em vez do Firebase.")]
	public bool useLocalMode = false;

	[Tooltip("URL do servidor Node.js (ex: http://192.168.1.100:3001 ou http://localhost:3001)")]
	public string localServerUrl = "http://localhost:3001";

#if !DISABLE_FIREBASE
	private const string DATABASE_URL = "https://elasnojogo-7a990-default-rtdb.firebaseio.com/";
#endif

	public string FIXED_ROOM_CODE = "ARENA01";
	public const int TURN_DURATION_SECONDS = 30;

#if !DISABLE_FIREBASE
	private FirebaseAuth auth;
	private FirebaseDatabase database;
	private DatabaseReference reference;
#endif

	private string authUid;
	public string playerId;
	private bool identityConfigured;
	private string roomCode;

#if !DISABLE_FIREBASE
	private DatabaseReference roomReference;
	private DatabaseReference actionsReference;
	private EventHandler<ValueChangedEventArgs> roomListener;
	private EventHandler<ChildChangedEventArgs> actionListener;
	private EventHandler<ValueChangedEventArgs> connectionListener;
	private EventHandler<ValueChangedEventArgs> serverTimeOffsetListener;
#endif

	private bool isLocalSocketConfigured = false;

	private long serverTimeOffset;
	private bool hasServerTimeOffset;
	private bool firebaseReadyEventSent;
	private bool initializationInProgress;
	private bool reconnectInProgress;
	private bool forceReconnectInProgress;
	private string lastPlayerName;
	private int timeoutProcessingTurn = -1;

	public bool IsInitialized { get; private set; }
	public bool IsConnected { get; private set; }
	public bool IsInRoom { get; private set; }
	public bool IsMyPlayerConnected { get; private set; }
	public bool InitializationInProgress => initializationInProgress;
	public bool HasRoomListener => useLocalMode ? isLocalSocketConfigured : roomListener != null;
	public bool HasActionListener => useLocalMode ? isLocalSocketConfigured : actionListener != null;
	public string LastError { get; private set; } = "";

	public long ServerTimeOffset => serverTimeOffset;
	public bool HasServerTimeOffset => hasServerTimeOffset;

	public string PlayerId => playerId;
	public string AuthUid => authUid;
	public string RoomCode => roomCode;
	public RoomState CurrentRoom { get; private set; }

	public event Action OnFirebaseReady;
	public event Action<RoomState> OnRoomUpdated;
	public event Action<string> OnError;
	public event Action OnRoomJoined;
	public event Action OnRoomLeft;
	public event Action OnShotProcessed;
	public event Action OnMatchRestartRequested;

	private long lastRestartSignal = 0;
	private bool restartSignalInitialized = false;

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;
		DontDestroyOnLoad(gameObject);
	}

	private async void Start() { await InitializeFirebaseAsync(); }

	private void Update()
	{
		if (!IsConnected || !IsInRoom || CurrentRoom == null) return;
		if (CurrentRoom.Status != "Playing" || !hasServerTimeOffset) return;
		if (CurrentRoom.TurnStartedAt <= 0 || CurrentRoom.TurnDuration <= 0) return;

		long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + serverTimeOffset;
		long turnEnd = CurrentRoom.TurnStartedAt + (CurrentRoom.TurnDuration * 1000L);

		if (serverNow >= turnEnd)
			_ = ProcessExpiredTurn(CurrentRoom.TurnNumber);
	}

	public async Task<bool> InitializeFirebaseAsync()
	{
		if (IsInitialized) return true;
		if (initializationInProgress)
		{
			for (int i = 0; i < 600 && initializationInProgress; i++) await Task.Delay(100);
			return IsInitialized;
		}

		initializationInProgress = true;
		try
		{
			if (useLocalMode)
			{
				await InitializeLocalModeAsync();
			}
			else
			{
#if !DISABLE_FIREBASE
				if (Application.internetReachability == NetworkReachability.NotReachable)
				{
					ReportError("Conecte-se à internet para jogar.");
					return false;
				}

				DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
				if (dependencyStatus != DependencyStatus.Available)
				{
					ReportError("Dependências Firebase indisponíveis: " + dependencyStatus);
					return false;
				}

				FirebaseApp app = FirebaseApp.DefaultInstance;
				auth = FirebaseAuth.DefaultInstance;
				database = FirebaseDatabase.GetInstance(app, DATABASE_URL);
				reference = database.RootReference;

				if (auth.CurrentUser == null) await auth.SignInAnonymouslyAsync();
				authUid = auth.CurrentUser.UserId;
				IsInitialized = true;
				LastError = "";
				Debug.Log("[FootballVR] Firebase inicializado. Auth UID: " + authUid);
				MonitorConnection();
#else
                ReportError("Firebase desativado nas definições de compilação e useLocalMode é falso.");
                return false;
#endif
			}
			return true;
		}
		catch (Exception ex)
		{
			ReportError("Erro ao inicializar: " + ex);
			return false;
		}
		finally
		{
			initializationInProgress = false;
		}
	}

	private async Task InitializeLocalModeAsync()
	{
		Debug.Log("[FootballVR] Inicializando Modo Local (Node.js)...");

		await SyncLocalTimeAsync();

		if (SocketIOManager.Instance == null || SocketIOManager.Instance.Socket == null)
		{
			ReportError("SocketIOManager ou Socket não encontrado na cena. Adicione-o para o Modo Local.");
			return;
		}

		// O TRUQUE DO 'var': O compilador infere o tipo exato aqui, 
		// permitindo o uso de '+=' e '.Emit' sem erros de namespace ou 'dynamic'.
		var s = SocketIOManager.Instance.Socket;

		s.OnConnected += OnLocalConnected;
		s.OnDisconnected += OnLocalDisconnected;
		s.OnError += (SocketIOUnity.Runtime.SocketError err) => {
			Debug.LogError("[FootballVR Local] Socket Erro: " + err.Message);
		};

		s.On("value", (string msg) =>
		{
			try
			{
				var payload = JsonUtility.FromJson<SocketPayload>(msg);
				if (payload != null && (payload.path == "Rooms/" + FIXED_ROOM_CODE || payload.path == "Rooms"))
				{
					UpdateLocalRoomState(payload.value);
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[FootballVR Local] Erro ao processar socket: " + ex.Message);
			}
		});

		isLocalSocketConfigured = true;

		string wsUrl = localServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
		Debug.Log("[FootballVR Local] Conectando em: " + wsUrl);
		s.Connect(wsUrl);

		for (int i = 0; i < 50 && !IsConnected; i++) await Task.Delay(100);

		if (!IsConnected)
		{
			ReportError("Não foi possível conectar ao servidor local em " + wsUrl);
			return;
		}

		IsInitialized = true;
		Debug.Log("[FootballVR] Modo Local inicializado com sucesso.");
	}

	private void OnLocalConnected()
	{
		IsConnected = true;
		Debug.Log("[FootballVR Local] Socket conectado ao servidor!");
		if (!firebaseReadyEventSent)
		{
			firebaseReadyEventSent = true;
			OnFirebaseReady?.Invoke();
		}
		if (IsInRoom && !string.IsNullOrEmpty(lastPlayerName))
		{
			_ = ReconnectRoomAsync(lastPlayerName);
		}
	}

	private void OnLocalDisconnected()
	{
		IsConnected = false;
		Debug.LogWarning("[FootballVR Local] Desconectado do servidor.");
	}

	private async Task SyncLocalTimeAsync()
	{
		try
		{
			long localBefore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			using (UnityWebRequest req = UnityWebRequest.Get($"{localServerUrl}/api/time"))
			{
				await req.SendWebRequest();
				if (req.result == UnityWebRequest.Result.Success)
				{
					long localAfter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					var json = JsonUtility.FromJson<TimeResponse>(req.downloadHandler.text);
					long roundTrip = localAfter - localBefore;
					long estimatedLocalAtServer = localBefore + (roundTrip / 2);
					serverTimeOffset = json.serverTime - estimatedLocalAtServer;
					hasServerTimeOffset = true;
					Debug.Log($"[FootballVR Local] ServerTimeOffset sincronizado: {serverTimeOffset}ms");
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[FootballVR Local] Falha ao sincronizar tempo: " + ex.Message);
		}
	}

	[Serializable] private class TimeResponse { public long serverTime; }
	[Serializable] private class ShotResponse { public bool success; public bool processed; public string error; }
	[Serializable] private class JoinResponse { public bool success; public string error; }

	[Serializable] public class SocketPayload { public string path; public SocketRoomData value; }
	[Serializable] public class SocketRoomData { public SocketMatchData Match; public SocketScoreData Score; public SocketPlayersData Players; }
	[Serializable] public class SocketMatchData { public string Status; public string CurrentTurnPlayer; public int TurnNumber; public int ShotsPerPlayer; public string Winner; public long TurnStartedAt; public int TurnDuration; public long RestartSignal; }
	[Serializable] public class SocketScoreData { public int Jogador1; public int Jogador2; }
	[Serializable] public class SocketPlayersData { public SocketPlayerData Jogador1; public SocketPlayerData Jogador2; }
	[Serializable] public class SocketPlayerData { public string Name; public bool Connected; }

	public void ConfigureIdentity(string fixedPlayerId)
	{
		if (IsInRoom)
		{
			if (playerId == fixedPlayerId) return;
			Debug.LogError("[FootballVR] Não é possível trocar a identidade depois de entrar na sala.");
			return;
		}

		if (fixedPlayerId != "Jogador1" && fixedPlayerId != "Jogador2")
		{
			ReportError("Identidade inválida. Use apenas 'Jogador1' ou 'Jogador2'.");
			identityConfigured = false;
			return;
		}

		playerId = fixedPlayerId;
		identityConfigured = true;
		Debug.Log("[FootballVR] Identidade fixa configurada: " + playerId);
	}

#if !DISABLE_FIREBASE
	private void MonitorConnection()
	{
		if (reference == null) return;

		DatabaseReference connectedReference = reference.Child(".info").Child("connected");
		if (connectionListener != null) connectedReference.ValueChanged -= connectionListener;
		connectionListener = (sender, args) =>
		{
			IsConnected = args.Snapshot.Exists && args.Snapshot.Value is bool && (bool)args.Snapshot.Value;
			if (IsConnected && !firebaseReadyEventSent)
			{
				firebaseReadyEventSent = true;
				OnFirebaseReady?.Invoke();
			}
			if (IsConnected && IsInRoom && !forceReconnectInProgress)
				_ = ReconnectRoomAsync(lastPlayerName);
		};
		connectedReference.ValueChanged += connectionListener;

		DatabaseReference offsetReference = reference.Child(".info").Child("serverTimeOffset");
		if (serverTimeOffsetListener != null) offsetReference.ValueChanged -= serverTimeOffsetListener;
		serverTimeOffsetListener = (sender, args) =>
		{
			if (args.Snapshot.Exists && args.Snapshot.Value != null)
			{
				try
				{
					serverTimeOffset = Convert.ToInt64(args.Snapshot.Value);
					hasServerTimeOffset = true;
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[FootballVR] Erro serverTimeOffset: " + ex.Message);
				}
			}
		};
		offsetReference.ValueChanged += serverTimeOffsetListener;
	}
#endif

	public async Task JoinRoom(string playerName)
	{
		if (!IsInitialized || !identityConfigured)
		{
			ReportError("Sistema não inicializado ou identidade não configurada.");
			return;
		}
		if (IsInRoom) return;

		lastPlayerName = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName.Trim();
		roomCode = FIXED_ROOM_CODE;

		if (useLocalMode)
		{
			await JoinRoomLocalAsync(lastPlayerName);
		}
		else
		{
#if !DISABLE_FIREBASE
			await JoinRoomFirebaseAsync(playerName);
#endif
		}
	}

	private async Task JoinRoomLocalAsync(string playerName)
	{
		try
		{
			// Envia como JSON em vez de WWWForm
			string jsonPayload = $"{{\"playerId\":\"{playerId}\", \"playerName\":\"{playerName}\"}}";

			using (UnityWebRequest req = new UnityWebRequest($"{localServerUrl}/api/match/join", "POST"))
			{
				byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
				req.uploadHandler = new UploadHandlerRaw(bodyRaw);
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");

				await req.SendWebRequest();

				if (req.result != UnityWebRequest.Result.Success)
				{
					ReportError("Falha ao entrar na sala local: " + req.error);
					return;
				}

				string responseText = req.downloadHandler.text;
				var response = JsonUtility.FromJson<JoinResponse>(responseText);

				if (response == null || !response.success)
				{
					ReportError("Não foi possível entrar: " + (response != null ? response.error : "Resposta inválida do servidor."));
					return;
				}
			}

			IsInRoom = true;

			if (isLocalSocketConfigured && SocketIOManager.Instance?.Socket != null)
			{
				var s = SocketIOManager.Instance.Socket;
				s.Emit("identify", new { playerId = playerId });
				s.Emit("subscribe", new { path = "Rooms/" + FIXED_ROOM_CODE });
				Debug.Log("[FootballVR Local] Inscrito na sala ARENA01 via Socket.");
			}

			restartSignalInitialized = false;
			lastRestartSignal = 0;
			OnRoomJoined?.Invoke();
			LastError = "";
			Debug.Log("[FootballVR Local] Entrou na sala como " + playerId);
		}
		catch (Exception ex)
		{
			IsInRoom = false;
			ReportError("Erro ao entrar na sala local: " + ex.Message);
		}
	}

#if !DISABLE_FIREBASE
	private async Task JoinRoomFirebaseAsync(string playerName)
	{
		DatabaseReference targetRoom = reference.Child("Rooms").Child(roomCode);
		try
		{
			DatabaseReference myConnectedRef = targetRoom.Child("Players").Child(playerId).Child("Connected");
			await myConnectedRef.OnDisconnect().SetValue(false);

			DataSnapshot transactionSnapshot = await targetRoom.RunTransaction(data =>
			{
				if (data.Value == null) data.Value = CreateInitialRoom();
				MutableData match = data.Child("Match");
				EnsureMatchDefaults(match);
				if (ReadString(match.Child("Status")) == "Finished") return TransactionResult.Abort();

				MutableData player = data.Child("Players").Child(playerId);
				player.Child("Name").Value = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName;
				player.Child("Connected").Value = true;

				if (data.Child("Score").Value == null) data.Child("Score").Value = new Dictionary<string, object> { { "Jogador1", 0 }, { "Jogador2", 0 } };
				if (data.Child("Actions").Value == null) data.Child("Actions").Value = new Dictionary<string, object>();
				if (data.Child("ProcessedActions").Value == null) data.Child("ProcessedActions").Value = new Dictionary<string, object>();

				return TransactionResult.Success(data);
			});

			if (transactionSnapshot == null)
			{
				await myConnectedRef.OnDisconnect().Cancel();
				ReportError("Não foi possível entrar. A partida pode estar finalizada.");
				return;
			}

			IsInRoom = true;
			roomReference = targetRoom;
			actionsReference = roomReference.Child("Actions");
			restartSignalInitialized = false;
			lastRestartSignal = 0;
			AttachRoomListeners();
			await UpdatePresence();
			OnRoomJoined?.Invoke();
			LastError = "";
			Debug.Log("[FootballVR] Entrou na sala como " + playerId);
		}
		catch (Exception ex)
		{
			IsInRoom = false;
			roomReference = null;
			ReportError("Erro ao entrar na sala: " + ex);
		}
	}

	private async Task UpdatePresence()
	{
		if (!IsInRoom || !IsConnected || roomReference == null || string.IsNullOrEmpty(playerId)) return;
		try
		{
			DatabaseReference myConnectedRef = roomReference.Child("Players").Child(playerId).Child("Connected");
			await myConnectedRef.OnDisconnect().SetValue(false);
			await myConnectedRef.SetValueAsync(true);
		}
		catch (Exception ex) { Debug.LogError("[FootballVR] Erro ao registrar presença: " + ex); }
	}
#endif

	private void UpdateLocalRoomState(SocketRoomData roomData)
	{
		if (roomData == null) return;

		var match = roomData.Match;
		var score = roomData.Score;
		var players = roomData.Players;

		var myPlayer = playerId == "Jogador1" ? players?.Jogador1 : players?.Jogador2;
		IsMyPlayerConnected = myPlayer != null && myPlayer.Connected;

		var p1 = players?.Jogador1;
		var p2 = players?.Jogador2;

		CurrentRoom = new RoomState
		{
			Code = roomCode,
			Status = match != null ? match.Status : "Waiting",
			CurrentTurnPlayer = match != null ? match.CurrentTurnPlayer : "",
			TurnNumber = match != null ? match.TurnNumber : 0,
			ShotsPerPlayer = match != null ? Mathf.Max(1, match.ShotsPerPlayer) : 5,
			Player1Score = score != null ? score.Jogador1 : 0,
			Player2Score = score != null ? score.Jogador2 : 0,
			Player1Connected = p1 != null && p1.Connected,
			Player2Connected = p2 != null && p2.Connected,
			Winner = match != null ? match.Winner : "",
			TurnStartedAt = match != null ? match.TurnStartedAt : 0,
			TurnDuration = match != null ? Mathf.Max(1, match.TurnDuration) : 30,
			RestartSignal = match != null ? match.RestartSignal : 0
		};

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

#if !DISABLE_FIREBASE
	private void AttachRoomListeners()
	{
		DetachRoomListeners();
		if (roomReference == null) return;

		roomListener = (sender, args) => { if (args.Snapshot.Exists) UpdateRoomState(args.Snapshot); };
		roomReference.ValueChanged += roomListener;

		actionListener = (sender, args) =>
		{
			if (args.Snapshot != null && !string.IsNullOrEmpty(args.Snapshot.Key))
				_ = ProcessShot(args.Snapshot.Key);
		};
		actionsReference.ChildAdded += actionListener;
	}

	private void UpdateRoomState(DataSnapshot snapshot)
	{
		DataSnapshot match = snapshot.Child("Match");
		DataSnapshot score = snapshot.Child("Score");
		DataSnapshot myPlayer = snapshot.Child("Players").Child(playerId);

		IsMyPlayerConnected = ReadBool(myPlayer.Child("Connected"));

		CurrentRoom = new RoomState
		{
			Code = roomCode,
			Status = ReadString(match.Child("Status")),
			CurrentTurnPlayer = ReadString(match.Child("CurrentTurnPlayer")),
			TurnNumber = ReadInt(match.Child("TurnNumber")),
			ShotsPerPlayer = Mathf.Max(1, ReadInt(match.Child("ShotsPerPlayer"))),
			Player1Score = ReadInt(score.Child("Jogador1")),
			Player2Score = ReadInt(score.Child("Jogador2")),
			Player1Connected = ReadBool(snapshot.Child("Players").Child("Jogador1").Child("Connected")),
			Player2Connected = ReadBool(snapshot.Child("Players").Child("Jogador2").Child("Connected")),
			Winner = ReadString(match.Child("Winner")),
			TurnStartedAt = ReadLong(match.Child("TurnStartedAt")),
			TurnDuration = Mathf.Max(1, ReadInt(match.Child("TurnDuration")))
		};

		CurrentRoom.RestartSignal = ReadLong(match.Child("RestartSignal"));

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
#endif

	public bool IsMyTurn()
	{
		return IsConnected && IsInRoom && IsMyPlayerConnected && CurrentRoom != null &&
			   CurrentRoom.Status == "Playing" && CurrentRoom.CurrentTurnPlayer == playerId;
	}

	public async Task<bool> SubmitShot(bool goal)
	{
		if (!IsConnected || !IsInRoom || !IsMyPlayerConnected || CurrentRoom == null || !IsMyTurn())
			return false;

		if (useLocalMode)
		{
			return await SubmitShotLocalAsync(goal);
		}
		else
		{
#if !DISABLE_FIREBASE
			return await SubmitShotFirebaseAsync(goal);
#endif
		}
		return false;
	}

	private async Task<bool> SubmitShotLocalAsync(bool goal)
	{
		try
		{
			string shotId = Guid.NewGuid().ToString("N");
			var payload = $"{{\"playerId\":\"{playerId}\", \"shotId\":\"{shotId}\", \"goal\":{goal.ToString().ToLower()}, \"turnNumber\":{CurrentRoom.TurnNumber}}}";

			using (UnityWebRequest req = new UnityWebRequest($"{localServerUrl}/api/match/shot", "POST"))
			{
				byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(payload);
				req.uploadHandler = new UploadHandlerRaw(bodyRaw);
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");

				await req.SendWebRequest();
				if (req.result == UnityWebRequest.Result.Success)
				{
					var response = JsonUtility.FromJson<ShotResponse>(req.downloadHandler.text);
					if (response.success && response.processed)
					{
						OnShotProcessed?.Invoke();
						return true;
					}
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			ReportError("Erro ao enviar chute local: " + ex.Message);
			return false;
		}
	}

#if !DISABLE_FIREBASE
	private async Task<bool> SubmitShotFirebaseAsync(bool goal)
	{
		try
		{
			string shotId = Guid.NewGuid().ToString("N");
			Dictionary<string, object> shotData = new Dictionary<string, object>
			{
				{ "ShotId", shotId }, { "PlayerId", playerId }, { "Goal", goal },
				{ "TurnNumber", CurrentRoom.TurnNumber }, { "CreatedAt", ServerValue.Timestamp }
			};
			await actionsReference.Child(shotId).SetValueAsync(shotData);
			return true;
		}
		catch (Exception ex)
		{
			ReportError("Erro ao enviar chute: " + ex.Message);
			return false;
		}
	}

	private async Task ProcessShot(string shotId)
	{
		if (!IsConnected || !IsMyPlayerConnected || playerId != "Jogador1" || roomReference == null || string.IsNullOrEmpty(shotId)) return;

		try
		{
			await roomReference.RunTransaction(data =>
			{
				MutableData action = data.Child("Actions").Child(shotId);
				if (action.Value == null) return TransactionResult.Abort();

				MutableData processed = data.Child("ProcessedActions").Child(shotId);
				if (processed.Value != null) { action.Value = null; return TransactionResult.Success(data); }

				MutableData match = data.Child("Match");
				string status = ReadString(match.Child("Status"));
				string currentTurn = ReadString(match.Child("CurrentTurnPlayer"));
				string actionPlayerId = ReadString(action.Child("PlayerId"));
				int actionTurn = ReadInt(action.Child("TurnNumber"));
				int currentTurnNumber = ReadInt(match.Child("TurnNumber"));

				if (status != "Playing" || currentTurn != actionPlayerId || actionTurn != currentTurnNumber)
				{ action.Value = null; return TransactionResult.Success(data); }

				long turnStartedAt = ReadLong(match.Child("TurnStartedAt"));
				int turnDuration = ReadInt(match.Child("TurnDuration"));

				if (hasServerTimeOffset)
				{
					long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + serverTimeOffset;
					if (serverNow >= turnStartedAt + (turnDuration * 1000L)) { action.Value = null; return TransactionResult.Success(data); }
				}

				bool isGoal = ReadBool(action.Child("Goal"));
				if (isGoal)
				{
					MutableData score = data.Child("Score").Child(actionPlayerId);
					score.Value = ReadInt(score) + 1;
				}

				int nextTurn = currentTurnNumber + 1;
				int shotsPerPlayer = Mathf.Max(1, ReadInt(match.Child("ShotsPerPlayer")));
				match.Child("TurnNumber").Value = nextTurn;
				processed.Value = true;
				action.Value = null;

				if (nextTurn >= shotsPerPlayer * 2) FinishMatch(data, match);
				else
				{
					string nextPlayer = currentTurnNumber % 2 == 0 ? "Jogador2" : "Jogador1";
					match.Child("CurrentTurnPlayer").Value = nextPlayer;
					match.Child("TurnStartedAt").Value = ServerValue.Timestamp;
				}
				return TransactionResult.Success(data);
			});

			DataSnapshot processedSnapshot = await roomReference.Child("ProcessedActions").Child(shotId).GetValueAsync();
			if (processedSnapshot.Exists) { Debug.Log("[FootballVR] Chute processado: " + shotId); OnShotProcessed?.Invoke(); }
		}
		catch (Exception ex) { Debug.LogError("[FootballVR] Erro ao processar chute: " + ex); }
	}

	private void FinishMatch(MutableData data, MutableData match)
	{
		match.Child("Status").Value = "Finished";
		int p1Score = ReadInt(data.Child("Score").Child("Jogador1"));
		int p2Score = ReadInt(data.Child("Score").Child("Jogador2"));
		string winner = "Draw";
		if (p1Score > p2Score) winner = "Jogador1";
		else if (p2Score > p1Score) winner = "Jogador2";

		match.Child("Winner").Value = winner;
		match.Child("CurrentTurnPlayer").Value = "";
		match.Child("TurnStartedAt").Value = 0;
	}
#endif

	private async Task ProcessExpiredTurn(int expectedTurn)
	{
		if (!IsConnected || !IsMyPlayerConnected || timeoutProcessingTurn == expectedTurn) return;

		if (useLocalMode)
		{
			timeoutProcessingTurn = expectedTurn;
			await Task.Delay(2000);
			timeoutProcessingTurn = -1;
			return;
		}

#if !DISABLE_FIREBASE
		timeoutProcessingTurn = expectedTurn;
		try
		{
			await roomReference.RunTransaction(data =>
			{
				MutableData match = data.Child("Match");
				if (ReadString(match.Child("Status")) != "Playing") return TransactionResult.Abort();
				if (ReadInt(match.Child("TurnNumber")) != expectedTurn) return TransactionResult.Abort();

				long startedAt = ReadLong(match.Child("TurnStartedAt"));
				int duration = ReadInt(match.Child("TurnDuration"));
				if (startedAt <= 0 || duration <= 0 || !hasServerTimeOffset) return TransactionResult.Abort();

				long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + serverTimeOffset;
				if (serverNow < startedAt + (duration * 1000L)) return TransactionResult.Abort();

				int nextTurn = expectedTurn + 1;
				int shotsPerPlayer = Mathf.Max(1, ReadInt(match.Child("ShotsPerPlayer")));
				match.Child("TurnNumber").Value = nextTurn;
				data.Child("ProcessedActions").Child("timeout_" + expectedTurn).Value = true;

				if (nextTurn >= shotsPerPlayer * 2) FinishMatch(data, match);
				else
				{
					string nextPlayer = expectedTurn % 2 == 0 ? "Jogador2" : "Jogador1";
					match.Child("CurrentTurnPlayer").Value = nextPlayer;
					match.Child("TurnStartedAt").Value = ServerValue.Timestamp;
				}
				return TransactionResult.Success(data);
			});
		}
		catch (Exception ex) { Debug.LogError("[FootballVR] Erro ao processar tempo esgotado: " + ex); }
		finally { if (timeoutProcessingTurn == expectedTurn) timeoutProcessingTurn = -1; }
#endif
	}

	public async Task LeaveRoom()
	{
		if (!IsInRoom) return;

		if (useLocalMode)
		{
			try
			{
				var payload = $"{{\"playerId\":\"{playerId}\"}}";
				using (UnityWebRequest req = new UnityWebRequest($"{localServerUrl}/api/match/leave", "POST"))
				{
					byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(payload);
					req.uploadHandler = new UploadHandlerRaw(bodyRaw);
					req.downloadHandler = new DownloadHandlerBuffer();
					req.SetRequestHeader("Content-Type", "application/json");
					await req.SendWebRequest();
				}
			}
			catch (Exception ex) { Debug.LogWarning("[FootballVR Local] Erro ao sair: " + ex.Message); }
			finally
			{
				if (isLocalSocketConfigured && SocketIOManager.Instance?.Socket != null)
				{
					var s = SocketIOManager.Instance.Socket;
					s.Emit("unsubscribe", new { path = "Rooms/" + FIXED_ROOM_CODE });
				}
				roomCode = null;
				IsInRoom = false;
				IsMyPlayerConnected = false;
				CurrentRoom = null;
				OnRoomLeft?.Invoke();
			}
		}
		else
		{
#if !DISABLE_FIREBASE
			DatabaseReference leavingRoom = roomReference;
			try
			{
				if (IsConnected)
				{
					await leavingRoom.Child("Players").Child(playerId).Child("Connected").OnDisconnect().Cancel();
					await leavingRoom.RunTransaction(data =>
					{
						MutableData player = data.Child("Players").Child(playerId);
						player.Child("Connected").Value = false;
						if (ReadString(data.Child("Match").Child("Status")) == "Playing")
						{
							data.Child("Match").Child("Status").Value = "Waiting";
							data.Child("Match").Child("CurrentTurnPlayer").Value = "";
							data.Child("Match").Child("TurnStartedAt").Value = 0;
						}
						return TransactionResult.Success(data);
					});
				}
			}
			catch (Exception ex) { Debug.LogWarning("[FootballVR] Erro ao sair: " + ex.Message); }
			finally
			{
				DetachRoomListeners();
				roomCode = null;
				roomReference = null;
				actionsReference = null;
				IsInRoom = false;
				IsMyPlayerConnected = false;
				CurrentRoom = null;
				OnRoomLeft?.Invoke();
			}
#endif
		}
	}

#if !DISABLE_FIREBASE
	private void DetachRoomListeners()
	{
		if (roomReference != null && roomListener != null) { roomReference.ValueChanged -= roomListener; roomListener = null; }
		if (actionsReference != null && actionListener != null) { actionsReference.ChildAdded -= actionListener; actionListener = null; }
	}

	public async Task<bool> ForceReconnectAsync(string playerName)
	{
		if (useLocalMode) return await ReconnectRoomAsync(playerName);

#if !DISABLE_FIREBASE
		if (IsInRoom && CurrentRoom != null && CurrentRoom.Status == "Playing")
		{
			ReportError("Force Reconnect bloqueado durante a partida.");
			return false;
		}
		if (forceReconnectInProgress) return false;
		forceReconnectInProgress = true;
		try
		{
			if (!IsInitialized) { if (!await InitializeFirebaseAsync()) return false; }
			if (database == null) { ReportError("Instância do Firebase indisponível."); return false; }

			Debug.Log("[FootballVR] Reiniciando conexão do Realtime Database...");
			database.GoOffline();
			await Task.Delay(500);
			database.GoOnline();
			return await ReconnectRoomAsync(playerName);
		}
		catch (Exception ex) { ReportError("Falha no Force Reconnect: " + ex.Message); return false; }
		finally { forceReconnectInProgress = false; }
#else
        return false;
#endif
	}

	public async Task<bool> ReconnectRoomAsync(string playerName)
	{
		if (reconnectInProgress) return false;
		reconnectInProgress = true;
		try
		{
			if (!IsInitialized) { if (!await InitializeFirebaseAsync()) return false; }

			if (useLocalMode)
			{
				if (!IsConnected)
				{
					ReportError("Sem conexão com o servidor local.");
					return false;
				}

				if (!identityConfigured || string.IsNullOrEmpty(playerId))
				{
					ReportError("Identidade não configurada.");
					return false;
				}

				string name = string.IsNullOrWhiteSpace(playerName)
					? playerId
					: playerName.Trim();

				lastPlayerName = name;
				roomCode = FIXED_ROOM_CODE;

				// Executa novamente o join HTTP para restaurar a presença.
				// A rota de join não deve reiniciar a partida.
				await JoinRoomLocalAsync(name);

				if (!IsInRoom || !IsConnected)
					return false;

				LastError = "";

				Debug.Log("[FootballVR Local] Sessão e inscrição da sala restauradas.");

				return true;
			}

#if !DISABLE_FIREBASE
			for (int i = 0; i < 150 && !IsConnected; i++) await Task.Delay(100);
			if (!IsConnected) { ReportError("Firebase inicializado, mas sem conexão."); return false; }
			if (!identityConfigured || string.IsNullOrEmpty(playerId)) { ReportError("Identidade não configurada."); return false; }

			if (!IsInRoom || roomReference == null)
			{
				await JoinRoom(string.IsNullOrWhiteSpace(playerName) ? playerId : playerName.Trim());
				return IsInRoom;
			}

			lastPlayerName = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName.Trim();
			DatabaseReference targetRoom = roomReference;
			DatabaseReference myConnectedRef = targetRoom.Child("Players").Child(playerId).Child("Connected");
			await myConnectedRef.OnDisconnect().SetValue(false);

			DataSnapshot result = await targetRoom.RunTransaction(data =>
			{
				if (data.Value == null) data.Value = CreateInitialRoom();
				MutableData match = data.Child("Match");
				EnsureMatchDefaults(match);
				if (ReadString(match.Child("Status")) == "Finished") return TransactionResult.Abort();

				MutableData player = data.Child("Players").Child(playerId);
				player.Child("Name").Value = lastPlayerName;
				player.Child("Connected").Value = true;

				if (data.Child("Score").Value == null) data.Child("Score").Value = new Dictionary<string, object> { { "Jogador1", 0 }, { "Jogador2", 0 } };
				if (data.Child("Actions").Value == null) data.Child("Actions").Value = new Dictionary<string, object>();
				if (data.Child("ProcessedActions").Value == null) data.Child("ProcessedActions").Value = new Dictionary<string, object>();
				return TransactionResult.Success(data);
			});

			if (result == null) { ReportError("Não foi possível recuperar a presença."); return false; }

			roomCode = FIXED_ROOM_CODE;
			roomReference = targetRoom;
			actionsReference = targetRoom.Child("Actions");
			IsInRoom = true;
			IsMyPlayerConnected = true;
			AttachRoomListeners();
			await UpdatePresence();
			LastError = "";
			Debug.Log("[FootballVR] Conexão recuperada para " + playerId + ".");
			return true;
#endif
		}
		catch (Exception ex) { ReportError("Falha ao recuperar conexão/sala: " + ex.Message); return false; }
		finally { reconnectInProgress = false; }
	}

	private static Dictionary<string, object> CreateInitialRoom()
	{
		return new Dictionary<string, object>
		{
			{ "CreatedAt", ServerValue.Timestamp },
			{ "Match", CreateInitialMatch() },
			{ "Score", new Dictionary<string, object> { { "Jogador1", 0 }, { "Jogador2", 0 } } },
			{ "Players", new Dictionary<string, object>() },
			{ "Actions", new Dictionary<string, object>() },
			{ "ProcessedActions", new Dictionary<string, object>() }
		};
	}

	private static Dictionary<string, object> CreateInitialMatch()
	{
		return new Dictionary<string, object>
		{
			{ "Status", "Waiting" }, { "CurrentTurnPlayer", "" }, { "TurnNumber", 0 },
			{ "ShotsPerPlayer", 5 }, { "Winner", "" }, { "TurnStartedAt", 0 }, { "TurnDuration", TURN_DURATION_SECONDS }
		};
	}

	private static void EnsureMatchDefaults(MutableData match)
	{
		if (match.Child("Status").Value == null) match.Child("Status").Value = "Waiting";
		if (match.Child("CurrentTurnPlayer").Value == null) match.Child("CurrentTurnPlayer").Value = "";
		if (match.Child("TurnNumber").Value == null) match.Child("TurnNumber").Value = 0;
		if (match.Child("ShotsPerPlayer").Value == null) match.Child("ShotsPerPlayer").Value = 5;
		if (match.Child("Winner").Value == null) match.Child("Winner").Value = "";
		if (match.Child("TurnStartedAt").Value == null) match.Child("TurnStartedAt").Value = 0;
		if (match.Child("TurnDuration").Value == null) match.Child("TurnDuration").Value = TURN_DURATION_SECONDS;
	}

	private static string ReadString(MutableData data) => data == null || data.Value == null ? "" : data.Value.ToString();
	private static string ReadString(DataSnapshot data) => data == null || data.Value == null ? "" : data.Value.ToString();
	private static int ReadInt(MutableData data) => data == null || data.Value == null ? 0 : Convert.ToInt32(data.Value);
	private static int ReadInt(DataSnapshot data) => data == null || data.Value == null ? 0 : Convert.ToInt32(data.Value);
	private static bool ReadBool(MutableData data) => data != null && data.Value != null && Convert.ToBoolean(data.Value);
	private static bool ReadBool(DataSnapshot data) => data != null && data.Value != null && Convert.ToBoolean(data.Value);
	private static long ReadLong(MutableData data) => data == null || data.Value == null ? 0L : Convert.ToInt64(data.Value);
	private static long ReadLong(DataSnapshot data) => data == null || data.Value == null ? 0L : Convert.ToInt64(data.Value);
#endif

	private void ReportError(string message)
	{
		LastError = message;
		Debug.LogError("[FootballVR] " + message);
		OnError?.Invoke(message);
	}

	private void OnDestroy()
	{
#if !DISABLE_FIREBASE
		DetachRoomListeners();
#endif
		if (Instance == this) Instance = null;
	}

	[System.Serializable]
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
}