using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AiKodex.StadiumCreator2.Editor
{
    /// <summary>Finds the package's own assets (works wherever the "Stadium Creator 2" folder is moved to).</summary>
    public static class StadiumCreatorAssets
    {
        public static string Root
        {
            get
            {
                var guid = AssetDatabase.FindAssets("t:CrowdCharacterLibrary").FirstOrDefault();
                if (guid == null) return "Assets/Stadium Creator 2";
                return Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid))).Replace('\\', '/');
            }
        }

        static T Load<T>(string relative) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + "/" + relative);

        public static CrowdCharacterLibrary Library => Load<CrowdCharacterLibrary>("Data/CrowdCharacterLibrary.asset");
        public static CrowdAnimationDatabase Animations => Load<CrowdAnimationDatabase>("Data/CrowdAnimationDatabase.asset");
        public static ComputeShader Simulation => Load<ComputeShader>("Shaders/CrowdSim.compute");
        public static Material CrowdMaterial => Load<Material>("Materials/M_Crowd.mat");
        public static string DemoScene => Root + "/Demo/Demo.unity";

        public static void AssignDefaults(CrowdManager m)
        {
            if (!m.library) m.library = Library;
            if (!m.animations) m.animations = Animations;
            if (!m.simulationShader) m.simulationShader = Simulation;
            if (!m.characterMaterial) m.characterMaterial = CrowdMaterial;
        }

        public static void AssignDefaults(Grandstand g)
        {
            if (!g.concreteMaterial) g.concreteMaterial = Load<Material>("Materials/M_Concrete.mat");
            if (!g.woodMaterial) g.woodMaterial = Load<Material>("Materials/M_Wood.mat");
            if (!g.steelMaterial) g.steelMaterial = Load<Material>("Materials/M_SteelPainted.mat");
            if (!g.aluminiumMaterial) g.aluminiumMaterial = Load<Material>("Materials/M_Aluminium.mat");
            if (!g.seatMaterial) g.seatMaterial = Load<Material>("Materials/M_SeatPlastic.mat");
        }
    }

    public static class StadiumCreatorMenu
    {
        [MenuItem("Tools/Stadium Creator 2/Create Stadium", priority = 1)]
        [MenuItem("GameObject/Stadium Creator 2/Stadium", priority = 10)]
        public static void CreateStadium()
        {
            var go = new GameObject("Stadium");
            Undo.RegisterCreatedObjectUndo(go, "Create Stadium");
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                // place on the ground in front of the scene camera
                var ray = new Ray(view.camera.transform.position, view.camera.transform.forward);
                var ground = new Plane(Vector3.up, Vector3.zero);
                go.transform.position = ground.Raycast(ray, out float d) && d < 200f ? ray.GetPoint(d) : view.pivot;
                go.transform.position = new Vector3(go.transform.position.x, 0f, go.transform.position.z);
            }
            var crowd = go.AddComponent<CrowdManager>();
            StadiumCreatorAssets.AssignDefaults(crowd);
            AddGrandstand(crowd, null);
            Selection.activeGameObject = go;
            if (view != null) view.Frame(new Bounds(go.transform.position + new Vector3(0, 2, 2), new Vector3(8, 4, 6)), false);
        }

        [MenuItem("Tools/Stadium Creator 2/Open Demo Scene", priority = 20)]
        public static void OpenDemo()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(StadiumCreatorAssets.DemoScene);
        }

        [MenuItem("Tools/Stadium Creator 2/Documentation", priority = 40)]
        public static void Docs()
        {
            // open the PDFs with the system viewer
            foreach (var name in new[] { "Getting Started.pdf", "Manual.pdf" })
            {
                var path = System.IO.Path.GetFullPath(StadiumCreatorAssets.Root + "/Documentation/Stadium Creator 2 - " + name);
                if (System.IO.File.Exists(path)) { Application.OpenURL("file:///" + path.Replace('\\', '/')); return; }
            }
            Debug.LogWarning("Stadium Creator 2: documentation folder not found.");
        }

        /// <summary>Adds a grandstand to the stadium, continuing from the last one (or a default 5 × 10 stand).</summary>
        public static Grandstand AddGrandstand(CrowdManager crowd, Grandstand after)
        {
            if (after == null) after = crowd.GetComponentsInChildren<Grandstand>().LastOrDefault();
            var go = new GameObject("Grandstand " + (crowd.GetComponentsInChildren<Grandstand>().Length + 1));
            Undo.RegisterCreatedObjectUndo(go, "Add Grandstand");
            go.transform.SetParent(crowd.transform, false);
            var g = go.AddComponent<Grandstand>();
            if (after != null)
            {
                g.CopySettings(after);
                g.seed = after.seed + 1;
                g.PlaceAfter(after);
            }
            else { g.rows = 5; g.columns = 10; }
            StadiumCreatorAssets.AssignDefaults(g);
            g.Rebuild();
            Selection.activeGameObject = go;
            return g;
        }
    }

    [CustomEditor(typeof(Grandstand)), CanEditMultipleObjects]
    public class GrandstandEditor : UnityEditor.Editor
    {
        static bool s_Materials;
        SerializedProperty P(string n) => serializedObject.FindProperty(n);

        void OnEnable()
        {
            foreach (var t in targets) StadiumCreatorAssets.AssignDefaults((Grandstand)t);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var g = (Grandstand)target;

            EditorGUILayout.LabelField("Size", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(P("rows"));
            EditorGUILayout.PropertyField(P("columns"), new GUIContent("Columns (seats per row)"));
            EditorGUILayout.PropertyField(P("curve"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Construction", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(P("style"));
            if (g.style == GrandstandStyle.Classic) EditorGUILayout.PropertyField(P("deck"));
            EditorGUILayout.PropertyField(P("railings"));
            EditorGUILayout.PropertyField(P("aisleStairs"));
            if (g.style == GrandstandStyle.Classic) EditorGUILayout.PropertyField(P("seatColor"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Spacing", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(P("seatSpacing"));
            EditorGUILayout.PropertyField(P("autoRowSize"), new GUIContent("Auto Row Size", "Recommended row depth and rise for the style"));
            using (new EditorGUI.DisabledScope(g.autoRowSize))
            {
                EditorGUILayout.PropertyField(P("rowDepth"));
                using (new EditorGUI.DisabledScope(g.style == GrandstandStyle.ConcreteSlabs))
                    EditorGUILayout.PropertyField(P("rowRise"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Crowd", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(P("density"));
            EditorGUILayout.PropertyField(P("team"), new GUIContent("Team (-1 auto, 0 home, 1 away)"));
            EditorGUILayout.PropertyField(P("standingBias"));

            s_Materials = EditorGUILayout.Foldout(s_Materials, "Materials", true);
            if (s_Materials)
            {
                EditorGUI.indentLevel++;
                foreach (var n in new[] { "concreteMaterial", "woodMaterial", "steelMaterial", "aluminiumMaterial", "seatMaterial", "seed" })
                    EditorGUILayout.PropertyField(P(n));
                EditorGUI.indentLevel--;
            }
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed) foreach (var t in targets) ((Grandstand)t).Rebuild();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"{g.SeatCount:N0} seats · {g.Length:0.0} m wide · {g.TotalDepth:0.0} m deep", MessageType.None);
            var crowd = g.GetComponentInParent<CrowdManager>();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (crowd && GUILayout.Button("Add Grandstand Beside", GUILayout.Height(26))) StadiumCreatorMenu.AddGrandstand(crowd, g);
                if (crowd && GUILayout.Button("Select Stadium", GUILayout.Height(26))) Selection.activeGameObject = crowd.gameObject;
            }
            if (!crowd) EditorGUILayout.HelpBox("Place this grandstand under a Stadium (CrowdManager) to populate it.", MessageType.Info);
        }
    }
}
