
using UnityEngine;

public class BallSound : MonoBehaviour
{
	public AudioSource source;
	public AudioClip defaultClip, metalClip;

	public GoalKeeperActions goalKeeperActions;
	public QuestFootKick questFootKick;

	private bool kicked;
	private bool goalScored;
	private float nextSoundTime;

	private void OnCollisionEnter(Collision collision)
	{
		if (Time.time < nextSoundTime) return;

		nextSoundTime = Time.time + 0.5f;

		if (source == null) return;

		if (collision.collider.CompareTag("GoalFrame"))
		{
			if (metalClip != null)
				source.PlayOneShot(metalClip);
		}
		else
		{
			if (defaultClip != null)
				source.PlayOneShot(defaultClip);
		}
	}

	private void OnTriggerEnter(Collider other)
	{
		// DETECÇÃO DE GOL
		if (other.CompareTag("Goal"))
		{
			if (!goalScored)
			{
				goalScored = true;

				if (questFootKick != null)
					questFootKick.Goal(gameObject);
				else
					Debug.LogWarning("[BallSound] QuestFootKick não configurado!");
			}

			return;
		}

		// DEFESA DO GOLEIRO
		if (kicked) return;

		if (goalKeeperActions == null) return;

		if (other.CompareTag("Left"))
		{
			kicked = true;
			goalKeeperActions.CatchLeft();
		}
		else if (other.CompareTag("Center"))
		{
			kicked = true;
			goalKeeperActions.CatchCenter();
		}
		else if (other.CompareTag("Right"))
		{
			kicked = true;
			goalKeeperActions.CatchRight();
		}
	}

	public void ResetState()
	{
		kicked = false;
		goalScored = false;
		nextSoundTime = 0f;
	}
}
