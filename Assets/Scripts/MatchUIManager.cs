
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
	private QuestFootKick questFootKick;

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

	[Header("Eventos")]
	public UnityEvent onMyGoal;
	public UnityEvent onOpponentGoal;
	public UnityEvent onMyTurnStart;
	public UnityEvent onOpponentTurnStart;
	public UnityEvent onTurnTimeout;
	public UnityEvent onMatchStarted;
	public UnityEvent onMatchFinished;

	private FirebaseManager firebaseManager;
	private FirebaseManager.RoomState currentRoom;

	private int lastTurnNumber = -1;
	private int lastPlayer1Score = 0;
	private int lastPlayer2Score = 0;
	private string lastStatus = "";
	private Coroutine feedbackCoroutine;

	private void Start()
	{
		firebaseManager = FirebaseManager.Instance;
		questFootKick = FindAnyObjectByType<QuestFootKick>();
		if (firebaseManager == null)
		{
			Debug.LogError("[MatchUI] FirebaseManager não encontrado!");
			return;
		}

		firebaseManager.OnRoomUpdated += HandleRoomUpdated;
		firebaseManager.OnRoomJoined += HandleRoomJoined;

		if (firebaseManager.IsInRoom && firebaseManager.CurrentRoom != null)
			HandleRoomUpdated(firebaseManager.CurrentRoom);
		else
			SetInitialWaitingState();
	}

	private void OnDestroy()
	{
		if (firebaseManager != null)
		{
			firebaseManager.OnRoomUpdated -= HandleRoomUpdated;
			firebaseManager.OnRoomJoined -= HandleRoomJoined;
		}
	}

	private void Update()
	{
		if (currentRoom != null && currentRoom.Status == "Playing")
			UpdateTimer();
	}

	private void SetInitialWaitingState()
	{
		if (txtRoomCode) txtRoomCode.text = "Sala: ---";
		if (txtMatchStatus) txtMatchStatus.text = "AGUARDANDO";
		if (txtTurnInfo) txtTurnInfo.text = "Aguardando início da partida...";
		if (txtCurrentPlayer) txtCurrentPlayer.text = questFootKick.playerDisplayName;
		if (txtTimer) txtTimer.text = "--:--";
		if (txtPlayer1Score) txtPlayer1Score.text = "Jogador 1: 0";
		if (txtPlayer2Score) txtPlayer2Score.text = "Jogador 2: 0";
		if (txtPlayer1Shots) txtPlayer1Shots.text = "Chutes: 0/5";
		if (txtPlayer2Shots) txtPlayer2Shots.text = "Chutes: 0/5";
		if (txtWinner) txtWinner.text = "";
		HideFeedback();
	}

	private void HandleRoomJoined()
	{
		if (txtRoomCode)
			txtRoomCode.text = $"Sala: {firebaseManager.RoomCode}";
	}

	private void HandleRoomUpdated(FirebaseManager.RoomState room)
	{
		if (room == null) return;

		bool isFirstUpdate = currentRoom == null;
		currentRoom = room;

		bool isPlayer1 = firebaseManager.PlayerId == "Jogador1";
		int turnNumber = room.TurnNumber;
		int shotsPerPlayer = room.ShotsPerPlayer;

		if (!isFirstUpdate)
		{
			// Detectar GOLS
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
			else if (room.Player2Score > lastPlayer2Score)
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

			// Detectar MUDANÇA DE TURNO
			if (turnNumber != lastTurnNumber && turnNumber > 0)
			{
				bool wasTimeout = false;

				if (lastStatus == "Playing" && firebaseManager.HasServerTimeOffset)
				{
					long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
						+ firebaseManager.ServerTimeOffset;

					long turnEnd = currentRoom.TurnStartedAt
						+ (currentRoom.TurnDuration * 1000L);

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

			if (room.Status == "Playing" && lastStatus != "Playing")
			{
				ShowFeedback("PARTIDA INICIADA!", Color.blue);
				onMatchStarted?.Invoke();
			}

			// Detectar FINALIZAÇÃO
			if (room.Status == "Finished" && lastStatus != "Finished")
			{
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
		}

		// ATUALIZAÇÃO DO PLACAR
		if (txtPlayer1Score)
			txtPlayer1Score.text = "Jogador 1: " + room.Player1Score;

		if (txtPlayer2Score)
			txtPlayer2Score.text = "Jogador 2: " + room.Player2Score;

		int p1Shots = (turnNumber + 1) / 2;
		int p2Shots = turnNumber / 2;

		if (txtPlayer1Shots)
			txtPlayer1Shots.text = $"Chutes: {Mathf.Min(p1Shots, shotsPerPlayer)}/{shotsPerPlayer}";

		if (txtPlayer2Shots)
			txtPlayer2Shots.text = $"Chutes: {Mathf.Min(p2Shots, shotsPerPlayer)}/{shotsPerPlayer}";

		// ESTADO FINALIZADO
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
		}
		// ESTADO AGUARDANDO
		else if (room.Status == "Waiting")
		{
			if (txtMatchStatus)
				txtMatchStatus.text = "AGUARDANDO INÍCIO";

			if (txtTurnInfo)
				txtTurnInfo.text = "O Admin deve iniciar a partida.";

			if (txtTimer)
				txtTimer.text = "--:--";

			if (txtWinner)
				txtWinner.text = "";
		}
		// ESTADO EM ANDAMENTO
		else if (room.Status == "Playing")
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

		lastTurnNumber = turnNumber;
		lastPlayer1Score = room.Player1Score;
		lastPlayer2Score = room.Player2Score;
		lastStatus = room.Status;
	}


	private string GetPlayerName(FirebaseManager.RoomState room, bool player1)
	{
		// Os nomes serão obtidos do FirebaseManager quando disponíveis.
		// Até lá, utiliza a identificação fixa do jogador.
		return player1 ? "Jogador 1" : "Jogador 2";
	}

	private string GetMatchResult()
	{
		if (currentRoom == null)
			return "AGUARDANDO RESULTADO";

		if (currentRoom.Player1Score == currentRoom.Player2Score)
			return "EMPATE!";

		bool isPlayer1 = firebaseManager.PlayerId == "Jogador1";

		bool iWon =
			(isPlayer1 && currentRoom.Player1Score > currentRoom.Player2Score) ||
			(!isPlayer1 && currentRoom.Player2Score > currentRoom.Player1Score);

		return iWon ? "VOCÊ VENCEU!" : "VOCÊ PERDEU!";
	}

	private void UpdateTimer()
	{
		if (currentRoom == null || currentRoom.TurnStartedAt <= 0)
		{
			if (txtTimer) txtTimer.text = "Tempo: --:--";
			return;
		}

		if (!firebaseManager.HasServerTimeOffset)
		{
			if (txtTimer) txtTimer.text = "Sincronizando...";
			return;
		}

		long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			+ firebaseManager.ServerTimeOffset;

		long turnEnd = currentRoom.TurnStartedAt
			+ (currentRoom.TurnDuration * 1000L);

		long remainingMs = System.Math.Max(0L, turnEnd - serverNow);

		int totalSeconds = (int)(remainingMs / 1000L);
		int minutes = totalSeconds / 60;
		int seconds = totalSeconds % 60;

		if (txtTimer)
		{
			txtTimer.text = $"Tempo: {minutes:00}:{seconds:00}";
			txtTimer.color = totalSeconds <= 10 ? Color.red : Color.white;
		}
	}

	private void ShowFeedback(string message, Color color)
	{
		if (txtFeedbackMessage == null) return;

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
	}
}
