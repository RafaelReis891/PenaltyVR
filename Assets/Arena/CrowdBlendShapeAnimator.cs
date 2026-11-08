
using UnityEngine;

[DisallowMultipleComponent]
public class CrowdBlendShapeAnimator : MonoBehaviour
{
	[Header("Renderer")]
	[Tooltip("Se vazio, procura um SkinnedMeshRenderer nos filhos.")]
	public SkinnedMeshRenderer targetRenderer;

	[Header("BlendShapes")]
	[Tooltip("Deixe vazio para animar todos os BlendShapes.")]
	public string[] blendShapeNames;

	[Header("Valores")]
	[Range(0f, 100f)]
	public float minWeight = 0f;

	[Range(0f, 100f)]
	public float maxWeight = 100f;

	[Header("Velocidade")]
	[Min(0.01f)]
	[Tooltip("Velocidade geral das mudanças.")]
	public float waveSpeed = 1f;

	[Min(0.01f)]
	[Tooltip("Tempo mínimo entre novos alvos aleatórios.")]
	public float minChangeInterval = 1f;

	[Min(0.01f)]
	[Tooltip("Tempo máximo entre novos alvos aleatórios.")]
	public float maxChangeInterval = 4f;

	[Min(0.01f)]
	[Tooltip("Velocidade de aproximação dos valores.")]
	public float smoothness = 2f;

	[Header("Aleatoriedade")]
	[Range(0f, 1f)]
	[Tooltip("Chance de um BlendShape receber um novo alvo diferente de zero.")]
	public float activationChance = 0.65f;

	[Range(0f, 1f)]
	[Tooltip("Variação aleatória da velocidade por BlendShape.")]
	public float speedVariation = 0.5f;

	[Tooltip("Seed fixa para reproduzir o mesmo padrão. Zero usa uma seed aleatória.")]
	public int randomSeed;

	private float[] currentWeights;
	private float[] targetWeights;
	private float[] changeTimers;
	private float[] individualSpeeds;

	private System.Random random;

	private void Awake()
	{
		Initialize();
	}

	private void Initialize()
	{
		if (targetRenderer == null)
			targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>();

		if (targetRenderer == null || targetRenderer.sharedMesh == null)
		{
			Debug.LogWarning(
				"[CrowdBlendShapeAnimator] SkinnedMeshRenderer não encontrado.",
				this
			);

			enabled = false;
			return;
		}

		int count = targetRenderer.sharedMesh.blendShapeCount;

		currentWeights = new float[count];
		targetWeights = new float[count];
		changeTimers = new float[count];
		individualSpeeds = new float[count];

		int seed = randomSeed == 0
			? System.Environment.TickCount ^ GetEntityId().ToString().GetHashCode()
			: randomSeed;

		random = new System.Random(seed);

		for (int i = 0; i < count; i++)
		{
			currentWeights[i] = targetRenderer.GetBlendShapeWeight(i);
			targetWeights[i] = currentWeights[i];

			individualSpeeds[i] = RandomRange(
				Mathf.Max(0.01f, 1f - speedVariation),
				1f + speedVariation
			);

			changeTimers[i] = RandomRange(
				minChangeInterval,
				maxChangeInterval
			);
		}
	}

	private void Update()
	{
		if (targetRenderer == null || currentWeights == null)
			return;

		float dt = Time.deltaTime;

		int count = currentWeights.Length;

		for (int i = 0; i < count; i++)
		{
			if (!ShouldAnimate(i))
				continue;

			changeTimers[i] -= dt;

			if (changeTimers[i] <= 0f)
			{
				ChooseNewTarget(i);

				changeTimers[i] = RandomRange(
					Mathf.Min(minChangeInterval, maxChangeInterval),
					Mathf.Max(minChangeInterval, maxChangeInterval)
				);
			}

			// Aproxima suavemente o valor atual do alvo.
			float t = 1f - Mathf.Exp(
				-smoothness * waveSpeed * individualSpeeds[i] * dt
			);

			currentWeights[i] = Mathf.Lerp(
				currentWeights[i],
				targetWeights[i],
				t
			);

			targetRenderer.SetBlendShapeWeight(
				i,
				Mathf.Clamp(currentWeights[i], minWeight, maxWeight)
			);
		}
	}

	private bool ShouldAnimate(int index)
	{
		if (blendShapeNames == null || blendShapeNames.Length == 0)
			return true;

		string shapeName = targetRenderer.sharedMesh.GetBlendShapeName(index);

		foreach (string name in blendShapeNames)
		{
			if (shapeName == name)
				return true;
		}

		return false;
	}

	private void ChooseNewTarget(int index)
	{
		if (RandomValue() <= activationChance)
		{
			targetWeights[index] = RandomRange(minWeight, maxWeight);
		}
		else
		{
			targetWeights[index] = minWeight;
		}

		individualSpeeds[index] = RandomRange(
			Mathf.Max(0.01f, 1f - speedVariation),
			1f + speedVariation
		);
	}

	private float RandomValue()
	{
		return (float)random.NextDouble();
	}

	private float RandomRange(float min, float max)
	{
		return Mathf.Lerp(min, max, RandomValue());
	}
}
