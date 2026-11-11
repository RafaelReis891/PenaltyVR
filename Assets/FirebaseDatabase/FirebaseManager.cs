using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;

public class FirebaseManager : MonoBehaviour
{
	public static FirebaseManager Instance;

	private const string DATABASE_URL = "https://elasnojogo-7a990-default-rtdb.firebaseio.com/";
	public string FIXED_ROOM_CODE = "ARENA01";
	public const int TURN_DURATION_SECONDS = 30;

	private FirebaseAuth auth;
	private FirebaseDatabase database;
	private DatabaseReference reference;

	private string authUid;
	public string playerId; // "Jogador1" ou "Jogador2" (Fixo)
	private bool identityConfigured;
	private string roomCode;

	private DatabaseReference roomReference;
	private DatabaseReference actionsReference;

	private EventHandler<ValueChangedEventArgs> roomListener;
	private EventHandler<ChildChangedEventArgs> actionListener;
	private EventHandler<ValueChangedEventArgs> connectionListener;
	private EventHandler<ValueChangedEventArgs> serverTimeOffsetListener;

	private long serverTimeOffset;
	private bool hasServerTimeOffset;
	private bool firebaseReadyEventSent;
	private int timeoutProcessingTurn = -1;

	public bool IsInitialized { get; private set; }
	public bool IsConnected { get; private set; }
	public bool IsInRoom { get; private set; }
	public bool IsMyPlayerConnected { get; private set; }

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

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;
		DontDestroyOnLoad(gameObject);
	}

	private async void Start() { await InitializeFirebase(); }

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

	private async Task InitializeFirebase()
	{
		try
		{
			if (Application.internetReachability == NetworkReachability.NotReachable)
			{
				ReportError("Conecte-se à internet para jogar.");
				return;
			}

			DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
			if (dependencyStatus != DependencyStatus.Available)
			{
				ReportError("Dependências Firebase indisponíveis: " + dependencyStatus);
				return;
			}

			FirebaseApp app = FirebaseApp.DefaultInstance;
			auth = FirebaseAuth.DefaultInstance;
			database = FirebaseDatabase.GetInstance(app, DATABASE_URL);
			reference = database.RootReference;

			if (auth.CurrentUser == null)
				await auth.SignInAnonymouslyAsync();

			authUid = auth.CurrentUser.UserId;
			IsInitialized = true;

			Debug.Log("[FootballVR] Firebase inicializado. Auth UID: " + authUid);
			MonitorConnection();
		}
		catch (Exception ex)
		{
			ReportError("Erro ao inicializar Firebase: " + ex);
		}
	}

	public void ConfigureIdentity(string fixedPlayerId)
	{
		if (IsInRoom)
		{
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

	private void MonitorConnection()
	{
		if (reference == null) return;

		DatabaseReference connectedReference = reference.Child(".info").Child("connected");
		connectionListener = (sender, args) =>
		{
			IsConnected = args.Snapshot.Exists && args.Snapshot.Value is bool && (bool)args.Snapshot.Value;
			if (IsConnected && !firebaseReadyEventSent)
			{
				firebaseReadyEventSent = true;
				OnFirebaseReady?.Invoke();
			}
			if (IsConnected && IsInRoom)
				_ = UpdatePresence();
		};
		connectedReference.ValueChanged += connectionListener;

		DatabaseReference offsetReference = reference.Child(".info").Child("serverTimeOffset");
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

	// PRESENÇA SIMPLIFICADA: apenas marca Connected = true/false
	private async Task UpdatePresence()
	{
		if (!IsInRoom || !IsConnected || roomReference == null || string.IsNullOrEmpty(playerId))
			return;

		try
		{
			DatabaseReference myConnectedRef = roomReference.Child("Players").Child(playerId).Child("Connected");

			// Quando a conexão cair, o Firebase marca automaticamente como false
			await myConnectedRef.OnDisconnect().SetValue(false);

			// Marca como conectado agora
			await myConnectedRef.SetValueAsync(true);

			Debug.Log("[FootballVR] Presença registrada: " + playerId + " conectado.");
		}
		catch (Exception ex)
		{
			Debug.LogError("[FootballVR] Erro ao registrar presença: " + ex);
		}
	}

	public async Task JoinRoom(string playerName)
	{
		if (!IsInitialized || !identityConfigured)
		{
			ReportError("Firebase não inicializado ou identidade não configurada.");
			return;
		}
		if (IsInRoom) return;

		roomCode = FIXED_ROOM_CODE;
		DatabaseReference targetRoom = reference.Child("Rooms").Child(roomCode);

		try
		{
			// Registra OnDisconnect ANTES de entrar (garante que se o Unity fechar, marca como desconectado)
			DatabaseReference myConnectedRef = targetRoom.Child("Players").Child(playerId).Child("Connected");
			await myConnectedRef.OnDisconnect().SetValue(false);

			DataSnapshot transactionSnapshot = await targetRoom.RunTransaction(data =>
			{
				// Cria a sala se não existir
				if (data.Value == null)
				{
					data.Value = CreateInitialRoom();
				}

				MutableData match = data.Child("Match");
				EnsureMatchDefaults(match);

				MutableData player = data.Child("Players").Child(playerId);

				// Se a partida está Finished e não é reset, não permite entrar
				if (ReadString(match.Child("Status")) == "Finished")
					return TransactionResult.Abort();

				// Registra/atualiza o jogador
				player.Child("Name").Value = string.IsNullOrWhiteSpace(playerName) ? playerId : playerName;
				player.Child("Connected").Value = true;

				// Garante que Score existe
				if (data.Child("Score").Value == null)
				{
					data.Child("Score").Value = new Dictionary<string, object>
					{
						{ "Jogador1", 0 },
						{ "Jogador2", 0 }
					};
				}
				if (data.Child("Actions").Value == null) data.Child("Actions").Value = new Dictionary<string, object>();
				if (data.Child("ProcessedActions").Value == null) data.Child("ProcessedActions").Value = new Dictionary<string, object>();

				return TransactionResult.Success(data);
			});

			bool transactionSucceeded = transactionSnapshot != null;

			if (!transactionSucceeded)
			{
				await myConnectedRef.OnDisconnect().Cancel();
				ReportError("Não foi possível entrar como " + playerId + ". A partida pode estar finalizada. Peça ao Admin para resetar.");
				return;
			}

			IsInRoom = true;
			roomReference = targetRoom;
			actionsReference = roomReference.Child("Actions");
			AttachRoomListeners();
			await UpdatePresence();
			OnRoomJoined?.Invoke();

			Debug.Log("[FootballVR] Entrou na sala como " + playerId);
		}
		catch (Exception ex)
		{
			IsInRoom = false;
			roomReference = null;
			ReportError("Erro ao entrar na sala: " + ex);
		}
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
			{ "Status", "Waiting" },
			{ "CurrentTurnPlayer", "" },
			{ "TurnNumber", 0 },
			{ "ShotsPerPlayer", 5 },
			{ "Winner", "" },
			{ "TurnStartedAt", 0 },
			{ "TurnDuration", TURN_DURATION_SECONDS }
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
		OnRoomUpdated?.Invoke(CurrentRoom);
	}

	public bool IsMyTurn()
	{
		return IsConnected && IsInRoom && IsMyPlayerConnected && CurrentRoom != null &&
			   CurrentRoom.Status == "Playing" && CurrentRoom.CurrentTurnPlayer == playerId;
	}

	public async Task<bool> SubmitShot(bool goal)
	{
		if (!IsConnected || !IsInRoom || !IsMyPlayerConnected || CurrentRoom == null || actionsReference == null || !IsMyTurn())
			return false;

		try
		{
			string shotId = Guid.NewGuid().ToString("N");
			Dictionary<string, object> shotData = new Dictionary<string, object>
			{
				{ "ShotId", shotId },
				{ "PlayerId", playerId },
				{ "Goal", goal },
				{ "TurnNumber", CurrentRoom.TurnNumber },
				{ "CreatedAt", ServerValue.Timestamp }
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
		if (!IsConnected || !IsMyPlayerConnected || roomReference == null || string.IsNullOrEmpty(shotId))
			return;

		try
		{
			await roomReference.RunTransaction(data =>
			{
				MutableData action = data.Child("Actions").Child(shotId);
				if (action.Value == null) return TransactionResult.Abort();

				MutableData processed = data.Child("ProcessedActions").Child(shotId);
				if (processed.Value != null)
				{
					action.Value = null;
					return TransactionResult.Success(data);
				}

				MutableData match = data.Child("Match");
				string status = ReadString(match.Child("Status"));
				string currentTurn = ReadString(match.Child("CurrentTurnPlayer"));
				string actionPlayerId = ReadString(action.Child("PlayerId"));
				int actionTurn = ReadInt(action.Child("TurnNumber"));
				int currentTurnNumber = ReadInt(match.Child("TurnNumber"));

				if (status != "Playing" || currentTurn != actionPlayerId || actionTurn != currentTurnNumber)
				{
					action.Value = null;
					return TransactionResult.Success(data);
				}

				long turnStartedAt = ReadLong(match.Child("TurnStartedAt"));
				int turnDuration = ReadInt(match.Child("TurnDuration"));

				if (hasServerTimeOffset)
				{
					long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + serverTimeOffset;
					if (serverNow >= turnStartedAt + (turnDuration * 1000L))
					{
						action.Value = null;
						return TransactionResult.Success(data);
					}
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

				if (nextTurn >= shotsPerPlayer * 2)
				{
					FinishMatch(data, match);
				}
				else
				{
					string nextPlayer = currentTurnNumber % 2 == 0 ? "Jogador2" : "Jogador1";
					match.Child("CurrentTurnPlayer").Value = nextPlayer;
					match.Child("TurnStartedAt").Value = ServerValue.Timestamp;
				}

				return TransactionResult.Success(data);
			});

			DataSnapshot processedSnapshot = await roomReference.Child("ProcessedActions").Child(shotId).GetValueAsync();
			if (processedSnapshot.Exists)
			{
				Debug.Log("[FootballVR] Chute processado: " + shotId);
				OnShotProcessed?.Invoke();
			}
		}
		catch (Exception ex)
		{
			Debug.LogError("[FootballVR] Erro ao processar chute " + shotId + ": " + ex.Message);
		}
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

	private async Task ProcessExpiredTurn(int expectedTurn)
	{
		if (!IsConnected || !IsMyPlayerConnected || roomReference == null || timeoutProcessingTurn == expectedTurn)
			return;
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

				if (nextTurn >= shotsPerPlayer * 2)
				{
					FinishMatch(data, match);
				}
				else
				{
					string nextPlayer = expectedTurn % 2 == 0 ? "Jogador2" : "Jogador1";
					match.Child("CurrentTurnPlayer").Value = nextPlayer;
					match.Child("TurnStartedAt").Value = ServerValue.Timestamp;
				}
				return TransactionResult.Success(data);
			});
		}
		catch (Exception ex)
		{
			Debug.LogError("[FootballVR] Erro ao processar tempo esgotado: " + ex);
		}
		finally
		{
			if (timeoutProcessingTurn == expectedTurn) timeoutProcessingTurn = -1;
		}
	}

	public async Task LeaveRoom()
	{
		if (!IsInRoom || roomReference == null) return;
		DatabaseReference leavingRoom = roomReference;

		try
		{
			if (IsConnected)
			{
				// Cancela o OnDisconnect antes de sair limpo
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
		catch (Exception ex)
		{
			Debug.LogWarning("[FootballVR] Erro ao sair: " + ex.Message);
		}
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
	}

	private void DetachRoomListeners()
	{
		if (roomReference != null && roomListener != null) { roomReference.ValueChanged -= roomListener; roomListener = null; }
		if (actionsReference != null && actionListener != null) { actionsReference.ChildAdded -= actionListener; actionListener = null; }
	}

	private void OnDestroy()
	{
		DetachRoomListeners();
		if (Instance == this) Instance = null;
	}

	private static string ReadString(MutableData data) => data == null || data.Value == null ? "" : data.Value.ToString();
	private static string ReadString(DataSnapshot data) => data == null || data.Value == null ? "" : data.Value.ToString();
	private static int ReadInt(MutableData data) => data == null || data.Value == null ? 0 : Convert.ToInt32(data.Value);
	private static int ReadInt(DataSnapshot data) => data == null || data.Value == null ? 0 : Convert.ToInt32(data.Value);
	private static bool ReadBool(MutableData data) => data != null && data.Value != null && Convert.ToBoolean(data.Value);
	private static bool ReadBool(DataSnapshot data) => data != null && data.Value != null && Convert.ToBoolean(data.Value);
	private static long ReadLong(MutableData data) => data == null || data.Value == null ? 0L : Convert.ToInt64(data.Value);
	private static long ReadLong(DataSnapshot data) => data == null || data.Value == null ? 0L : Convert.ToInt64(data.Value);

	private void ReportError(string message)
	{
		Debug.LogError("[FootballVR] " + message);
		OnError?.Invoke(message);
	}

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
	}
}