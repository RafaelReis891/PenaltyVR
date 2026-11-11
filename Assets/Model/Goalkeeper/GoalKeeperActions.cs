using UnityEngine;

public class GoalKeeperActions : MonoBehaviour
{

    public Transform centerGoalKeeper;
    public Animator goalKeeperAnim, goalKeeperCenterAnim;
    public float timeResetGoalKeeper = 4f;
	public Vector3 goalKeeperSizeStart = new Vector3(1,1,1);
	public Vector3 goalKeeperPostionStart;
	public Vector3 goalKeeperRotationStart;

	[Header("Triggers")]
	public Transform triggersROOT;
	public float triggersMin = 2.0f;
	public float triggersMax = 2.6f;

	public float yRotationCenter = 20;

	// Update is called once per frame
	void Update()
    {
		if (Input.GetKeyDown(KeyCode.RightArrow)) CatchRight();
		if (Input.GetKeyDown(KeyCode.LeftArrow)) CatchLeft();
	}

    public void CatchRight()
    {
		transform.rotation = Quaternion.Euler(0,160,0);
		centerGoalKeeper.transform.localScale = goalKeeperSizeStart;
		goalKeeperAnim.SetTrigger("Catch");
		goalKeeperCenterAnim.SetTrigger("Go");
		Invoke(nameof(ResetGoalKeeperPosRot), timeResetGoalKeeper);
		triggersROOT.transform.position = new Vector3(0,0, Random.Range(triggersMin, triggersMax));
	}

	public void CatchCenter()
	{
		transform.rotation = Quaternion.Euler(0, 180, 0);
		centerGoalKeeper.transform.localScale = goalKeeperSizeStart;
		goalKeeperAnim.SetTrigger("CatchCenter");
		Invoke(nameof(ResetGoalKeeperPosRot), timeResetGoalKeeper);
	}

	public void CatchLeft()
	{
		transform.rotation = Quaternion.Euler(0, 200, 0);
		centerGoalKeeper.transform.localScale = new Vector3(-goalKeeperSizeStart.x, goalKeeperSizeStart.y, goalKeeperSizeStart.z);
		goalKeeperAnim.SetTrigger("Catch");
		goalKeeperCenterAnim.SetTrigger("Go");
		Invoke(nameof(ResetGoalKeeperPosRot), timeResetGoalKeeper);
		triggersROOT.transform.position = new Vector3(0, 0, Random.Range(triggersMin, triggersMax));
	}

	void ResetGoalKeeperPosRot()
    {
		goalKeeperAnim.SetTrigger("Reset");
		centerGoalKeeper.transform.localPosition = goalKeeperPostionStart;
		centerGoalKeeper.transform.localRotation = Quaternion.Euler(goalKeeperRotationStart);
		transform.rotation = Quaternion.Euler(0, 180, 0);

		triggersROOT.transform.position = new Vector3(0, 0, triggersMin);
	}

	public void ForceReset()
	{
		CancelInvoke(nameof(ResetGoalKeeperPosRot));
		ResetGoalKeeperPosRot();
	}
}
