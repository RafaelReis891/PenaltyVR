
using UnityEngine;

public class GoalKeeperActions : MonoBehaviour
{
	public Transform centerGoalKeeper;
	public Animator goalKeeperAnim, goalKeeperCenterAnim;

	public float timeResetGoalKeeper = 4f;

	public Vector3 goalKeeperSizeStart = new Vector3(1, 1, 1);
	public Vector3 goalKeeperPostionStart;
	public Vector3 goalKeeperRotationStart;

	[Header("Triggers")]
	public Transform triggersROOT;
	public float triggersMin = 2.0f;
	public float triggersMax = 2.6f;

	public float yRotationCenter = 20;

	private bool resetScheduled;

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.RightArrow)) CatchRight();
		if (Input.GetKeyDown(KeyCode.LeftArrow)) CatchLeft();
	}

	public void CatchRight()
	{
		PrepareCatch();

		transform.rotation = Quaternion.Euler(0, 160, 0);

		if (centerGoalKeeper != null)
			centerGoalKeeper.localScale = goalKeeperSizeStart;

		if (goalKeeperAnim != null)
			goalKeeperAnim.SetTrigger("Catch");

		if (goalKeeperCenterAnim != null)
			goalKeeperCenterAnim.SetTrigger("Go");

		RandomizeTriggerPosition();
		ScheduleReset();
	}

	public void CatchCenter()
	{
		PrepareCatch();

		transform.rotation = Quaternion.Euler(0, 180, 0);

		if (centerGoalKeeper != null)
			centerGoalKeeper.localScale = goalKeeperSizeStart;

		if (goalKeeperAnim != null)
			goalKeeperAnim.SetTrigger("CatchCenter");

		ScheduleReset();
	}

	public void CatchLeft()
	{
		PrepareCatch();

		transform.rotation = Quaternion.Euler(0, 200, 0);

		if (centerGoalKeeper != null)
		{
			centerGoalKeeper.localScale = new Vector3(
				-goalKeeperSizeStart.x,
				goalKeeperSizeStart.y,
				goalKeeperSizeStart.z
			);
		}

		if (goalKeeperAnim != null)
			goalKeeperAnim.SetTrigger("Catch");

		if (goalKeeperCenterAnim != null)
			goalKeeperCenterAnim.SetTrigger("Go");

		RandomizeTriggerPosition();
		ScheduleReset();
	}

	private void PrepareCatch()
	{
		CancelInvoke(nameof(ResetGoalKeeperPosRot));
		resetScheduled = false;
	}

	private void ScheduleReset()
	{
		CancelInvoke(nameof(ResetGoalKeeperPosRot));

		resetScheduled = true;
		Invoke(nameof(ResetGoalKeeperPosRot), timeResetGoalKeeper);
	}

	private void RandomizeTriggerPosition()
	{
		if (triggersROOT == null) return;

		triggersROOT.position = new Vector3(
			0,
			0,
			Random.Range(triggersMin, triggersMax)
		);
	}

	private void ResetGoalKeeperPosRot()
	{
		resetScheduled = false;

		if (goalKeeperAnim != null)
			goalKeeperAnim.SetTrigger("Reset");

		if (centerGoalKeeper != null)
		{
			centerGoalKeeper.localPosition = goalKeeperPostionStart;
			centerGoalKeeper.localRotation = Quaternion.Euler(goalKeeperRotationStart);

			// IMPORTANTE: restaura a escala original.
			centerGoalKeeper.localScale = goalKeeperSizeStart;
		}

		transform.rotation = Quaternion.Euler(0, 180, 0);

		if (triggersROOT != null)
		{
			triggersROOT.position = new Vector3(0, 0, triggersMin);
		}
	}

	public void ForceReset()
	{
		CancelInvoke(nameof(ResetGoalKeeperPosRot));
		resetScheduled = false;

		ResetGoalKeeperPosRot();
	}

	private void OnDisable()
	{
		CancelInvoke(nameof(ResetGoalKeeperPosRot));
		resetScheduled = false;
	}
}
