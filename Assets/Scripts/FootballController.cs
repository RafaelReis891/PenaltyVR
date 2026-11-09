
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class QuestFootKick : MonoBehaviour
{
	public AudioSource source;
	public float linearVelocity;
	private Rigidbody rb;

	[Header("Configuração do chute")]
	public float extraForce = 5f;
	public float upwardForce = 1.5f;

	[Header("Turnos")]
	[Min(1)]
	public int shotsPerPlayer = 5;

	[Min(1f)]
	public float turnTime = 5f;

	public int currentBall;

	private int playerShots;
	private int opponentShots;

	private float turnTimer;

	private enum TurnState
	{
		PlayerTurn,
		WaitingOpponent,
		WaitingPlayer,
		WaitingResults,
		Finished
	}

	private TurnState turnState = TurnState.PlayerTurn;

	[Header("Assistência de curva")]
	public Transform goalCenter;
	public float curveStrength = 3f;
	public float maxCurveAcceleration = 5f;
	public float curveDuration = 2f;

	private Rigidbody curvedBall;
	private float curveTimer;

	[Header("Configuração do adversário")]
	public float horizontalOffset = 2f;
	public float verticalOffset = 1.5f;

	[Header("Placar")]
	public int playerGoals;
	public int opponentGoals;

	[Header("Eventos")]
	public UnityEvent onPlayerGoal;
	public UnityEvent onOpponentGoal;
	public UnityEvent onFinished;

	[Header("Bolas")]
	public List<GameObject> balls;

	[Header("Finalização dos chutes")]
	[Min(0.1f)]
	public float ballStopDuration = 1.25f;

	[Min(1f)]
	public float maxBallResolveTime = 12f;

	[Min(0f)]
	public float stopVelocity = 0.15f;

	private enum ShotOwner
	{
		Player,
		Opponent
	}

	// Autor de cada bola.
	private readonly Dictionary<GameObject, ShotOwner> ballOwners =
		new Dictionary<GameObject, ShotOwner>();

	// Bolas que já marcaram gol.
	private readonly HashSet<GameObject> scoredBalls =
		new HashSet<GameObject>();

	// Bolas que ainda precisam ter seu resultado resolvido.
	private readonly HashSet<GameObject> pendingBalls =
		new HashSet<GameObject>();

	private readonly Dictionary<GameObject, float> ballIdleTimers =
		new Dictionary<GameObject, float>();

	private readonly Dictionary<GameObject, float> ballElapsedTimers =
		new Dictionary<GameObject, float>();

	private readonly List<GameObject> ballsToRemove =
		new List<GameObject>();

	private bool finished;
	private bool noMoreBalls;

	// --------------------------------------------------
	// INICIALIZAÇÃO
	// --------------------------------------------------

	void Start()
	{
		if (balls == null)
			balls = new List<GameObject>();

		rb = GetComponent<Rigidbody>();

		foreach (GameObject ball in balls)
		{
			if (ball != null)
				ball.SetActive(false);
		}

		currentBall = Mathf.Clamp(currentBall, 0, balls.Count);

		BeginPlayerTurn();
	}

	// --------------------------------------------------
	// UPDATE
	// --------------------------------------------------

	void Update()
	{
		if (finished)
			return;

		linearVelocity = rb.linearVelocity.magnitude * 3.6f;

		UpdatePendingBalls();

		if (finished)
			return;

		// Teste de chute pelo teclado.
		if (turnState == TurnState.PlayerTurn &&
			Input.GetKeyDown(KeyCode.Space))
		{
			SimularChuteAleatorio();
		}

		if (turnState == TurnState.WaitingResults)
		{
			if (pendingBalls.Count == 0)
				FinishGame();

			return;
		}

		if (turnState == TurnState.WaitingOpponent ||
			turnState == TurnState.WaitingPlayer)
		{
			turnTimer -= Time.deltaTime;

			if (turnTimer <= 0f)
			{
				if (turnState == TurnState.WaitingOpponent)
				{
					BeginOpponentTurn();
				}
				else
				{
					BeginPlayerTurn();
				}
			}
		}
	}

	// --------------------------------------------------
	// CONTROLE DAS BOLAS EM MOVIMENTO
	// --------------------------------------------------

	void UpdatePendingBalls()
	{
		ballsToRemove.Clear();

		foreach (GameObject ball in pendingBalls)
		{
			if (ball == null)
			{
				ballsToRemove.Add(ball);
				continue;
			}

			Rigidbody rb = ball.GetComponent<Rigidbody>();

			if (rb == null)
			{
				ballsToRemove.Add(ball);
				continue;
			}

			float elapsed = ballElapsedTimers[ball] + Time.deltaTime;

			ballElapsedTimers[ball] = elapsed;

			// Tempo máximo para a bola alcançar o gol.
			if (elapsed >= maxBallResolveTime)
			{
				Debug.Log(
					"[QuestFootKick] Tempo de resolução da bola esgotado."
				);

				ballsToRemove.Add(ball);
				continue;
			}

			// Verifica se a bola parou.
			if (rb.linearVelocity.sqrMagnitude <=
				stopVelocity * stopVelocity)
			{
				float idle = ballIdleTimers[ball] + Time.deltaTime;

				ballIdleTimers[ball] = idle;

				if (idle >= ballStopDuration)
				{
					Debug.Log(
						"[QuestFootKick] Bola parada. Tentativa encerrada."
					);

					ballsToRemove.Add(ball);
				}
			}
			else
			{
				ballIdleTimers[ball] = 0f;
			}
		}

		foreach (GameObject ball in ballsToRemove)
			RemovePendingBall(ball);

		// Todas as tentativas foram resolvidas.
		if (turnState == TurnState.WaitingResults &&
			pendingBalls.Count == 0)
		{
			FinishGame();
		}
	}

	void RemovePendingBall(GameObject ball)
	{
		pendingBalls.Remove(ball);

		if (ball != null)
		{
			ballIdleTimers.Remove(ball);
			ballElapsedTimers.Remove(ball);

			if (curvedBall != null &&
				curvedBall.gameObject == ball)
			{
				curvedBall = null;
			}
		}
	}

	// --------------------------------------------------
	// IDENTIFICAÇÃO DE GOLS
	// --------------------------------------------------

	public void Goal(GameObject ball)
	{
		if (ball == null || finished)
			return;

		// A bola precisa ter sido realmente chutada.
		if (!ballOwners.TryGetValue(ball, out ShotOwner owner))
		{
			Debug.LogWarning(
				"[QuestFootKick] Bola sem autor de chute registrado."
			);

			return;
		}

		// Evita gols duplicados.
		if (scoredBalls.Contains(ball))
			return;

		// Evita registrar bolas que já tiveram a tentativa encerrada.
		if (!pendingBalls.Contains(ball))
			return;

		scoredBalls.Add(ball);

		RemovePendingBall(ball);

		if (owner == ShotOwner.Player)
		{
			playerGoals++;

			Debug.Log(
				$"<color=green>GOL DO JOGADOR! " +
				$"PLACAR: {playerGoals} x {opponentGoals}</color>"
			);

			onPlayerGoal?.Invoke();
		}
		else
		{
			opponentGoals++;

			Debug.Log(
				$"<color=red>GOL DO ADVERSÁRIO! " +
				$"PLACAR: {playerGoals} x {opponentGoals}</color>"
			);

			onOpponentGoal?.Invoke();
		}

		if (turnState == TurnState.WaitingResults &&
			pendingBalls.Count == 0)
		{
			FinishGame();
		}
	}

	// --------------------------------------------------
	// CURVA DA BOLA
	// --------------------------------------------------

	void FixedUpdate()
	{		

		if (curvedBall == null)
			return;

		if (curveTimer <= 0f)
		{
			curvedBall = null;
			return;
		}

		ApplyGoalCurve();

		curveTimer -= Time.fixedDeltaTime;
	}

	void ApplyGoalCurve()
	{
		if (goalCenter == null || curvedBall == null)
			return;

		Vector3 velocity = curvedBall.linearVelocity;

		if (velocity.sqrMagnitude < 0.1f)
			return;

		Vector3 currentDirection = curvedBall.linearVelocity;
		currentDirection.y = 0f;

		if (currentDirection.sqrMagnitude < 0.001f)
			return;

		currentDirection.Normalize();

		Vector3 goalDirection = goalCenter.position - curvedBall.position;
		goalDirection.y = 0f;

		if (goalDirection.sqrMagnitude < 0.001f)
			return;

		goalDirection.Normalize();

		// Identifica para qual lado a bola precisa corrigir
		float angle = Vector3.SignedAngle(
			currentDirection,
			goalDirection,
			Vector3.up
		);

		// Limita a intensidade da correção
		float correction = Mathf.Clamp(
			angle * curveStrength,
			-maxCurveAcceleration,
			maxCurveAcceleration
		);

		// Direção lateral correta
		Vector3 lateralDirection = Vector3.Cross(
			Vector3.up,
			currentDirection
		).normalized;

		// Aplica a curva em direção ao centro do gol
		curvedBall.AddForce(
			lateralDirection * correction,
			ForceMode.Acceleration
		);

		curvedBall.AddForce(
			lateralDirection * correction,
			ForceMode.Acceleration
		);
	}

	// --------------------------------------------------
	// CONTROLE DAS BOLAS
	// --------------------------------------------------

	GameObject GetCurrentBall()
	{
		while (currentBall < balls.Count)
		{
			GameObject ball = balls[currentBall];

			if (ball != null)
				return ball;

			currentBall++;
		}

		return null;
	}

	bool ActivateCurrentBall()
	{
		GameObject ball = GetCurrentBall();

		if (ball == null)
		{
			Debug.LogWarning(
				"[QuestFootKick] Não existem mais bolas disponíveis."
			);

			noMoreBalls = true;
			CheckGameCompletion();

			return false;
		}

		ball.SetActive(true);

		Rigidbody rb = ball.GetComponent<Rigidbody>();

		if (rb == null)
		{
			Debug.LogError(
				$"[QuestFootKick] A bola {ball.name} não possui Rigidbody."
			);

			noMoreBalls = true;
			CheckGameCompletion();

			return false;
		}

		rb.isKinematic = false;
		rb.WakeUp();

		return true;
	}

	void AdvanceBall()
	{
		currentBall++;
	}

	// --------------------------------------------------
	// TURNOS
	// --------------------------------------------------

	void BeginPlayerTurn()
	{
		if (finished || noMoreBalls)
			return;

		if (playerShots >= shotsPerPlayer)
		{
			if (opponentShots >= shotsPerPlayer)
				CheckGameCompletion();

			return;
		}

		if (!ActivateCurrentBall())
			return;

		turnState = TurnState.PlayerTurn;

		Debug.Log(
			$"[QuestFootKick] SUA VEZ! " +
			$"Chutes: {playerShots}/{shotsPerPlayer} | " +
			$"Adversário: {opponentShots}/{shotsPerPlayer} | " +
			$"Placar: {playerGoals} x {opponentGoals}"
		);

		source.Play();
	}

	void BeginOpponentTurn()
	{
		if (finished || noMoreBalls)
			return;

		if (opponentShots >= shotsPerPlayer)
		{
			BeginPlayerTurn();
			return;
		}

		GameObject ball = GetCurrentBall();

		if (ball == null)
		{
			noMoreBalls = true;
			CheckGameCompletion();
			return;
		}

		if (!ActivateCurrentBall())
			return;

		source.Play();

		// O adversário realiza o chute.
		bool kicked = ExecuteRandomKick(
			ball,
			ShotOwner.Opponent
		);

		if (!kicked)
		{
			Debug.LogError(
				"[QuestFootKick] Não foi possível executar o chute do adversário."
			);

			noMoreBalls = true;
			CheckGameCompletion();
			return;
		}

		opponentShots++;

		AdvanceBall();

		Debug.Log(
			$"[QuestFootKick] ADVERSÁRIO CHUTOU! " +
			$"{opponentShots}/{shotsPerPlayer}"
		);

		CheckGameCompletion();

		if (turnState == TurnState.WaitingResults ||
			finished)
		{
			return;
		}

		turnState = TurnState.WaitingPlayer;
		turnTimer = turnTime;
	}

	void CompletePlayerTurn()
	{
		if (finished || turnState != TurnState.PlayerTurn)
			return;

		playerShots++;

		AdvanceBall();

		Debug.Log(
			$"[QuestFootKick] VOCÊ CHUTOU! " +
			$"{playerShots}/{shotsPerPlayer}"
		);

		CheckGameCompletion();

		if (turnState == TurnState.WaitingResults ||
			finished)
		{
			return;
		}

		turnState = TurnState.WaitingOpponent;
		turnTimer = turnTime;
	}

	// --------------------------------------------------
	// VERIFICAÇÃO DA PARTIDA
	// --------------------------------------------------

	void CheckGameCompletion()
	{
		bool allShotsCompleted =
			playerShots >= shotsPerPlayer &&
			opponentShots >= shotsPerPlayer;

		if (allShotsCompleted || noMoreBalls)
		{
			turnState = TurnState.WaitingResults;

			Debug.Log(
				"[QuestFootKick] Aguardando resolução das bolas..."
			);

			if (pendingBalls.Count == 0)
				FinishGame();
		}
	}

	// --------------------------------------------------
	// CHUTE DO JOGADOR
	// --------------------------------------------------

	private void OnCollisionEnter(Collision collision)
	{
		if (finished || turnState != TurnState.PlayerTurn)
			return;

		Rigidbody ballRb = collision.rigidbody;

		if (ballRb == null)
			return;

		if (!ballRb.gameObject.CompareTag("Ball"))
			return;

		GameObject current = GetCurrentBall();

		if (current == null || ballRb.gameObject != current)
			return;

		// Ignora contatos fracos ou acidentais
		float impactSpeed = collision.relativeVelocity.magnitude;

		// Recupera a direção física original da bola
		Vector3 kickDirection = ballRb.linearVelocity.normalized;

		// Se a bola estiver parada, utiliza a normal da colisão
		if (kickDirection.sqrMagnitude < 0.001f)
		{
			if (collision.contactCount == 0)
				return;

			kickDirection = -collision.GetContact(0).normal;
		}

		if (kickDirection.sqrMagnitude < 0.001f)
			return;

		// Mantém a elevação fixa, independente do controle
		kickDirection.y += upwardForce;
		kickDirection.Normalize();

		LaunchBall(
			ballRb,
			kickDirection,
			ShotOwner.Player
		);

		CompletePlayerTurn();
	}

	// --------------------------------------------------
	// CHUTE ALEATÓRIO
	// --------------------------------------------------

	[ContextMenu("Testar Chute Aleatório")]
	void SimularChuteAleatorio()
	{
		if (finished || turnState != TurnState.PlayerTurn)
		{
			Debug.LogWarning(
				"[QuestFootKick] Não é sua vez de chutar."
			);

			return;
		}

		GameObject ball = GetCurrentBall();

		if (ball == null)
			return;

		if (!ExecuteRandomKick(ball, ShotOwner.Player))
			return;

		CompletePlayerTurn();
	}

	bool ExecuteRandomKick(GameObject ball, ShotOwner owner)
	{
		if (ball == null || goalCenter == null)
			return false;

		Rigidbody ballRb = ball.GetComponent<Rigidbody>();

		if (ballRb == null)
			return false;

		int targetIndex = Random.Range(0, 5);

		Vector3 targetOffset = Vector3.zero;

		switch (targetIndex)
		{
			case 0:
				targetOffset = new Vector3(
					-horizontalOffset,
					verticalOffset,
					0f
				);
				break;

			case 1:
				targetOffset = new Vector3(
					horizontalOffset,
					verticalOffset,
					0f
				);
				break;

			case 2:
				targetOffset = new Vector3(
					-horizontalOffset,
					0f,
					0f
				);
				break;

			case 3:
				targetOffset = new Vector3(
					horizontalOffset,
					0f,
					0f
				);
				break;

			case 4:
				targetOffset = Vector3.zero;
				break;
		}

		Vector3 targetPosition =
			goalCenter.TransformPoint(targetOffset);

		Vector3 kickDirection =
			targetPosition - ballRb.position;

		kickDirection.y = 0f;

		if (kickDirection.sqrMagnitude < 0.001f)
			return false;

		kickDirection.Normalize();

		kickDirection.y = upwardForce;
		kickDirection.Normalize();

		LaunchBall(
			ballRb,
			kickDirection,
			owner
		);

		Debug.Log(
			$"[QuestFootKick] Chute executado. " +
			$"Autor: {owner} | Alvo: {targetIndex}"
		);

		return true;
	}

	// --------------------------------------------------
	// EXECUÇÃO DO IMPULSO
	// --------------------------------------------------

	void LaunchBall(
	Rigidbody ballRb,
	Vector3 direction,
	ShotOwner owner
)
	{
		if (ballRb == null)
			return;

		GameObject ball = ballRb.gameObject;

		// Registra a autoria antes de aplicar o impulso
		ballOwners[ball] = owner;

		// Registra que esta bola precisa ser resolvida
		pendingBalls.Add(ball);

		ballIdleTimers[ball] = 0f;
		ballElapsedTimers[ball] = 0f;

		ballRb.isKinematic = false;
		ballRb.WakeUp();

		// Impulso exclusivamente pela direção física calculada
		ballRb.AddForce(
			direction * extraForce,
			ForceMode.Impulse
		);

		// A assistência de curva pertence somente ao jogador
		if (owner == ShotOwner.Player && goalCenter != null)
		{
			curvedBall = ballRb;
			curveTimer = curveDuration;
		}
		else
		{
			curvedBall = null;
			curveTimer = 0f;
		}
	}

	// --------------------------------------------------
	// FINALIZAÇÃO
	// --------------------------------------------------

	void FinishGame()
	{
		if (finished)
			return;

		finished = true;
		turnState = TurnState.Finished;

		string result;

		if (playerGoals > opponentGoals)
			result = "VITÓRIA DO JOGADOR!";
		else if (opponentGoals > playerGoals)
			result = "VITÓRIA DO ADVERSÁRIO!";
		else
			result = "EMPATE!";

		Debug.Log(
			$"<color=yellow>[QuestFootKick] PARTIDA FINALIZADA!</color>\n" +
			$"Jogador: {playerGoals} gols | " +
			$"Adversário: {opponentGoals} gols\n" +
			$"{result}\n" +
			$"Chutes: {playerShots} x {opponentShots}"
		);

		onFinished?.Invoke();

		Invoke(nameof(RestartGame), 5f);
	}

	void RestartGame()
	{
		SceneManager.LoadScene(
			SceneManager.GetActiveScene().buildIndex
		);
	}
}
