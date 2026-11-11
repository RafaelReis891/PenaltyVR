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

		if (collision.collider.CompareTag("GoalFrame"))
			source.PlayOneShot(metalClip);
		else
			source.PlayOneShot(defaultClip);
	}

	private void OnTriggerEnter(Collider other)
	{
		// DETECÇÃO DE GOL
		if (other.CompareTag("Goal"))
		{
			if (!goalScored)
			{
				goalScored = true;
				if (questFootKick != null) questFootKick.Goal(gameObject);
				else Debug.LogWarning("[BallSound] QuestFootKick não configurado!");
			}
			return;
		}

		// DEFESA DO GOLEIRO
		if (other.CompareTag("Left"))
		{
			if (!kicked) { goalKeeperActions.CatchLeft(); kicked = true; }
		}
		else if (other.CompareTag("Center"))
		{
			if (!kicked) { goalKeeperActions.CatchCenter(); kicked = true; }
		}
		else if (other.CompareTag("Right"))
		{
			if (!kicked) { goalKeeperActions.CatchRight(); kicked = true; }
		}
	}

	// NOVO MÉTODO: Chama este método quando a partida reiniciar
	public void ResetState()
	{
		kicked = false;
		goalScored = false;
	}
}