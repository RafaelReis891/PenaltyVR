
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

public class QuestFootKick : MonoBehaviour
{
	public enum PlayerRole { Player1, Player2 }

	[Header("Modo de Jogo")]
	public bool PLAY_ALONE = false;
	[Tooltip("Modo local de teste: não conecta ao servidor e permite chutes infinitos.")]
	public bool DEBUG_MODE = false;
	public Button StartGameOffline;
	public PlayableDirector timeline;

	[Header("Configuração do Jogador")]
	[Tooltip("Defina no Inspector: Esta build é o Jogador 1 ou Jogador 2?")]
	public PlayerRole myRole = PlayerRole.Player1;

	[Tooltip("Nome que será exibido no painel Admin.")]
	public string playerDisplayName = "Jogador 1";
	public TMP_Text playerLocalLabel;

	public AudioSource source;
	public float linearVelocity;
	private Rigidbody rb;

	[Header("Fonte do movimento VR")]
	[Tooltip("Transform rastreado usado para medir o chute. Pode ser o controle, a mão ou o pé. Não precisa ser o objeto Kick.")]
	public Transform motionSource;
	[Tooltip("Câmera/headset usado para remover o deslocamento global causado pela cabeça.")]
	public Transform motionReference;
	public bool compensateHeadMovement = true;
	[Tooltip("Lê diretamente a posição do controle direito pelo subsistema XR, ignorando o movimento da cabeça.")]
	public bool useRightHandDevicePose = true;
	public XRNode trackedHand = XRNode.RightHand;
	[Tooltip("Transform da origem de tracking usado para converter a pose local do controle para o mundo.")]
	public Transform trackingOrigin;
	private Transform activeMotionSource;

	[Header("Goleiro")]
	public GoalKeeperActions goalKeeper;

	[Header("Configuração do chute")]
	public float extraForce = 5f;
	public float upwardForce = 1.5f;
	[Tooltip("Velocidade mínima do pé para iniciar uma tentativa de chute.")]
	public float minimumKickSpeed = 0.85f;
	[Tooltip("Velocidade mínima do pé em direção à bola durante o avanço.")]
	public float minimumForwardKickSpeed = 0.55f;
	[Tooltip("Velocidade máxima usada para normalizar a intensidade do gesto.")]
	public float maximumKickSpeed = 5.5f;
	[Tooltip("Distância máxima entre o Kick e a bola para aceitar o gesto.")]
	public float kickContactDistance = 0.85f;
	public float minimumBallSpeed = 7f;
	public float maximumBallSpeed = 18f;
	[Tooltip("Multiplicador geral da força. O valor original 13 equivale a 1x.")]
	public float shotForceMultiplierReference = 13f;
	[Range(0f, 1f)]
	public float movementDirectionInfluence = 0.8f;
	public float kickCooldown = 0.35f;
	[Tooltip("Exige um recuo antes do avanço. Reduz falsos chutes causados por movimentos da cabeça.")]
	public bool requireBackSwing = false;
	[Tooltip("Ativa a assistência que curva a bola automaticamente em direção ao centro do gol.")]
	public bool assistCurveTowardGoal = false;
	public bool disableKickFixedJoint = true;

	[Header("Diagnóstico do movimento")]
	public bool showKickDebug;
	[Tooltip("Label opcional para mostrar o estado do gesto. Se vazio, será criado automaticamente no Kick em DEBUG_MODE.")]
	public TMP_Text gestureStateLabel;
	public bool createDebugGestureLabel = true;
	public float kickSpeed;
	public float kickForwardSpeed;
	public float lastShotSpeed;
	public string kickGestureState = "Idle";
	private TMP_Text runtimeGestureStateLabel;

	private FixedJoint kickJoint;
	private Vector3 previousKickPosition;
	private Vector3 smoothedKickVelocity;

	// Deslocamento acumulado do controle durante o avanço do chute.
	// É este vetor que determina a direção final da bola.
	private Vector3 kickDirectionDisplacement;

	private bool kickMotionInitialized;
	private float previousForwardSpeed;
	private float kickCooldownTimer;
	private bool kickBackSwingDetected;
	private bool adminShotRequested;

	[Header("Turnos")]
	[Min(1)] public int shotsPerPlayer = 5;
	public int currentBall;

	private int playerShots;
	private int opponentShots;
	private bool reloadAfterSoloShot;
	private enum TurnState
	{
		WaitingRoom,
		WaitingOpponent,
		PlayerTurn,
		WaitingResults,
		Finished
	}

	private TurnState turnState = TurnState.WaitingRoom;

	[Header("Assistência de curva")]
	public Transform goalCenter;
	[Tooltip("Câmera usada para mirar no gol. Se vazio, usa Camera.main.")]
	public Camera aimCamera;
	[Tooltip("Faixa central em metros. Dentro dela o chute é considerado CENTRAL.")]
	public float aimCenterOffset = 0.35f;
	[Tooltip("Limite lateral da mira no plano do gol.")]
	public float aimMaxHorizontalOffset = 2.5f;
	[Tooltip("Altura fixa do alvo no espaço local do gol. 0 = centro vertical do gol.")]
	public float aimTargetHeight = 0f;
	public string aimSide = "CENTER";
	public float aimHorizontalOffset;
	public Vector3 aimPointWorld;
	public bool aimRayValid;
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

