
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ConfigurableJoint))]
public class FixedJointHeight : MonoBehaviour
{
	[Header("CONEXÃO")]
	public Rigidbody connectedBody;

	[Header("RESTRIÇÃO DE ALTURA")]
	public bool clampHeight = true;

	[Tooltip("Mantém a altura inicial da conexão.")]
	public bool fixedHeight = false;

	[Tooltip("Altura mínima relativa à posição inicial.")]
	public float minHeight = -0.5f;

	[Tooltip("Altura máxima relativa à posição inicial.")]
	public float maxHeight = 0.5f;

	[Header("CONFIGURAÇÃO")]
	public bool autoConfigure = true;

	private Rigidbody rb;
	private ConfigurableJoint joint;

	private float initialHeight;

	private void Awake()
	{
		rb = GetComponent<Rigidbody>();
		joint = GetComponent<ConfigurableJoint>();

		if (autoConfigure)
			ConfigureJoint();
	}

	private void ConfigureJoint()
	{
		joint.connectedBody = connectedBody;

		// Comportamento de FixedJoint nos eixos X e Z
		joint.xMotion = ConfigurableJointMotion.Locked;
		joint.yMotion = ConfigurableJointMotion.Free;
		joint.zMotion = ConfigurableJointMotion.Locked;

		// Bloqueia todas as rotações
		joint.angularXMotion = ConfigurableJointMotion.Locked;
		joint.angularYMotion = ConfigurableJointMotion.Locked;
		joint.angularZMotion = ConfigurableJointMotion.Locked;

		// Referência de altura
		initialHeight = connectedBody != null
			? connectedBody.transform.InverseTransformPoint(rb.position).y
			: rb.position.y;

		UpdateHeightConstraint();
	}

	private void UpdateHeightConstraint()
	{
		if (fixedHeight)
		{
			joint.yMotion = ConfigurableJointMotion.Locked;
		}
		else if (clampHeight)
		{
			joint.yMotion = ConfigurableJointMotion.Limited;

			SoftJointLimit lowLimit = joint.linearLimit;
			lowLimit.limit = Mathf.Max(0f, (maxHeight - minHeight) * 0.5f);

			joint.linearLimit = lowLimit;

			// Centraliza o limite no intervalo desejado
			Vector3 anchor = joint.anchor;
			Vector3 connectedAnchor = joint.connectedAnchor;

			float center = (minHeight + maxHeight) * 0.5f;

			if (connectedBody != null)
			{
				connectedAnchor.y = initialHeight + center;
				joint.connectedAnchor = connectedAnchor;
			}
			else
			{
				anchor.y = initialHeight + center;
				joint.anchor = anchor;
			}
		}
		else
		{
			joint.yMotion = ConfigurableJointMotion.Free;
		}
	}

	public void SetConnectedBody(Rigidbody body)
	{
		connectedBody = body;
		joint.connectedBody = body;

		ConfigureJoint();
	}

	public void SetHeightRange(float min, float max)
	{
		minHeight = min;
		maxHeight = max;

		UpdateHeightConstraint();
	}
}
