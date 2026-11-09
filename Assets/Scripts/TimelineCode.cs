using UnityEngine;

public class TimelineCode : MonoBehaviour
{
	public Transform xrCameraTimeline;
	public Transform xrCameraPlayer;

	private void Start()
	{
		xrCameraTimeline.gameObject.SetActive(true);
		xrCameraPlayer.gameObject.SetActive(false);
	}

	public void ActivatePlayer()
	{
		xrCameraTimeline.gameObject.SetActive(false);
		xrCameraPlayer.gameObject.SetActive(true);
	}
}