	private int lastPlayer1Score;
	private int lastPlayer2Score;
	private int lastProcessedTurn = -1;

	private string lastRoomStatus = "";

	[Header("Menu de calibração F4")]
	private bool showTuningMenu;
	private Rect tuningMenuRect = new Rect(20f, 20f, 390f, 650f);
	private const int TuningWindowId = 47291;
	private const string TuningPrefsPrefix = "FootballVR.KickTuning.";

	private string extraForceText;
	private string upwardForceText;
	private string minimumKickSpeedText;
	private string kickContactDistanceText;
	private string minimumBallSpeedText;
	private string maximumBallSpeedText;
	private string aimCenterOffsetText;
	private string aimMaxHorizontalOffsetText;
	private string curveStrengthText;

	private void Awake()
	{
		LoadTuningSettings();
	}

	private void LoadTuningSettings()
	{
		extraForce = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(extraForce), extraForce);
		upwardForce = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(upwardForce), upwardForce);
		minimumKickSpeed = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(minimumKickSpeed), minimumKickSpeed);
		kickContactDistance = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(kickContactDistance), kickContactDistance);
		minimumBallSpeed = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(minimumBallSpeed), minimumBallSpeed);
		maximumBallSpeed = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(maximumBallSpeed), maximumBallSpeed);
		aimCenterOffset = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(aimCenterOffset), aimCenterOffset);
		aimMaxHorizontalOffset = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(aimMaxHorizontalOffset), aimMaxHorizontalOffset);
		curveStrength = PlayerPrefs.GetFloat(TuningPrefsPrefix + nameof(curveStrength), curveStrength);
		showKickDebug = PlayerPrefs.GetInt(TuningPrefsPrefix + nameof(showKickDebug), showKickDebug ? 1 : 0) == 1;

		RefreshTuningTextFields();
	}

	private void SaveTuningSettings()
	{
		TryApplyFloat(extraForceText, ref extraForce);
		TryApplyFloat(upwardForceText, ref upwardForce);
		TryApplyFloat(minimumKickSpeedText, ref minimumKickSpeed);
		TryApplyFloat(kickContactDistanceText, ref kickContactDistance);
		TryApplyFloat(minimumBallSpeedText, ref minimumBallSpeed);
		TryApplyFloat(maximumBallSpeedText, ref maximumBallSpeed);
		TryApplyFloat(aimCenterOffsetText, ref aimCenterOffset);
		TryApplyFloat(aimMaxHorizontalOffsetText, ref aimMaxHorizontalOffset);
		TryApplyFloat(curveStrengthText, ref curveStrength);

		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(extraForce), extraForce);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(upwardForce), upwardForce);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(minimumKickSpeed), minimumKickSpeed);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(kickContactDistance), kickContactDistance);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(minimumBallSpeed), minimumBallSpeed);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(maximumBallSpeed), maximumBallSpeed);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(aimCenterOffset), aimCenterOffset);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(aimMaxHorizontalOffset), aimMaxHorizontalOffset);
		PlayerPrefs.SetFloat(TuningPrefsPrefix + nameof(curveStrength), curveStrength);
		PlayerPrefs.SetInt(TuningPrefsPrefix + nameof(showKickDebug), showKickDebug ? 1 : 0);
		PlayerPrefs.Save();

		RefreshTuningTextFields();
		Debug.Log("[QuestFootKick] Parâmetros do chute salvos.");
	}

	private void TryApplyFloat(string text, ref float value)
	{
		if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
			value = parsed;
	}

	private void RefreshTuningTextFields()
	{
		extraForceText = extraForce.ToString("0.###", CultureInfo.InvariantCulture);
		upwardForceText = upwardForce.ToString("0.###", CultureInfo.InvariantCulture);
		minimumKickSpeedText = minimumKickSpeed.ToString("0.###", CultureInfo.InvariantCulture);
		kickContactDistanceText = kickContactDistance.ToString("0.###", CultureInfo.InvariantCulture);
		minimumBallSpeedText = minimumBallSpeed.ToString("0.###", CultureInfo.InvariantCulture);
		maximumBallSpeedText = maximumBallSpeed.ToString("0.###", CultureInfo.InvariantCulture);
		aimCenterOffsetText = aimCenterOffset.ToString("0.###", CultureInfo.InvariantCulture);
		aimMaxHorizontalOffsetText = aimMaxHorizontalOffset.ToString("0.###", CultureInfo.InvariantCulture);
		curveStrengthText = curveStrength.ToString("0.###", CultureInfo.InvariantCulture);
	}

	private void Start()
	{
		Debug.Log("[QuestFootKick] Iniciando. Papel configurado: " + myRole);

		if (playerLocalLabel != null)
		{
			playerLocalLabel.text =
				$"{playerDisplayName}\r\n<size=36>Chutes: 0/{(DEBUG_MODE ? "∞" : PLAY_ALONE ? "1" : shotsPerPlayer.ToString())}\r\nGols: 0";
		}

		if (balls == null)
			balls = new List<GameObject>();

		rb = GetComponent<Rigidbody>();
		activeMotionSource = motionSource;
		if (activeMotionSource == null)
			Debug.LogError("[QuestFootKick] Motion Source não configurado. O chute foi bloqueado para evitar leitura do Kick/cabeça.");

		kickJoint = GetComponent<FixedJoint>();

		if (disableKickFixedJoint)
		{
			if (kickJoint != null)
			{
				Destroy(kickJoint);
				kickJoint = null;
			}

			if (rb != null)
			{
				rb.isKinematic = true;
				rb.useGravity = false;
				rb.interpolation = RigidbodyInterpolation.Interpolate;
			}
		}

		kickMotionInitialized = false;
		smoothedKickVelocity = Vector3.zero;
		kickDirectionDisplacement = Vector3.zero;

		if (activeMotionSource != null)
		{
			// A primeira amostra válida será capturada no LateUpdate.
			kickMotionInitialized = false;
			smoothedKickVelocity = Vector3.zero;
			kickDirectionDisplacement = Vector3.zero;
			kickBackSwingDetected = false;
		}

		if (DEBUG_MODE && createDebugGestureLabel)
			CreateDebugGestureLabel();

		foreach (GameObject ball in balls)
		{
			if (ball == null)
				continue;

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

		if (DEBUG_MODE)
		{
			Debug.Log("<color=yellow>[QuestFootKick] DEBUG_MODE ATIVADO. Jogo local, sem servidor e com chutes infinitos.</color>");
			playerShots = 0;
			opponentShots = 0;
			turnState = TurnState.PlayerTurn;

			if (!ActivateCurrentBall())
			{
				Debug.LogError("[QuestFootKick] DEBUG_MODE não conseguiu ativar a bola.");
				turnState = TurnState.WaitingRoom;
				return;
			}

			if (source != null && !source.isPlaying)
				source.Play();

			return;
		}

		// MODO SOLO
		if (PLAY_ALONE)
		{
			Debug.Log("[QuestFootKick] MODO SOLO ATIVADO.");

			playerShots = 0;
			opponentShots = 0;

			turnState = TurnState.PlayerTurn;

			if (!ActivateCurrentBall())
			{
				Debug.LogError("[QuestFootKick] Não foi possível ativar a bola para o modo solo.");
				turnState = TurnState.WaitingRoom;
				return;
			}

			Debug.Log("<color=green>[FootballVR] MODO SOLO: VOCÊ TEM 1 CHUTE!</color>");

			if (source != null && !source.isPlaying)
				source.Play();

			if (StartGameOffline != null)
			{
				StartGameOffline.gameObject.SetActive(true);
				StartGameOffline.onClick.RemoveListener(StartGameButton);
				StartGameOffline.onClick.AddListener(StartGameButton);
			}

			if (timeline != null)
				timeline.Pause();

			return;
		}
		else
		{
			if(StartGameOffline)
				StartGameOffline.gameObject.SetActive(false);
		}

		// MODO MULTIPLAYER
		if (FirebaseManager.Instance == null)
		{
			Debug.LogError("[QuestFootKick] FirebaseManager não encontrado na cena.");
			return;
		}

		FirebaseManager.Instance.OnRoomUpdated += HandleRoomUpdated;
		FirebaseManager.Instance.OnRoomJoined += OnSuccessfullyJoinedRoom;

		if (FirebaseManager.Instance.IsInRoom &&
			FirebaseManager.Instance.CurrentRoom != null)
		{
			HandleRoomUpdated(FirebaseManager.Instance.CurrentRoom);
		}
	}

	private void OnDestroy()
	{
		if (FirebaseManager.Instance != null)
		{
			FirebaseManager.Instance.OnRoomUpdated -= HandleRoomUpdated;
			FirebaseManager.Instance.OnRoomJoined -= OnSuccessfullyJoinedRoom;
		}
	}


		private void LateUpdate()
		{
			if (kickMotionInitialized)
				return;

			if (!TryGetMotionSamplePosition(out Vector3 position))
				return;

			previousKickPosition = position;
			smoothedKickVelocity = Vector3.zero;
			kickDirectionDisplacement = Vector3.zero;

			kickMotionInitialized = true;
		}


	void StartGameButton()
	{
		if (timeline != null)
			timeline.Play();
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.F4))
		{
			showTuningMenu = !showTuningMenu;
			if (showTuningMenu)
				RefreshTuningTextFields();
		}

		if (rb != null)
			linearVelocity = rb.linearVelocity.magnitude * 3.6f;

		// O tracking dos controles é atualizado no ciclo de frame do XR.
		// A detecção precisa acompanhar o Transform, não apenas o passo físico.
		UpdateKickMotion();
		UpdateAimState();
		UpdateGestureStateLabel();

		if (!finished)
			UpdatePendingBalls();

		if (!finished &&
			turnState == TurnState.PlayerTurn &&
			Input.GetKeyDown(KeyCode.Space))
		{
			SimularChuteAleatorio();
		}

		if (adminShotRequested)
		{
			adminShotRequested = false;
			SimularChuteAleatorio();
		}
	}

	public void RequestAdminRandomShot()
	{
		if (finished ||
			turnState != TurnState.PlayerTurn ||
			shotInProgress)
		{
			Debug.LogWarning(
				"[QuestFootKick] Comando Admin ignorado: não é a vez do jogador ou já existe um chute.");
			return;
		}

		adminShotRequested = true;

		Debug.Log("[QuestFootKick] Comando de chute recebido do painel Admin.");
	}

	private void CreateDebugGestureLabel()
	{
		if (gestureStateLabel != null)
			return;

		GameObject labelObject = new GameObject("GestureStateDebugLabel");
		labelObject.transform.SetParent(transform, false);
		labelObject.transform.localPosition = new Vector3(0f, 0.9f, 0f);
		labelObject.transform.localScale = Vector3.one * 0.02f;

		TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
		label.fontSize = 3.5f;
		label.alignment = TextAlignmentOptions.Center;
		label.color = Color.yellow;
		label.text = "GestureState: Idle";
		labelObject.AddComponent<GestureDebugBillboard>();

		gestureStateLabel = label;
		runtimeGestureStateLabel = label;
	}

	private void UpdateGestureStateLabel()
	{
		TMP_Text label = gestureStateLabel != null ? gestureStateLabel : runtimeGestureStateLabel;
		if (label == null)
			return;

		bool shouldShowLabel = DEBUG_MODE && showKickDebug;
		label.gameObject.SetActive(shouldShowLabel);
		if (!shouldShowLabel)
			return;

		label.text =
			$"GestureState: {kickGestureState}\n" +
			$"Aim: {aimSide} ({aimHorizontalOffset:F2})\n" +
			$"Speed: {kickSpeed:F2} m/s\n" +
			$"Forward: {kickForwardSpeed:F2} m/s\n" +
			$"Shot: {lastShotSpeed:F2} m/s";

		label.color = kickGestureState switch
		{
			"BackSwing" => Color.yellow,
			"ForwardSwing" => Color.cyan,
			"KickDetected" => Color.green,
			_ => Color.white
		};
	}

	private void OnGUI()
	{
		if (!showTuningMenu)
			return;

		GUI.color = Color.white;
		tuningMenuRect = GUI.Window(
			TuningWindowId,
			tuningMenuRect,
			DrawTuningWindow,
			"Football VR - Calibracao F4");
	}

	private void DrawTuningWindow(int windowId)
	{
		GUILayout.BeginVertical();
		GUILayout.Label("Altere os valores e pressione SALVAR.");
		GUILayout.Space(6f);

		extraForceText = DrawTuningField("ExtraForce", extraForceText);
		upwardForceText = DrawTuningField("UpwardForce", upwardForceText);
		minimumKickSpeedText = DrawTuningField("MinimumKickSpeed", minimumKickSpeedText);
		kickContactDistanceText = DrawTuningField("KickContactDistance", kickContactDistanceText);
		minimumBallSpeedText = DrawTuningField("MinimumBallSpeed", minimumBallSpeedText);
		maximumBallSpeedText = DrawTuningField("MaximumBallSpeed", maximumBallSpeedText);
		showKickDebug = GUILayout.Toggle(showKickDebug, "ShowKickDebug");
		aimCenterOffsetText = DrawTuningField("AimCenterOffset", aimCenterOffsetText);
		aimMaxHorizontalOffsetText = DrawTuningField("AimMaxHorizontalOffset", aimMaxHorizontalOffsetText);
		curveStrengthText = DrawTuningField("CurveStrength", curveStrengthText);

		GUILayout.Space(8f);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("SALVAR"))
			SaveTuningSettings();

		if (GUILayout.Button("CARREGAR"))
			LoadTuningSettings();

		if (GUILayout.Button("FECHAR"))
			showTuningMenu = false;
		GUILayout.EndHorizontal();

		GUILayout.Space(5f);
		GUILayout.Label("F4: abrir/fechar menu");
		GUILayout.EndVertical();

		GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
	}

	private string DrawTuningField(string label, string value)
	{
		GUILayout.BeginHorizontal();
		GUILayout.Label(label, GUILayout.Width(190f));
		string result = GUILayout.TextField(value ?? string.Empty);
		GUILayout.EndHorizontal();
		return result;
	}

	private void UpdateAimState()
	{
		Camera cameraToUse = aimCamera != null ? aimCamera : Camera.main;

		if (goalCenter == null || cameraToUse == null)
		{
			aimRayValid = false;
			aimSide = "CENTER";
			aimHorizontalOffset = 0f;
			return;
		}

		Ray gazeRay = new Ray(
			cameraToUse.transform.position,
			cameraToUse.transform.forward);
		Plane goalPlane = new Plane(goalCenter.forward, goalCenter.position);

		if (!goalPlane.Raycast(gazeRay, out float distance) || distance < 0f)
		{
			aimRayValid = false;
			aimSide = "CENTER";
			aimHorizontalOffset = 0f;
			return;
		}

		Vector3 gazeHit = gazeRay.GetPoint(distance);
		Vector3 localHit = goalCenter.InverseTransformPoint(gazeHit);
		aimHorizontalOffset = Mathf.Clamp(
			localHit.x,
			-aimMaxHorizontalOffset,
			aimMaxHorizontalOffset);

		if (aimHorizontalOffset < -aimCenterOffset)
			aimSide = "LEFT";
		else if (aimHorizontalOffset > aimCenterOffset)
			aimSide = "RIGHT";
		else
			aimSide = "CENTER";

		Vector3 localTarget = new Vector3(
			aimHorizontalOffset,
			aimTargetHeight,
			0f);
		aimPointWorld = goalCenter.TransformPoint(localTarget);
		aimRayValid = true;

		Debug.DrawRay(
			cameraToUse.transform.position,
			cameraToUse.transform.forward * distance,
			Color.cyan);
		Debug.DrawLine(
			gazeHit,
			aimPointWorld,
			Color.yellow);
	}

	private bool TryGetMotionSamplePosition(out Vector3 position)
{
    if (activeMotionSource == null)
    {
        position = Vector3.zero;
        return false;
    }

    // Única fonte do gesto: o Transform explicitamente configurado em
    // motionSource. Não usa câmera, XRNode, trackingOrigin ou pose de HMD.
    position = activeMotionSource.position;
    return true;
}


