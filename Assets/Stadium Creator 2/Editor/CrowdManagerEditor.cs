using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AiKodex.StadiumCreator2.Editor
{
    [CustomEditor(typeof(CrowdManager))]
    public class CrowdManagerEditor : UnityEditor.Editor
    {
        static bool s_Advanced;

        void OnEnable()
        {
            var m = (CrowdManager)target;
            if (!m.AssetsReady) { StadiumCreatorAssets.AssignDefaults(m); EditorUtility.SetDirty(m); }
        }

        public override void OnInspectorGUI()
        {
            var m = (CrowdManager)target;
            serializedObject.Update();
            if (!m.AssetsReady)
                EditorGUILayout.HelpBox("Character data not found. Re-import the Stadium Creator 2 package.", MessageType.Warning);

            // ---- grandstands
            var stands = m.GetComponentsInChildren<Grandstand>();
            EditorGUILayout.LabelField("Stadium", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"{stands.Length} grandstand(s) · {m.SpectatorCount:N0} spectators", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Grandstand", GUILayout.Height(28))) StadiumCreatorMenu.AddGrandstand(m, stands.LastOrDefault());
                using (new EditorGUI.DisabledScope(stands.Length == 0))
                    if (GUILayout.Button("Remove Last", GUILayout.Height(28)))
                    {
                        Undo.DestroyObjectImmediate(stands.Last().gameObject);
                        m.RequestRegenerate();
                        GUIUtility.ExitGUI();
                    }
            }
            foreach (var s in stands)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{s.name}", $"{s.style} · {s.rows} × {s.columns}" + (Mathf.Abs(s.curve) > 0.001f ? $" · curve {s.curve:0.00}" : ""));
                    if (GUILayout.Button("Edit", GUILayout.Width(50))) Selection.activeGameObject = s.gameObject;
                }

            // ---- crowd
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Crowd", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("density"), new GUIContent("Density (all stands)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("homeRatio"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("homeTeam"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("awayTeam"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"));
            if (serializedObject.ApplyModifiedProperties()) m.RequestRegenerate();

            // ---- mood
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mood", EditorStyles.boldLabel);
            var names = Enum.GetNames(typeof(CrowdEmotion));
            const int perRow = 4;
            for (int i = 0; i < names.Length; i += perRow)
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + perRow, names.Length); j++)
                    {
                        var e = (CrowdEmotion)j;
                        GUI.backgroundColor = m.emotion == e ? new Color(0.6f, 0.9f, 1f) : Color.white;
                        if (GUILayout.Button(names[j], EditorStyles.miniButton)) { Undo.RecordObject(m, "Crowd mood"); m.ApplyEmotion(e, m.intensity); }
                    }
                    GUI.backgroundColor = Color.white;
                }
            float inten = EditorGUILayout.Slider("Intensity", m.intensity, 0f, 1f);
            if (!Mathf.Approximately(inten, m.intensity)) { Undo.RecordObject(m, "Crowd intensity"); m.ApplyEmotion(m.emotion, inten); }
            if (GUILayout.Button("Goal! (8 s celebration)"))
                m.TriggerReaction(CrowdEmotion.Celebrating, 1f, 8f, m.transform.position, 45f);

            EditorGUILayout.Space();
            s_Advanced = EditorGUILayout.Foldout(s_Advanced, "Advanced (rendering, behaviour, assets)", true);
            if (s_Advanced)
            {
                EditorGUI.indentLevel++;
                if (GUILayout.Button("Regenerate Crowd")) m.Generate();
                DrawDefaultInspector();
                EditorGUI.indentLevel--;
            }
        }
    }
}
