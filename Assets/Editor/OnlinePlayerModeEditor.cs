
using UnityEditor;
using UnityEngine;

public static class OnlinePlayerModeEditor
{
	private const string MENU_ROOT = "Online Player Mode/";

	[MenuItem(MENU_ROOT + "Player 1", false, 0)]
	private static void SetPlayer1()
	{
		SetPlayerMode(QuestFootKick.PlayerRole.Player1, "Jogador 1");
	}

	[MenuItem(MENU_ROOT + "Player 2", false, 1)]
	private static void SetPlayer2()
	{
		SetPlayerMode(QuestFootKick.PlayerRole.Player2, "Jogador 2");
	}

	[MenuItem(MENU_ROOT + "Player 1", true)]
	private static bool ValidatePlayer1()
	{
		return HasSelectedQuestFootKick();
	}

	[MenuItem(MENU_ROOT + "Player 2", true)]
	private static bool ValidatePlayer2()
	{
		return HasSelectedQuestFootKick();
	}

	private static bool HasSelectedQuestFootKick()
	{
		return Selection.activeGameObject != null &&
			   Selection.activeGameObject.GetComponent<QuestFootKick>() != null;
	}

	private static void SetPlayerMode(
		QuestFootKick.PlayerRole role,
		string displayName)
	{
		GameObject selectedObject = Selection.activeGameObject;

		if (selectedObject == null)
		{
			Debug.LogWarning("[Online Player Mode] Selecione um GameObject.");
			return;
		}

		QuestFootKick questFootKick =
			selectedObject.GetComponent<QuestFootKick>();

		if (questFootKick == null)
		{
			Debug.LogWarning(
				"[Online Player Mode] O objeto selecionado não possui QuestFootKick."
			);
			return;
		}

		Undo.RecordObject(questFootKick, "Change Online Player Mode");

		questFootKick.myRole = role;
		questFootKick.playerDisplayName = displayName;

		EditorUtility.SetDirty(questFootKick);

		if (PrefabUtility.IsPartOfPrefabInstance(questFootKick))
		{
			PrefabUtility.RecordPrefabInstancePropertyModifications(
				questFootKick
			);
		}

		Debug.Log(
			$"[Online Player Mode] {selectedObject.name} configurado como {displayName}."
		);

		EditorUtility.SetDirty(selectedObject);
	}
}