private Vector3 GetMotionWorldPosition()
{
    if (!TryGetMotionSamplePosition(out Vector3 worldPosition))
        return Vector3.zero;

    return worldPosition;
}


private Vector3 GetKickDirectionWorld()
{
    Vector3 direction = kickDirectionDisplacement;

    if (trackingOrigin != null)
        direction = trackingOrigin.TransformDirection(direction);

    return direction;
}

	private void OnSuccessfullyJoinedRoom()
	{
		if (FirebaseManager.Instance == null)
			return;

		Debug.Log(
			"[QuestFootKick] Entrou na sala " +
			FirebaseManager.Instance.RoomCode +
			" como " +
			FirebaseManager.Instance.PlayerId + ".");
	}

	private void HandleRoomUpdated(FirebaseManager.RoomState room)
	{
		if (room == null || FirebaseManager.Instance == null)
			return;

		bool startingFreshMatch =
			room.Status == "Playing" &&
			lastRoomStatus == "Waiting" &&
			room.TurnNumber == 0;

		if ((lastRoomStatus == "Finished" && room.Status == "Waiting") ||
			(finished && room.Status != "Finished") ||
			startingFreshMatch)
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

		if (goalKeeper != null)
			goalKeeper.ForceReset();

		ResetAllBalls();
	}

	private void ResetAllBalls()
	{
		foreach (GameObject ball in balls)
		{
			if (ball == null)
				continue;

			BallSound ballSound = ball.GetComponent<BallSound>();

			if (ballSound != null)
				ballSound.ResetState();

			Rigidbody ballRb = ball.GetComponent<Rigidbody>();

			if (ballRb != null)
			{
				ballRb.linearVelocity = Vector3.zero;
				ballRb.angularVelocity = Vector3.zero;
				ballRb.isKinematic = true;
			}

			if (initialBallPositions.TryGetValue(ball, out Vector3 position))
				ball.transform.position = position;

			if (initialBallRotations.TryGetValue(ball, out Quaternion rotation))
				ball.transform.rotation = rotation;

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

			if (i == currentBall || ball == null || !ball.activeSelf)
				continue;

			Rigidbody ballRb = ball.GetComponent<Rigidbody>();

			if (ballRb != null)
			{
				ballRb.linearVelocity = Vector3.zero;
				ballRb.angularVelocity = Vector3.zero;
				ballRb.isKinematic = true;
			}

			if (initialBallPositions.TryGetValue(ball, out Vector3 position))
				ball.transform.position = position;

			if (initialBallRotations.TryGetValue(ball, out Quaternion rotation))
				ball.transform.rotation = rotation;

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
		if (finished ||
			FirebaseManager.Instance == null ||
			!FirebaseManager.Instance.IsMyTurn())
			return;

		if (turnState == TurnState.PlayerTurn &&
			(shotInProgress || activeShotBall != null))
			return;

		if (playerShots >= shotsPerPlayer)
		{
			turnState = TurnState.WaitingOpponent;
			return;
		}

		if (!ActivateCurrentBall())
			return;

		turnState = TurnState.PlayerTurn;

		Debug.Log(
			"<color=green>[FootballVR] SUA VEZ!</color> Chutes: " +
			playerShots + "/" + shotsPerPlayer);

		if (source != null && !source.isPlaying)
			source.Play();
	}

	private GameObject GetCurrentBall()
	{
		return currentBall >= 0 && currentBall < balls.Count
			? balls[currentBall]
			: null;
	}

	private bool ActivateCurrentBall()
	{
		GameObject ball = GetCurrentBall();

		if (ball == null)
			return false;

		if (activeShotBall == ball && ball.activeSelf)
			return true;

		ball.SetActive(true);

		Rigidbody ballRb = ball.GetComponent<Rigidbody>();

		if (ballRb == null)
			return false;

		ballRb.linearVelocity = Vector3.zero;
		ballRb.angularVelocity = Vector3.zero;
		ballRb.isKinematic = false;
		ballRb.WakeUp();

		activeShotBall = ball;

		return true;
	}

	[ContextMenu("Testar Chute Aleatório")]
	public void SimularChuteAleatorio()
	{
		if (finished ||
			turnState != TurnState.PlayerTurn ||
			shotInProgress)
			return;

		if (!PLAY_ALONE && !DEBUG_MODE &&
			(FirebaseManager.Instance == null ||
			 !FirebaseManager.Instance.IsMyTurn()))
			return;

		GameObject ball = GetCurrentBall();

		if (ball == null)
			return;

		if (ExecuteRandomKick(ball))
		{
			Debug.Log("[FootballVR] Chute de teste executado.");

			if (PLAY_ALONE)
				reloadAfterSoloShot = true;
		}
	}

	private bool ExecuteRandomKick(GameObject ball)
	{
		if (ball == null || goalCenter == null)
			return false;

		Rigidbody ballRb = ball.GetComponent<Rigidbody>();

		if (ballRb == null)
			return false;

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

		LaunchBall(ballRb, direction, extraForce);

		return true;
	}

	private void LaunchBall(Rigidbody ballRb, Vector3 direction, float launchSpeed = -1f)
	{
		if (ballRb == null || shotInProgress)
			return;

		if (!PLAY_ALONE && !DEBUG_MODE &&
			(FirebaseManager.Instance == null ||
			 !FirebaseManager.Instance.IsMyTurn()))
			return;

		shotInProgress = true;
		shotResultSent = false;

		activeShotBall = ballRb.gameObject;

		pendingBalls.Add(activeShotBall);

		ballIdleTimers[activeShotBall] = 0f;
		ballElapsedTimers[activeShotBall] = 0f;

		ballRb.isKinematic = false;
		ballRb.WakeUp();

		if (launchSpeed <= 0f)
			launchSpeed = extraForce;

		ballRb.linearVelocity = direction * launchSpeed;
		ballRb.angularVelocity = Vector3.zero;

		if (assistCurveTowardGoal && goalCenter != null)
		{
			curvedBall = ballRb;
			curveTimer = curveDuration;
		}

		Debug.Log("[FootballVR] Chute realizado. Aguardando resultado físico.");
	}

	public void Goal(GameObject ball)
	{
		if (ball == null ||
			finished ||
			!shotInProgress ||
			shotResultSent)
			return;

		if (ball != activeShotBall ||
			!pendingBalls.Contains(ball) ||
			scoredBalls.Contains(ball))
			return;

		scoredBalls.Add(ball);

		Debug.Log("[FootballVR] Gol detectado localmente pelo trigger.");

		ResolveShot(ball, true);
	}

	private void UpdatePendingBalls()
	{
		ballsToRemove.Clear();

		foreach (GameObject ball in pendingBalls)
		{
			if (ball == null)
			{
				ballsToRemove.Add(ball);
				continue;
			}

			Rigidbody ballRb = ball.GetComponent<Rigidbody>();

			if (ballRb == null)
			{
				ballsToRemove.Add(ball);
				continue;
			}

			float elapsed = ballElapsedTimers.TryGetValue(ball, out float oldElapsed)
				? oldElapsed + Time.deltaTime
				: Time.deltaTime;

			ballElapsedTimers[ball] = elapsed;

			if (elapsed >= maxBallResolveTime)
			{
				ballsToRemove.Add(ball);
				continue;
			}

			if (ballRb.linearVelocity.sqrMagnitude <= stopVelocity * stopVelocity)
			{
				float idle = ballIdleTimers.TryGetValue(ball, out float oldIdle)
					? oldIdle + Time.deltaTime
					: Time.deltaTime;

				ballIdleTimers[ball] = idle;

				if (idle >= ballStopDuration)
					ballsToRemove.Add(ball);
			}
			else
			{
				ballIdleTimers[ball] = 0f;
			}
		}

		foreach (GameObject ball in ballsToRemove)
		{
			ResolveShot(
				ball,
				ball != null && scoredBalls.Contains(ball));
		}
	}

	private void ResolveShot(GameObject ball, bool goal)
	{
		if (shotResultSent || !shotInProgress)
			return;

		shotResultSent = true;

		RemovePendingBall(ball);

		turnState = TurnState.WaitingResults;

		Debug.Log(
			"<color=cyan>[FootballVR] Enviando resultado: " +
			(goal ? "GOL" : "ERRO") + "</color>");

		_ = SubmitShotResult(goal);
	}

	private async Task SubmitShotResult(bool goal)
	{
		if (DEBUG_MODE)
		{
			if (goal)
			{
				if (myRole == PlayerRole.Player1)
					player1Goals++;
				else
					player2Goals++;

				onPlayerGoal?.Invoke();
				Debug.Log("<color=green>[FootballVR] DEBUG_MODE: GOL!</color>");
			}
			else
			{
				Debug.Log("<color=red>[FootballVR] DEBUG_MODE: chute sem gol.</color>");
			}

			playerShots++;
			int nextBall = balls.Count > 0 ? (currentBall + 1) % balls.Count : 0;
			ResetAllBalls();
			currentBall = nextBall;
			turnState = TurnState.PlayerTurn;

			if (playerLocalLabel != null)
			{
				int goals = myRole == PlayerRole.Player1 ? player1Goals : player2Goals;
				playerLocalLabel.text =
					$"{playerDisplayName}\r\n<size=36>Chutes: {playerShots}/∞\r\nGols: {goals}";
			}

			ActivateCurrentBall();
			return;
		}

		if (PLAY_ALONE)
		{
			if (goal)
			{
				if (myRole == PlayerRole.Player1)
					player1Goals++;
				else
					player2Goals++;

				onPlayerGoal?.Invoke();

				Debug.Log("<color=green>[FootballVR] GOL NO MODO SOLO!</color>");
			}
			else
			{
				Debug.Log("<color=red>[FootballVR] CHUTE SEM GOL!</color>");
			}

			playerShots = 1;
			turnState = TurnState.Finished;

			if (playerLocalLabel != null)
			{
				playerLocalLabel.text =
					$"{playerDisplayName}\r\n<size=36>Chutes: 1/1\r\nGols: {(goal ? 1 : 0)}";
			}

			FinishGame();

			if (reloadAfterSoloShot)
			{
				Debug.Log("[FootballVR] Reiniciando a cena em 5 segundos...");
				StartCoroutine(ReloadCurrentSceneAfterDelay());
			}

			return;
		}

		FirebaseManager manager = FirebaseManager.Instance;

		if (manager == null)
			return;

		bool success = await manager.SubmitShot(goal);

		if (!success)
			Debug.LogError("[FootballVR] Falha ao enviar resultado.");
		else
			Debug.Log("[FootballVR] Resultado enviado. Turno será atualizado.");
	}

	private IEnumerator ReloadCurrentSceneAfterDelay()
	{
		yield return new WaitForSeconds(5f);

		int currentScene = SceneManager.GetActiveScene().buildIndex;

		Debug.Log("[FootballVR] Recarregando cena: " + currentScene);

		SceneManager.LoadScene(currentScene);
	}

	private void RemovePendingBall(GameObject ball)
	{
		pendingBalls.Remove(ball);

		if (ball != null)
		{
			ballIdleTimers.Remove(ball);
			ballElapsedTimers.Remove(ball);

			if (curvedBall != null && curvedBall.gameObject == ball)
				curvedBall = null;
		}
	}

	private void FixedUpdate()
	{
		if (curvedBall == null || curveTimer <= 0f)
		{
			curvedBall = null;
			return;
		}

		ApplyGoalCurve();

		curveTimer -= Time.fixedDeltaTime;
	}

	
private void UpdateKickMotion()
{
    if (kickCooldownTimer > 0f)
        kickCooldownTimer -= Time.deltaTime;

    if (!TryGetMotionSamplePosition(out Vector3 currentSample))
    {
        kickGestureState = "TrackingLost";

        kickBackSwingDetected = false;
        smoothedKickVelocity = Vector3.zero;
        kickDirectionDisplacement = Vector3.zero;

        kickMotionInitialized = false;
        return;
    }

    // Primeira amostra após inicialização ou perda de tracking.
    // Não calcular velocidade nesse frame para evitar um pico falso.
    if (!kickMotionInitialized)
    {
        previousKickPosition = currentSample;
        smoothedKickVelocity = Vector3.zero;
        kickDirectionDisplacement = Vector3.zero;

        kickMotionInitialized = true;
        kickGestureState = "TrackingReady";
        return;
    }

    float dt = Mathf.Max(Time.deltaTime, 0.0001f);

    Vector3 delta = currentSample - previousKickPosition;
    previousKickPosition = currentSample;

    Vector3 rawVelocity = delta / dt;

    // Ignora saltos bruscos do tracking/recentragem.
    // Um salto desses não pode gerar um chute.
    if (rawVelocity.magnitude > 8f)
    {
        smoothedKickVelocity = Vector3.zero;
        kickDirectionDisplacement = Vector3.zero;
        kickBackSwingDetected = false;

        kickGestureState = "TrackingJump";
        return;
    }

    // Suavização da velocidade usada apenas para reconhecer
    // a intensidade do gesto.
    float smoothing = 1f - Mathf.Exp(-20f * dt);

    smoothedKickVelocity = Vector3.Lerp(
        smoothedKickVelocity,
        rawVelocity,
        smoothing);

    kickSpeed = smoothedKickVelocity.magnitude;

    GameObject ball = GetCurrentBall();

    if (ball == null ||
        !ball.activeSelf ||
        shotInProgress ||
        turnState != TurnState.PlayerTurn)
    {
        kickGestureState = "Idle";
        kickBackSwingDetected = false;
        kickDirectionDisplacement = Vector3.zero;
        return;
    }

    // Posição do controle direito convertida para mundo.
    Vector3 controllerWorldPosition = GetMotionWorldPosition();

    Vector3 toBall =
        ball.transform.position - controllerWorldPosition;

    float distanceToBall = toBall.magnitude;

    if (distanceToBall > 0.001f)
        toBall /= distanceToBall;

    // Velocidade do controle na direção da bola.
    // Usada para validar o avanço, não para determinar
    // a direção final do chute.
    // As amostras já estão em world space; não aplicar outra conversão.
    Vector3 worldVelocity = smoothedKickVelocity;

    kickForwardSpeed = Vector3.Dot(worldVelocity, toBall);

    // Detecta o recuo, se ele estiver habilitado.
    if (distanceToBall <= kickContactDistance &&
        kickForwardSpeed < -0.35f)
    {
        kickBackSwingDetected = true;

        // O recuo não deve entrar na direção do chute.
        kickDirectionDisplacement = Vector3.zero;

        kickGestureState = "BackSwing";
        return;
    }

    // Acumula somente o deslocamento do controle durante
    // o avanço em direção à bola.
    if (kickForwardSpeed > 0.15f)
    {
        kickDirectionDisplacement += delta;
    }
    else if (kickSpeed < 0.25f)
    {
        // Quando o controle para, limpa o vetor antigo.
        // Isso evita que movimentos anteriores influenciem
        // o próximo chute.
        kickDirectionDisplacement = Vector3.zero;
    }

    bool movingForwardEnough =
        kickSpeed >= minimumKickSpeed &&
        kickForwardSpeed >= minimumForwardKickSpeed;

    bool validGesture = requireBackSwing
        ? kickBackSwingDetected && movingForwardEnough
        : movingForwardEnough;

    if (kickCooldownTimer <= 0f &&
        distanceToBall <= kickContactDistance &&
        validGesture)
    {
        if (TryExecuteMotionKick(ball))
        {
            kickCooldownTimer = kickCooldown;

            kickBackSwingDetected = false;
            kickDirectionDisplacement = Vector3.zero;

            kickGestureState = "KickDetected";
        }
    }
}

	private bool TryExecuteMotionKick(GameObject ball)
{
    if (ball == null || shotInProgress)
        return false;

    if (!PLAY_ALONE && !DEBUG_MODE &&
        (FirebaseManager.Instance == null ||
         !FirebaseManager.Instance.IsMyTurn()))
    {
        return false;
    }

    Rigidbody ballRb = ball.GetComponent<Rigidbody>();

    if (ballRb == null)
        return false;

		UpdateAimState();

		Vector3 targetPosition = aimRayValid
			? aimPointWorld
			: goalCenter != null ? goalCenter.position : ballRb.position + transform.forward;

		Vector3 movementDirection =
			Vector3.ProjectOnPlane(targetPosition - ballRb.position, Vector3.up);

		if (movementDirection.sqrMagnitude < 0.0001f)
			return false;

		movementDirection.Normalize();

    float strength = Mathf.InverseLerp(
        minimumKickSpeed,
        maximumKickSpeed,
        kickSpeed);

    float ballSpeed = Mathf.Lerp(
        minimumBallSpeed,
        maximumBallSpeed,
        strength);

    float forceMultiplier = Mathf.Max(
        0.1f,
        extraForce / Mathf.Max(shotForceMultiplierReference, 0.01f));

    ballSpeed *= forceMultiplier;

    lastShotSpeed = ballSpeed;

    // A direção horizontal vem do movimento real do controle.
    // A componente Y é apenas a elevação configurada.
    Vector3 direction = movementDirection;

    direction.y = upwardForce;
    direction.Normalize();

    LaunchBall(ballRb, direction, ballSpeed);

    Debug.Log(
        $"[QuestFootKick] Chute reconhecido. " +
        $"Velocidade controle={kickSpeed:F2} m/s, " +
        $"velocidade bola={ballSpeed:F2} m/s, " +
			$"lado={aimSide}, offset={aimHorizontalOffset:F2}, " +
        $"direção final={direction}");

    return true;
}

	private void ApplyGoalCurve()
	{
		if (goalCenter == null || curvedBall == null)
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

		float angle = Vector3.SignedAngle(
			currentDirection,
			goalDirection,
			Vector3.up);

		float correction = Mathf.Clamp(
			angle * curveStrength,
			-maxCurveAcceleration,
			maxCurveAcceleration);

		Vector3 lateralDirection =
			Vector3.Cross(Vector3.up, currentDirection).normalized;

		curvedBall.AddForce(
			lateralDirection * correction,
			ForceMode.Acceleration);
	}

	private void FinishGame()
	{
		if (finished)
			return;

		finished = true;
		turnState = TurnState.Finished;

		CancelInvoke(nameof(CleanupPreviousBall));

		if (!PLAY_ALONE)
		{
			ResetAllBalls();
		}

		bool isPlayer1;

		if (PLAY_ALONE)
		{
			isPlayer1 = myRole == PlayerRole.Player1;
		}
		else
		{
			isPlayer1 =
				FirebaseManager.Instance != null &&
				FirebaseManager.Instance.PlayerId == "Jogador1";
		}

		int myGoals = isPlayer1 ? player1Goals : player2Goals;
		int opponentGoals = isPlayer1 ? player2Goals : player1Goals;

		string result = myGoals > opponentGoals
			? "VITÓRIA!"
			: myGoals < opponentGoals
				? "DERROTA!"
				: "EMPATE!";

		Debug.Log(
			"<color=yellow>[FootballVR] PARTIDA FINALIZADA!</color>\n" +
			"Jogador 1: " + player1Goals +
			" | Jogador 2: " + player2Goals +
			"\n" + result);

		onFinished?.Invoke();
	}
}
