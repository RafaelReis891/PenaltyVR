using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class QuestFootKick : MonoBehaviour
{
	public enum PlayerRole { Player1, Player2 }

	[Header("Configuração do Jogador")]
	[Tooltip("Defina no Inspector: Esta build é o Jogador 1 ou Jogador 2?")]
	public PlayerRole myRole = PlayerRole.Player1;

	[Tooltip("Nome que será exibido no painel Admin.")]
	public string playerDisplayName = "Jogador 1";
	public TMP_Text playerLocalLabel;

	public AudioSource source;
	public float linearVelocity;
	private Rigidbody rb;

	[Header("Goleiro")]
	public GoalKeeperActions goalKeeper;

	[Header("Configuração do chute")]
	public float extraForce = 5f;
	public float upwardForce = 1.5f;

	[Header("Turnos")]
	[Min(1)] public int shotsPerPlayer = 5;
	public int currentBall;

	private int playerShots;
	private int opponentShots;

	private enum TurnState { WaitingRoom, WaitingOpponent, PlayerTurn, WaitingResults, Finished }
	private TurnState turnState = TurnState.WaitingRoom;

	[Header("Assistência de curva")]
	public Transform goalCenter;
	public float curveStrength = 3f;
	public float maxCurveAcceleration = 5f;
	public float curveDuration = 2f;
	private Rigidbody curvedBall;
	private float curveTimer;

	[Header("Placar")]
	public int player1Goals;
	public int player2Goals;

	[Header("Eventos")]
	public UnityEvent onPlayerGoal;
	public UnityEvent onOpponentGoal;
	public UnityEvent onFinished;

	[Header("Bolas")]
	public List<GameObject> balls;

	[Header("Finalização dos chutes")]
	[Min(0.1f)] public float ballStopDuration = 1.25f;
	[Min(1f)] public float maxBallResolveTime = 5f;
	[Min(0f)] public float stopVelocity = 0.15f;

	private readonly HashSet<GameObject> scoredBalls = new HashSet<GameObject>();
	private readonly HashSet<GameObject> pendingBalls = new HashSet<GameObject>();
	private readonly Dictionary<GameObject, float> ballIdleTimers = new Dictionary<GameObject, float>();
	private readonly Dictionary<GameObject, float> ballElapsedTimers = new Dictionary<GameObject, float>();
	private readonly Dictionary<GameObject, Vector3> initialBallPositions = new Dictionary<GameObject, Vector3>();
	private readonly Dictionary<GameObject, Quaternion> initialBallRotations = new Dictionary<GameObject, Quaternion>();
	private readonly List<GameObject> ballsToRemove = new List<GameObject>();

	private GameObject activeShotBall;
	private bool shotInProgress;
	private bool shotResultSent;
	private bool finished;
	private bool setupStarted;

	private int lastPlayer1Score;
	private int lastPlayer2Score;
	private int lastProcessedTurn = -1;
	private string lastRoomStatus = "";

	private void Start()
	{
		Debug.Log("[QuestFootKick] Iniciando. Papel configurado: " + myRole);
		playerLocalLabel.text = playerDisplayName;
		if (balls == null) balls = new List<GameObject>();
		rb = GetComponent<Rigidbody>();

		foreach (GameObject ball in balls)
		{
			if (ball == null) continue;
			initialBallPositions[ball] = ball.transform.position;
			initialBallRotations[ball] = ball.transform.rotation;
			Rigidbody ballRb = ball.GetComponent<Rigidbody>();
			if (ballRb != null)
			{
				ballRb.linearVelocity = Vector3.zero;
				ballRb.angularVelocity = Vector3.zero;
				ballRb.isKinematic = true;
			}
			ball.SetActive(false);
		}
		currentBall = 0;

		if (FirebaseManager.Instance == null)
		{
			Debug.LogError("[QuestFootKick] FirebaseManager não encontrado na cena.");
			return;
		}

		string fixedId = myRole == PlayerRole.Player1 ? "Jogador1" : "Jogador2";
		FirebaseManager.Instance.ConfigureIdentity(fixedId);

		FirebaseManager.Instance.OnRoomUpdated += HandleRoomUpdated;
		FirebaseManager.Instance.OnRoomJoined += OnSuccessfullyJoinedRoom;
		FirebaseManager.Instance.OnFirebaseReady += OnFirebaseReadyToSetup;

		if (FirebaseManager.Instance.IsInitialized && FirebaseManager.Instance.IsConnected)
			OnFirebaseReadyToSetup();

		if (FirebaseManager.Instance.IsInRoom && FirebaseManager.Instance.CurrentRoom != null)
			HandleRoomUpdated(FirebaseManager.Instance.CurrentRoom);
	}

	private void OnDestroy()
	{
		if (FirebaseManager.Instance != null)
		{
			FirebaseManager.Instance.OnRoomUpdated -= HandleRoomUpdated;
			FirebaseManager.Instance.OnRoomJoined -= OnSuccessfullyJoinedRoom;
			FirebaseManager.Instance.OnFirebaseReady -= OnFirebaseReadyToSetup;
		}
	}

	private async void OnFirebaseReadyToSetup()
	{
		if (setupStarted || FirebaseManager.Instance == null) return;
		setupStarted = true;

		try
		{
			string displayName = string.IsNullOrWhiteSpace(playerDisplayName)
				? (myRole == PlayerRole.Player1 ? "Jogador 1" : "Jogador 2")
				: playerDisplayName.Trim();

			Debug.Log("[QuestFootKick] Entrando na sala como: " + displayName);
			await FirebaseManager.Instance.JoinRoom(displayName);
		}
		catch (System.Exception ex)
		{
			Debug.LogError("[QuestFootKick] Falha no setup da sala: " + ex);
		}
	}

	private void Update()
	{

		if (rb != null) linearVelocity = rb.linearVelocity.magnitude * 3.6f;
		if (!finished) UpdatePendingBalls();

		if (!finished && turnState == TurnState.PlayerTurn && Input.GetKeyDown(KeyCode.Space))
			SimularChuteAleatorio();
	}

	private void OnSuccessfullyJoinedRoom()
	{
		if (FirebaseManager.Instance == null) return;
		Debug.Log("[QuestFootKick] Entrou na sala " + FirebaseManager.Instance.RoomCode + " como " + FirebaseManager.Instance.PlayerId + ".");
	}

	private void HandleRoomUpdated(FirebaseManager.RoomState room)
	{
		if (room == null || FirebaseManager.Instance == null) return;

		bool startingFreshMatch = room.Status == "Playing" && lastRoomStatus == "Waiting" && room.TurnNumber == 0;

		if ((lastRoomStatus == "Finished" && room.Status == "Waiting") || (finished && room.Status != "Finished") || startingFreshMatch)
		{
			Debug.Log("[FootballVR] Reset da partida detectado. Limpando estado local.");
			ResetLocalMatch();
		}

		if (finished && room.Status == "Finished")
		{
			lastRoomStatus = room.Status;
			return;
		}

		bool isPlayer1 = FirebaseManager.Instance.PlayerId == "Jogador1";
		int newMyGoals = isPlayer1 ? room.Player1Score : room.Player2Score;
		int newOpponentGoals = isPlayer1 ? room.Player2Score : room.Player1Score;
		int oldMyGoals = isPlayer1 ? lastPlayer1Score : lastPlayer2Score;
		int oldOpponentGoals = isPlayer1 ? lastPlayer2Score : lastPlayer1Score;

		if (newMyGoals > oldMyGoals)
		{
			onPlayerGoal?.Invoke();
			Debug.Log("<color=green>[FootballVR] GOL SEU!</color>");
		}
		if (newOpponentGoals > oldOpponentGoals)
		{
			onOpponentGoal?.Invoke();
			Debug.Log("<color=red>[FootballVR] GOL DO ADVERSÁRIO!</color>");
		}

		player1Goals = room.Player1Score;
		player2Goals = room.Player2Score;
		lastPlayer1Score = room.Player1Score;
		lastPlayer2Score = room.Player2Score;
		shotsPerPlayer = Mathf.Max(1, room.ShotsPerPlayer);

		if (isPlayer1)
		{
			playerShots = (room.TurnNumber + 1) / 2;
			opponentShots = room.TurnNumber / 2;
		}
		else
		{
			playerShots = room.TurnNumber / 2;
			opponentShots = (room.TurnNumber + 1) / 2;
		}

		playerShots = Mathf.Min(playerShots, shotsPerPlayer);
		opponentShots = Mathf.Min(opponentShots, shotsPerPlayer);
		currentBall = playerShots;

		if (room.TurnNumber != lastProcessedTurn)
		{
			lastProcessedTurn = room.TurnNumber;
			shotInProgress = false;
			shotResultSent = false;
			GameObject oldBall = activeShotBall;
			activeShotBall = null;
			if (oldBall != null)
			{
				CancelInvoke(nameof(CleanupPreviousBall));
				Invoke(nameof(CleanupPreviousBall), 3f);
			}
		}

		lastRoomStatus = room.Status;

		if (room.Status == "Finished")
		{
			FinishGame();
			return;
		}

		if (room.Status != "Playing")
		{
			turnState = TurnState.WaitingRoom;
			return;
		}

		if (!FirebaseManager.Instance.IsConnected)
		{
			turnState = TurnState.WaitingRoom;
			return;
		}

		if (room.CurrentTurnPlayer == FirebaseManager.Instance.PlayerId)
			BeginPlayerTurn();
		else
			turnState = TurnState.WaitingOpponent;
	}

	private void ResetLocalMatch()
	{
		finished = false;
		turnState = TurnState.WaitingRoom;
		player1Goals = 0;
		player2Goals = 0;
		playerShots = 0;
		opponentShots = 0;
		lastPlayer1Score = 0;
		lastPlayer2Score = 0;
		lastProcessedTurn = -1;
		shotInProgress = false;
		shotResultSent = false;
		activeShotBall = null;
		currentBall = 0;
		CancelInvoke(nameof(CleanupPreviousBall));
		if (goalKeeper != null) goalKeeper.ForceReset();
		ResetAllBalls();
	}

	private void ResetAllBalls()
	{
		foreach (GameObject ball in balls)
		{
			if (ball == null) continue;
			Rigidbody ballRb = ball.GetComponent<Rigidbody>();
			if (ballRb != null)
			{
				ballRb.linearVelocity = Vector3.zero;
				ballRb.angularVelocity = Vector3.zero;
				ballRb.isKinematic = true;
			}
			if (initialBallPositions.TryGetValue(ball, out Vector3 position)) ball.transform.position = position;
			if (initialBallRotations.TryGetValue(ball, out Quaternion rotation)) ball.transform.rotation = rotation;
			ball.SetActive(false);
		}
		activeShotBall = null;
		shotInProgress = false;
		shotResultSent = false;
		pendingBalls.Clear();
		scoredBalls.Clear();
		ballIdleTimers.Clear();
		ballElapsedTimers.Clear();
		ballsToRemove.Clear();
		curvedBall = null;
		curveTimer = 0f;
	}

	private void CleanupPreviousBall()
	{
		for (int i = 0; i < balls.Count; i++)
		{
			GameObject ball = balls[i];
			if (i == currentBall || ball == null || !ball.activeSelf) continue;
			Rigidbody ballRb = ball.GetComponent<Rigidbody>();
			if (ballRb != null)
			{
				ballRb.linearVelocity = Vector3.zero;
				ballRb.angularVelocity = Vector3.zero;
				ballRb.isKinematic = true;
			}
			if (initialBallPositions.TryGetValue(ball, out Vector3 position)) ball.transform.position = position;
			if (initialBallRotations.TryGetValue(ball, out Quaternion rotation)) ball.transform.rotation = rotation;
			ball.SetActive(false);
			RemovePendingBall(ball);
		}
		if (activeShotBall != null)
		{
			RemovePendingBall(activeShotBall);
			activeShotBall = null;
		}
	}

	private void BeginPlayerTurn()
	{
		if (finished || FirebaseManager.Instance == null || !FirebaseManager.Instance.IsMyTurn()) return;
		if (turnState == TurnState.PlayerTurn && (shotInProgress || activeShotBall != null)) return;
		if (playerShots >= shotsPerPlayer)
		{
			turnState = TurnState.WaitingOpponent;
			return;
		}
		if (!ActivateCurrentBall()) return;
		turnState = TurnState.PlayerTurn;
		Debug.Log("<color=green>[FootballVR] SUA VEZ!</color> Chutes: " + playerShots + "/" + shotsPerPlayer);
		if (source != null && !source.isPlaying) source.Play();
	}

	private GameObject GetCurrentBall() { return currentBall >= 0 && currentBall < balls.Count ? balls[currentBall] : null; }

	private bool ActivateCurrentBall()
	{
		GameObject ball = GetCurrentBall();
		if (ball == null) return false;
		if (activeShotBall == ball && ball.activeSelf) return true;

		ball.SetActive(true);
		Rigidbody ballRb = ball.GetComponent<Rigidbody>();
		if (ballRb == null) return false;

		ballRb.linearVelocity = Vector3.zero;
		ballRb.angularVelocity = Vector3.zero;
		ballRb.isKinematic = false;
		ballRb.WakeUp();
		activeShotBall = ball;
		return true;
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (finished || turnState != TurnState.PlayerTurn || shotInProgress) return;
		if (FirebaseManager.Instance == null || !FirebaseManager.Instance.IsMyTurn()) return;

		Rigidbody ballRb = collision.rigidbody;
		if (ballRb == null || !ballRb.gameObject.CompareTag("Ball")) return;

		GameObject current = GetCurrentBall();
		if (current == null || ballRb.gameObject != current) return;

		Vector3 kickDirection = ballRb.linearVelocity.normalized;
		if (kickDirection.sqrMagnitude < 0.001f)
		{
			if (collision.contactCount == 0) return;
			kickDirection = -collision.GetContact(0).normal;
		}
		if (kickDirection.sqrMagnitude < 0.001f) return;

		kickDirection.y += upwardForce;
		kickDirection.Normalize();
		LaunchBall(ballRb, kickDirection);
	}

	[ContextMenu("Testar Chute Aleatório")]
	private void SimularChuteAleatorio()
	{
		if (finished || turnState != TurnState.PlayerTurn || shotInProgress || FirebaseManager.Instance == null || !FirebaseManager.Instance.IsMyTurn()) return;
		GameObject ball = GetCurrentBall();
		if (ball == null) return;
		if (ExecuteRandomKick(ball)) Debug.Log("[FootballVR] Chute de teste executado.");
	}

	private bool ExecuteRandomKick(GameObject ball)
	{
		if (ball == null || goalCenter == null) return false;
		Rigidbody ballRb = ball.GetComponent<Rigidbody>();
		if (ballRb == null) return false;

		int targetIndex = Random.Range(0, 5);
		Vector3 targetOffset = targetIndex switch
		{
			0 => new Vector3(-2f, 1.5f, 0f),
			1 => new Vector3(2f, 1.5f, 0f),
			2 => new Vector3(-2f, 0f, 0f),
			3 => new Vector3(2f, 0f, 0f),
			_ => Vector3.zero
		};

		Vector3 targetPosition = goalCenter.TransformPoint(targetOffset);
		Vector3 direction = (targetPosition - ballRb.position).normalized;
		direction.y = upwardForce;
		direction.Normalize();
		LaunchBall(ballRb, direction);
		return true;
	}

	private void LaunchBall(Rigidbody ballRb, Vector3 direction)
	{
		if (ballRb == null || shotInProgress || FirebaseManager.Instance == null || !FirebaseManager.Instance.IsMyTurn()) return;

		shotInProgress = true;
		shotResultSent = false;
		activeShotBall = ballRb.gameObject;

		pendingBalls.Add(activeShotBall);
		ballIdleTimers[activeShotBall] = 0f;
		ballElapsedTimers[activeShotBall] = 0f;

		ballRb.isKinematic = false;
		ballRb.WakeUp();
		ballRb.AddForce(direction * extraForce, ForceMode.Impulse);

		if (goalCenter != null)
		{
			curvedBall = ballRb;
			curveTimer = curveDuration;
		}
		Debug.Log("[FootballVR] Chute realizado. Aguardando resultado físico.");
	}

	public void Goal(GameObject ball)
	{
		if (ball == null || finished || !shotInProgress || shotResultSent) return;
		if (ball != activeShotBall || !pendingBalls.Contains(ball) || scoredBalls.Contains(ball)) return;
		scoredBalls.Add(ball);
		Debug.Log("[FootballVR] Gol detectado localmente pelo trigger.");
		ResolveShot(ball, true);
	}

	private void UpdatePendingBalls()
	{
		ballsToRemove.Clear();
		foreach (GameObject ball in pendingBalls)
		{
			if (ball == null) { ballsToRemove.Add(ball); continue; }
			Rigidbody ballRb = ball.GetComponent<Rigidbody>();
			if (ballRb == null) { ballsToRemove.Add(ball); continue; }

			float elapsed = ballElapsedTimers.TryGetValue(ball, out float oldElapsed) ? oldElapsed + Time.deltaTime : Time.deltaTime;
			ballElapsedTimers[ball] = elapsed;

			if (elapsed >= maxBallResolveTime)
			{
				ballsToRemove.Add(ball);
				continue;
			}

			if (ballRb.linearVelocity.sqrMagnitude <= stopVelocity * stopVelocity)
			{
				float idle = ballIdleTimers.TryGetValue(ball, out float oldIdle) ? oldIdle + Time.deltaTime : Time.deltaTime;
				ballIdleTimers[ball] = idle;
				if (idle >= ballStopDuration) ballsToRemove.Add(ball);
			}
			else
			{
				ballIdleTimers[ball] = 0f;
			}
		}
		foreach (GameObject ball in ballsToRemove)
			ResolveShot(ball, ball != null && scoredBalls.Contains(ball));
	}

	private void ResolveShot(GameObject ball, bool goal)
	{
		if (shotResultSent || !shotInProgress) return;
		shotResultSent = true;
		RemovePendingBall(ball);
		turnState = TurnState.WaitingResults;
		Debug.Log("<color=cyan>[FootballVR] Enviando resultado: " + (goal ? "GOL" : "ERRO") + "</color>");
		_ = SubmitShotResult(goal);
	}

	private async Task SubmitShotResult(bool goal)
	{
		FirebaseManager manager = FirebaseManager.Instance;
		if (manager == null) return;
		bool success = await manager.SubmitShot(goal);
		if (!success) Debug.LogError("[FootballVR] Falha ao enviar resultado.");
		else Debug.Log("[FootballVR] Resultado enviado. Turno será atualizado.");
	}

	private void RemovePendingBall(GameObject ball)
	{
		pendingBalls.Remove(ball);
		if (ball != null)
		{
			ballIdleTimers.Remove(ball);
			ballElapsedTimers.Remove(ball);
			if (curvedBall != null && curvedBall.gameObject == ball) curvedBall = null;
		}
	}

	private void FixedUpdate()
	{
		if (curvedBall == null || curveTimer <= 0f) { curvedBall = null; return; }
		ApplyGoalCurve();
		curveTimer -= Time.fixedDeltaTime;
	}

	private void ApplyGoalCurve()
	{
		if (goalCenter == null || curvedBall == null) return;
		Vector3 currentDirection = curvedBall.linearVelocity;
		currentDirection.y = 0f;
		if (currentDirection.sqrMagnitude < 0.001f) return;
		currentDirection.Normalize();

		Vector3 goalDirection = goalCenter.position - curvedBall.position;
		goalDirection.y = 0f;
		if (goalDirection.sqrMagnitude < 0.001f) return;
		goalDirection.Normalize();

		float angle = Vector3.SignedAngle(currentDirection, goalDirection, Vector3.up);
		float correction = Mathf.Clamp(angle * curveStrength, -maxCurveAcceleration, maxCurveAcceleration);
		Vector3 lateralDirection = Vector3.Cross(Vector3.up, currentDirection).normalized;
		curvedBall.AddForce(lateralDirection * correction, ForceMode.Acceleration);
	}

	private void FinishGame()
	{
		if (finished) return;
		finished = true;
		turnState = TurnState.Finished;
		CancelInvoke(nameof(CleanupPreviousBall));
		ResetAllBalls();

		string result = (FirebaseManager.Instance.PlayerId == "Jogador1" ? player1Goals : player2Goals) > (FirebaseManager.Instance.PlayerId == "Jogador1" ? player2Goals : player1Goals)
			? "VITÓRIA!"
			: (FirebaseManager.Instance.PlayerId == "Jogador1" ? player1Goals : player2Goals) < (FirebaseManager.Instance.PlayerId == "Jogador1" ? player2Goals : player1Goals)
				? "DERROTA!"
				: "EMPATE!";

		Debug.Log("<color=yellow>[FootballVR] PARTIDA FINALIZADA!</color>\nJogador 1: " + player1Goals + " | Jogador 2: " + player2Goals + "\n" + result);
		onFinished?.Invoke();
	}
}