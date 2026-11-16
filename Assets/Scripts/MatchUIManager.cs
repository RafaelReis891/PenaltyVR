
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class MatchUIManager : MonoBehaviour
{
	[Header("Referências de Texto (TextMeshPro)")]
	public TextMeshProUGUI txtRoomCode;
	public TextMeshProUGUI txtMatchStatus;
	public TextMeshProUGUI txtTurnInfo;
	public TextMeshProUGUI txtTimer;

	[Header("Jogador Atual")]
	public TextMeshProUGUI txtCurrentPlayer;
	public TextMeshProUGUI txtPlacar;

	[Tooltip("Opcional. Necessário apenas para informações específicas do VR.")]
	public QuestFootKick questFootKick;

	[Header("Placar")]
	public TextMeshProUGUI txtPlayer1Score;
	public TextMeshProUGUI txtPlayer2Score;
	public TextMeshProUGUI txtPlayer1Shots;
	public TextMeshProUGUI txtPlayer2Shots;

	[Header("Finalização")]
	public TextMeshProUGUI txtWinner;

	[Header("Mensagem de Feedback")]
	public TextMeshProUGUI txtFeedbackMessage;
	public float feedbackDuration = 2.5f;

	[Header("Configuração")]
	[Tooltip("Nome utilizado quando não existe QuestFootKick na cena.")]
	public string fallbackPlayerName = "Jogador";

	[Tooltip("Quantidade padrão de chutes por jogador caso o servidor não informe.")]
	public int defaultShotsPerPlayer = 5;

	[Header("Eventos")]
	public UnityEvent onMyGoal;
	public UnityEvent onOpponentGoal;
	public UnityEvent onMyTurnStart;
	public UnityEvent onOpponentTurnStart;
	public UnityEvent onTurnTimeout;
	public UnityEvent onMatchStarted;
	public UnityEvent onMatchFinished;
	public UnityEvent onRestartMode;

	private FirebaseManager firebaseManager;
	private FirebaseManager.RoomState currentRoom;

	private int lastTurnNumber = -1;
	private int lastPlayer1Score = 0;
	private int lastPlayer2Score = 0;
	private string lastStatus = "";

	private Coroutine feedbackCoroutine;

	private bool isSubscribed;

	private void Start()
	{
		firebaseManager = FirebaseManager.Instance;

		// QuestFootKick é opcional.
		if (questFootKick == null)
			questFootKick = FindAnyObjectByType<QuestFootKick>();

		if (firebaseManager == null)
		{
			Debug.LogError("[MatchUI] FirebaseManager não encontrado!");
			return;
		}

		SubscribeEvents();

		if (firebaseManager.IsInRoom && firebaseManager.CurrentRoom != null)
		{
			HandleRoomUpdated(firebaseManager.CurrentRoom);
		}
		else
		{
			SetInitialWaitingState();
		}
	}

	private void OnEnable()
	{
		// Não assina aqui porque o FirebaseManager pode ainda não estar pronto.
	}

	private void OnDestroy()
	{
		UnsubscribeEvents();

		if (feedbackCoroutine != null)
			StopCoroutine(feedbackCoroutine);
	}

	private void SubscribeEvents()
	{
		if (firebaseManager == null || isSubscribed)
			return;

		firebaseManager.OnRoomUpdated += HandleRoomUpdated;
		firebaseManager.OnRoomJoined += HandleRoomJoined;
		firebaseManager.OnMatchRestartRequested += HandleMatchRestart;

		isSubscribed = true;

		Debug.Log("[MatchUI] Eventos conectados.");
	}

	private void UnsubscribeEvents()
	{
		if (firebaseManager == null || !isSubscribed)
			return;

		firebaseManager.OnRoomUpdated -= HandleRoomUpdated;
		firebaseManager.OnRoomJoined -= HandleRoomJoined;
		firebaseManager.OnMatchRestartRequested -= HandleMatchRestart;

		isSubscribed = false;
	}

	private void Update()
	{
		if (currentRoom != null && currentRoom.Status == "Playing")
			UpdateTimer();
	}

	private void SetInitialWaitingState()
	{
		if (txtRoomCode)
			txtRoomCode.text = "Sala: ---";

		if (txtMatchStatus)
			txtMatchStatus.text = "AGUARDANDO";

		if (txtTurnInfo)
			txtTurnInfo.text = "Aguardando início da partida...";

		if (txtCurrentPlayer)
			txtCurrentPlayer.text = GetLocalPlayerName() +
				"\n<size=36>Chutes: 0/" + GetConfiguredShots() +
				"\nGols: 0</size>";

		if (txtPlacar)
			txtPlacar.text = "Placar: 0 - 0";

		if (txtTimer)
			txtTimer.text = "--:--";

		if (txtPlayer1Score)
			txtPlayer1Score.text = "Jogador 1: 0";

		if (txtPlayer2Score)
			txtPlayer2Score.text = "Jogador 2: 0";

		if (txtPlayer1Shots)
			txtPlayer1Shots.text = "Chutes: 0/" + GetConfiguredShots();

		if (txtPlayer2Shots)
			txtPlayer2Shots.text = "Chutes: 0/" + GetConfiguredShots();

		if (txtWinner)
			txtWinner.text = "";

		HideFeedback();
	}

	private int GetConfiguredShots()
	{
		if (questFootKick != null && questFootKick.shotsPerPlayer > 0)
			return questFootKick.shotsPerPlayer;

		return Mathf.Max(1, defaultShotsPerPlayer);
	}

	private string GetLocalPlayerName()
	{
		if (questFootKick != null &&
			!string.IsNullOrEmpty(questFootKick.playerDisplayName))
		{
			return questFootKick.playerDisplayName;
		}

		if (firebaseManager != null)
		{
			if (firebaseManager.PlayerId == "Jogador1")
				return "Jogador 1";

			if (firebaseManager.PlayerId == "Jogador2")
				return "Jogador 2";
		}

		return fallbackPlayerName;
	}

	private bool IsLocalPlayer1()
	{
		if (firebaseManager == null)
			return false;

		return firebaseManager.PlayerId == "Jogador1";
	}

	private void HandleRoomJoined()
	{
		if (firebaseManager == null)
			return;

		if (txtRoomCode)
			txtRoomCode.text = "Sala: " + firebaseManager.RoomCode;

		Debug.Log("[MatchUI] Sala conectada: " + firebaseManager.RoomCode);
	}

	private void HandleMatchRestart()
	{
		Debug.Log("[MatchUI] Comando de reinício recebido do painel!");

		lastTurnNumber = -1;
		lastPlayer1Score = 0;
		lastPlayer2Score = 0;
		lastStatus = "";

		ShowFeedback("PARTIDA REINICIADA PELO ADMIN!", Color.magenta);

		onRestartMode?.Invoke();

		if (currentRoom != null)
			HandleRoomUpdated(currentRoom);
		else
			SetInitialWaitingState();
	}

	private void HandleRoomUpdated(FirebaseManager.RoomState room)
	{
		if (room == null)
			return;

		bool isFirstUpdate = currentRoom == null;

		currentRoom = room;

		int turnNumber = Mathf.Max(0, room.TurnNumber);
		int shotsPerPlayer = room.ShotsPerPlayer > 0
			? room.ShotsPerPlayer
			: GetConfiguredShots();

		bool isPlayer1 = IsLocalPlayer1();

		Debug.Log(
			$"[MatchUI] Atualização recebida | " +
			$"Status: {room.Status} | " +
			$"Turno: {turnNumber} | " +
			$"Jogador1: {room.Player1Score} | " +
			$"Jogador2: {room.Player2Score} | " +
			$"Chutes: {shotsPerPlayer}"
		);

		if (!isFirstUpdate)
		{
			DetectGoals(room, isPlayer1);

			DetectTurnChanges(room, turnNumber);

			DetectMatchStart(room);

			DetectMatchFinish(room, isPlayer1);
		}

		UpdateScoreboard(room, shotsPerPlayer, isPlayer1);

		UpdateMatchStatus(room, isPlayer1);

		lastTurnNumber = turnNumber;
		lastPlayer1Score = room.Player1Score;
		lastPlayer2Score = room.Player2Score;
		lastStatus = room.Status;
	}

	private void DetectGoals(FirebaseManager.RoomState room, bool isPlayer1)
	{
		if (room.Player1Score > lastPlayer1Score)
		{
			if (isPlayer1)
			{
				ShowFeedback("GOL SEU!", Color.green);
				onMyGoal?.Invoke();
			}
			else
			{
				ShowFeedback("GOL DO ADVERSÁRIO!", Color.red);
				onOpponentGoal?.Invoke();
			}
		}

		if (room.Player2Score > lastPlayer2Score)
		{
			if (!isPlayer1)
			{
				ShowFeedback("GOL SEU!", Color.green);
				onMyGoal?.Invoke();
			}
			else
			{
				ShowFeedback("GOL DO ADVERSÁRIO!", Color.red);
				onOpponentGoal?.Invoke();
			}
		}
	}

	private void DetectTurnChanges(FirebaseManager.RoomState room, int turnNumber)
	{
		if (turnNumber == lastTurnNumber || turnNumber <= 0)
			return;

		bool wasTimeout = false;

		if (lastStatus == "Playing" &&
			firebaseManager.HasServerTimeOffset &&
			room.TurnStartedAt > 0)
		{
			long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				+ firebaseManager.ServerTimeOffset;

			long turnEnd = room.TurnStartedAt
				+ room.TurnDuration * 1000L;

			wasTimeout = serverNow >= turnEnd - 1000;
		}

		if (wasTimeout)
		{
			ShowFeedback("TEMPO ESGOTADO!", Color.yellow);
			onTurnTimeout?.Invoke();
		}

		if (room.CurrentTurnPlayer == firebaseManager.PlayerId)
		{
			ShowFeedback("SUA VEZ!", Color.cyan);
			onMyTurnStart?.Invoke();
		}
		else if (!string.IsNullOrEmpty(room.CurrentTurnPlayer))
		{
			ShowFeedback("VEZ DO ADVERSÁRIO", Color.gray);
			onOpponentTurnStart?.Invoke();
		}
	}

	private void DetectMatchStart(FirebaseManager.RoomState room)
	{
		if (room.Status == "Playing" && lastStatus != "Playing")
		{
			ShowFeedback("PARTIDA INICIADA!", Color.blue);
			onMatchStarted?.Invoke();
		}
	}

	private void DetectMatchFinish(FirebaseManager.RoomState room, bool isPlayer1)
	{
		if (room.Status != "Finished" || lastStatus == "Finished")
			return;

		bool isDraw = room.Player1Score == room.Player2Score;

		bool iWon =
			(isPlayer1 && room.Player1Score > room.Player2Score) ||
			(!isPlayer1 && room.Player2Score > room.Player1Score);

		ShowFeedback(
			isDraw ? "EMPATE!" :
			iWon ? "VOCÊ VENCEU!" : "VOCÊ PERDEU!",
			isDraw ? Color.yellow :
			iWon ? Color.green : Color.red
		);

		onMatchFinished?.Invoke();
	}

	private void UpdateScoreboard(
		FirebaseManager.RoomState room,
		int shotsPerPlayer,
		bool isPlayer1)
	{
		int p1Shots = (room.TurnNumber + 1) / 2;
		int p2Shots = room.TurnNumber / 2;

		p1Shots = Mathf.Clamp(p1Shots, 0, shotsPerPlayer);
		p2Shots = Mathf.Clamp(p2Shots, 0, shotsPerPlayer);

		if (txtPlayer1Score)
			txtPlayer1Score.text = "Jogador 1: " + room.Player1Score;

		if (txtPlayer2Score)
			txtPlayer2Score.text = "Jogador 2: " + room.Player2Score;

		if (txtPlayer1Shots)
			txtPlayer1Shots.text =
				$"Chutes: {p1Shots}/{shotsPerPlayer}";

		if (txtPlayer2Shots)
			txtPlayer2Shots.text =
				$"Chutes: {p2Shots}/{shotsPerPlayer}";

		int myGoals = isPlayer1
			? room.Player1Score
			: room.Player2Score;

		int myShots = isPlayer1
			? p1Shots
			: p2Shots;

		if (txtCurrentPlayer)
		{
			txtCurrentPlayer.text =
				$"{GetLocalPlayerName()}\n" +
				$"<size=36>Chutes: {myShots}/{shotsPerPlayer}\n" +
				$"Gols: {myGoals}</size>";
		}

		if (txtPlacar)
		{
			txtPlacar.text =
				$"Placar: {room.Player1Score} - {room.Player2Score}";
		}
	}

	private void UpdateMatchStatus(FirebaseManager.RoomState room, bool isPlayer1)
	{
		if (room.Status == "Finished")
		{
			if (txtMatchStatus)
				txtMatchStatus.text = "PARTIDA FINALIZADA";

			if (txtTurnInfo)
				txtTurnInfo.text = "Todos os chutes foram realizados.";

			if (txtTimer)
				txtTimer.text = "Tempo: 00:00";

			if (txtWinner)
				txtWinner.text = GetMatchResult();

			return;
		}

		if (room.Status == "Waiting")
		{
			if (txtMatchStatus)
				txtMatchStatus.text = "AGUARDANDO INÍCIO";

			if (txtTurnInfo)
				txtTurnInfo.text = "O Admin deve iniciar a partida.";

			if (txtTimer)
				txtTimer.text = "--:--";

			if (txtWinner)
				txtWinner.text = "";

			return;
		}

		if (room.Status == "Playing")
		{
			if (txtMatchStatus)
				txtMatchStatus.text = "EM ANDAMENTO";

			if (txtWinner)
				txtWinner.text = "";

			bool isMyTurn = room.CurrentTurnPlayer == firebaseManager.PlayerId;

			if (txtTurnInfo)
			{
				txtTurnInfo.text = isMyTurn
					? "<color=#00FF00>É A SUA VEZ DE CHUTAR!</color>"
					: "<color=#FFAA00>Aguardando o adversário...</color>";
			}

			UpdateTimer();
		}
	}

	private string GetMatchResult()
	{
		if (currentRoom == null)
			return "AGUARDANDO RESULTADO";

		if (currentRoom.Player1Score == currentRoom.Player2Score)
			return "EMPATE!";

		bool isPlayer1 = IsLocalPlayer1();

		bool iWon =
			(isPlayer1 && currentRoom.Player1Score > currentRoom.Player2Score) ||
			(!isPlayer1 && currentRoom.Player2Score > currentRoom.Player1Score);

		return iWon ? "VOCÊ VENCEU!" : "VOCÊ PERDEU!";
	}

	private void UpdateTimer()
	{
		if (currentRoom == null ||
			currentRoom.TurnStartedAt <= 0)
		{
			if (txtTimer)
				txtTimer.text = "Tempo: --:--";

			return;
		}

		if (!firebaseManager.HasServerTimeOffset)
		{
			if (txtTimer)
				txtTimer.text = "Sincronizando...";

			return;
		}

		long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			+ firebaseManager.ServerTimeOffset;

		long turnEnd = currentRoom.TurnStartedAt
			+ currentRoom.TurnDuration * 1000L;

		long remainingMs = Math.Max(0L, turnEnd - serverNow);

		int totalSeconds = (int)(remainingMs / 1000L);

		int minutes = totalSeconds / 60;
		int seconds = totalSeconds % 60;

		if (txtTimer)
		{
			txtTimer.text = $"Tempo: {minutes:00}:{seconds:00}";
			txtTimer.color = totalSeconds <= 10
				? Color.red
				: Color.white;
		}
	}

	private void ShowFeedback(string message, Color color)
	{
		if (txtFeedbackMessage == null)
			return;

		if (feedbackCoroutine != null)
			StopCoroutine(feedbackCoroutine);

		txtFeedbackMessage.text = message;
		txtFeedbackMessage.color = color;
		txtFeedbackMessage.gameObject.SetActive(true);

		feedbackCoroutine = StartCoroutine(HideFeedbackAfterDelay());
	}

	private IEnumerator HideFeedbackAfterDelay()
	{
		yield return new WaitForSeconds(feedbackDuration);

		HideFeedback();
	}

	private void HideFeedback()
	{
		if (txtFeedbackMessage != null)
			txtFeedbackMessage.gameObject.SetActive(false);

		feedbackCoroutine = null;
	}
}
