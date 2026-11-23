
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
	public TextMeshProUGUI txtPlacarVR;
	public TextMeshProUGUI txtTurnInfoVR;
	public TextMeshProUGUI txtCurrentPlayerVR;
	public TextMeshProUGUI txtP1Info, txtP2Info;

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

	private Coroutine winnerCoroutine;

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


		if (winnerCoroutine != null)
			StopCoroutine(winnerCoroutine);
	}


	private void ShowWinnerMessage(string message)
	{
		if (txtWinner == null)
			return;

		// Cancela o temporizador anterior, se existir.
		if (winnerCoroutine != null)
		{
			StopCoroutine(winnerCoroutine);
			winnerCoroutine = null;
		}

		txtWinner.text = message;

		// Limpa automaticamente após 3 segundos.
		winnerCoroutine = StartCoroutine(HideWinnerAfterDelay());
	}

	private IEnumerator HideWinnerAfterDelay()
	{
		yield return new WaitForSeconds(3f);

		ClearWinnerMessage();
	}

	private void ClearWinnerMessage()
	{
		if (winnerCoroutine != null)
		{
			StopCoroutine(winnerCoroutine);
			winnerCoroutine = null;
		}

		if (txtWinner != null)
			txtWinner.text = "";
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
		{
			txtTurnInfo.text = "Aguardando início da partida...";
			txtTurnInfoVR.text = "Aguardando início da partida...";
		}

		if (txtCurrentPlayer)
			txtCurrentPlayer.text = GetLocalPlayerName() +
				"\n<size=36>Chutes: 0/" + GetConfiguredShots() +
				"\nGols: 0</size>";

		txtCurrentPlayerVR.text = $"Chutes: 0/{GetConfiguredShots()}";

		bool isPlayer1 = IsLocalPlayer1();
		txtP1Info.text = isPlayer1 ? "Você" : "Jogador 1";
		txtP2Info.text = isPlayer1 ? "Jogador 2" : "Você";

		if (txtPlacar)
		{
			txtPlacar.text = "Placar: 0 - 0";
			txtPlacarVR.text = "0 - 0";
		}

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
			// Detecta o gol primeiro.
			bool goalDetected = DetectGoals(room, isPlayer1);

			// Se houve gol nesta atualização, não apaga a mensagem
			// mesmo que a vez também tenha mudado para o jogador local.
			DetectTurnChanges(room, turnNumber, goalDetected);

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



	private bool DetectGoals(FirebaseManager.RoomState room, bool isPlayer1)
	{
		bool player1Scored = room.Player1Score > lastPlayer1Score;
		bool player2Scored = room.Player2Score > lastPlayer2Score;

		bool goalDetected = player1Scored || player2Scored;

		if (!goalDetected)
			return false;

		Debug.Log(
			$"[MatchUI] GOL DETECTADO | " +
			$"PlayerId: {firebaseManager.PlayerId} | " +
			$"P1: {lastPlayer1Score} -> {room.Player1Score} | " +
			$"P2: {lastPlayer2Score} -> {room.Player2Score}"
		);

		// Jogador 1 marcou.
		if (player1Scored)
		{
			bool wasMyGoal = isPlayer1;

			if (wasMyGoal)
			{
				ShowFeedback("GOL SEU!", Color.green);
				onMyGoal?.Invoke();

				ShowWinnerMessage("GOOOOOOOL!");
			}
			else
			{
				ShowFeedback("GOL DO ADVERSÁRIO!", Color.red);
				onOpponentGoal?.Invoke();

				ShowWinnerMessage("GOL DO ADVERSÁRIO!");
			}
		}

		// Jogador 2 marcou.
		if (player2Scored)
		{
			bool wasMyGoal = !isPlayer1;

			if (wasMyGoal)
			{
				ShowFeedback("GOL SEU!", Color.green);
				onMyGoal?.Invoke();

				ShowWinnerMessage("GOOOOOOOL!");
			}
			else
			{
				ShowFeedback("GOL DO ADVERSÁRIO!", Color.red);
				onOpponentGoal?.Invoke();

				ShowWinnerMessage("GOL DO ADVERSÁRIO!");
			}
		}

		return true;
	}



	private void DetectTurnChanges(
	FirebaseManager.RoomState room,
	int turnNumber,
	bool goalDetectedThisUpdate)
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

			// Não apaga o gol que acabou de ser detectado.
			// A mensagem do gol terá prioridade durante 3 segundos.
			if (!goalDetectedThisUpdate)
			{
				ClearWinnerMessage();
			}
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
		// Calcula os chutes realizados por cada jogador.
		int p1Shots = (room.TurnNumber + 1) / 2;
		int p2Shots = room.TurnNumber / 2;

		p1Shots = Mathf.Clamp(p1Shots, 0, shotsPerPlayer);
		p2Shots = Mathf.Clamp(p2Shots, 0, shotsPerPlayer);

		// Placar dos jogadores.
		if (txtPlayer1Score)
			txtPlayer1Score.text = "Jogador 1: " + room.Player1Score;

		if (txtPlayer2Score)
			txtPlayer2Score.text = "Jogador 2: " + room.Player2Score;

		// Quantidade de chutes de cada jogador.
		if (txtPlayer1Shots)
			txtPlayer1Shots.text =
				$"Chutes: {p1Shots}/{shotsPerPlayer}";

		if (txtPlayer2Shots)
			txtPlayer2Shots.text =
				$"Chutes: {p2Shots}/{shotsPerPlayer}";

		// Dados do jogador local.
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

		// IMPORTANTE:
		// Agora o texto VR utiliza a quantidade real de chutes.
		if (txtCurrentPlayerVR)
		{
			txtCurrentPlayerVR.text =
				$"Chutes: {myShots}/{shotsPerPlayer}";
		}

		// Placar geral.
		if (txtPlacar)
		{
			txtPlacar.text =
				$"Placar: {room.Player1Score} - {room.Player2Score}";
		}

		if (txtPlacarVR)
		{
			txtPlacarVR.text =
				$"{room.Player1Score} - {room.Player2Score}";
		}
	}


	private void UpdateMatchStatus(FirebaseManager.RoomState room, bool isPlayer1)
	{
		if (room.Status == "Finished")
		{
			if (txtMatchStatus)
				txtMatchStatus.text = "PARTIDA FINALIZADA";

			if (txtTurnInfo)
			{
				txtTurnInfo.text = "Todos os chutes foram realizados.";
				txtTurnInfoVR.text = "Todos os chutes foram realizados.";
			}

			if (txtTimer)
				txtTimer.text = "Tempo: 00:00";

			// Não substitui uma mensagem de gol que ainda está sendo exibida.
			if (txtWinner && winnerCoroutine == null)
			{
				txtWinner.text = GetMatchResult();
			}

			return;
		}

		if (room.Status == "Waiting")
		{
			if (txtMatchStatus)
				txtMatchStatus.text = "AGUARDANDO INÍCIO";

			if (txtTurnInfo)
			{
				txtTurnInfo.text = "O Admin deve iniciar a partida.";
				txtTurnInfoVR.text = "O Admin deve iniciar a partida.";
			}

			if (txtTimer)
				txtTimer.text = "--:--";

			return;
		}

		if (room.Status == "Playing")
		{
			if (txtMatchStatus)
				txtMatchStatus.text = "EM ANDAMENTO";

			bool isMyTurn = room.CurrentTurnPlayer == firebaseManager.PlayerId;

			if (txtTurnInfo)
			{
				txtTurnInfo.text = isMyTurn
					? "<color=#00FF00>É A SUA VEZ DE CHUTAR!</color>"
					: "<color=#FFAA00>Aguardando o adversário...</color>";

				txtTurnInfoVR.text = isMyTurn
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
