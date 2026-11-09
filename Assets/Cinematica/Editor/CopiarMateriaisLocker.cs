using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copia os materiais dos renderers do "Locker" para os renderers correspondentes
// do Locker_bigdoor_v2 ou Locker_bigdoor_v3.
// Correspondencia: 1) mesmo caminho na hierarquia, 2) mesmo nome de objeto, 3) mesma mesh (nome).
public static class CopiarMateriaisLocker
{
    const string Origem = "Locker";

    [MenuItem("Tools/Materiais/Copiar Materiais Locker -> Locker_bigdoor_v2")]
    static void CopiarV2()
    {
        CopiarPara("Locker_bigdoor_v2");
    }

    [MenuItem("Tools/Materiais/Copiar Materiais Locker -> Locker_bigdoor_v3")]
    static void CopiarV3()
    {
        CopiarPara("Locker_bigdoor_v3");
    }

    static void CopiarPara(string destino)
    {
        var src = GameObject.Find(Origem);
        var dst = GameObject.Find(destino);
        if (src == null || dst == null)
        {
            EditorUtility.DisplayDialog(
                "Copiar Materiais",
                "Nao achei '" + Origem + "' ou '" + destino + "' ativos na cena aberta.",
                "OK");
            return;
        }

        var porCaminho = new Dictionary<string, Renderer>();
        var porNome = new Dictionary<string, Renderer>();
        var porMesh = new Dictionary<string, Renderer>();
        foreach (var r in src.GetComponentsInChildren<Renderer>(true))
        {
            string c = Caminho(r.transform, src.transform);
            porCaminho[c] = r;
            if (!porNome.ContainsKey(r.name)) porNome[r.name] = r;
            string m = NomeMesh(r);
            if (m != null && !porMesh.ContainsKey(m)) porMesh[m] = r;
        }

        int ok = 0;
        var log = new StringBuilder();
        var semPar = new List<string>();
        foreach (var r in dst.GetComponentsInChildren<Renderer>(true))
        {
            string c = Caminho(r.transform, dst.transform);
            Renderer par;
            if (!porCaminho.TryGetValue(c, out par) && !porNome.TryGetValue(r.name, out par))
            {
                string m = NomeMesh(r);
                if (m == null || !porMesh.TryGetValue(m, out par)) par = null;
            }
            if (par == null) { semPar.Add(c); continue; }

            Undo.RecordObject(r, "Copiar Materiais Locker");
            r.sharedMaterials = par.sharedMaterials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            ok++;
            log.AppendLine(c + "  <-  " + Caminho(par.transform, src.transform));
        }

        EditorSceneManager.MarkSceneDirty(dst.scene);
        Debug.Log("[CopiarMateriaisLocker] " + destino + ": " + ok + " renderers copiados:\n" + log);
        if (semPar.Count > 0)
            Debug.LogWarning("[CopiarMateriaisLocker] " + destino + " sem correspondente (" + semPar.Count + "):\n" + string.Join("\n", semPar));
        EditorUtility.DisplayDialog(
            "Copiar Materiais",
            destino + ": " + ok + " renderers copiados. Sem correspondente: " + semPar.Count + ".\nVeja o Console e salve a cena (Ctrl+S).",
            "OK");
    }

    static string NomeMesh(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) return mf.sharedMesh.name;
        var smr = r as SkinnedMeshRenderer;
        if (smr != null && smr.sharedMesh != null) return smr.sharedMesh.name;
        return null;
    }

    static string Caminho(Transform t, Transform raiz)
    {
        if (t == raiz) return ".";
        string p = t.name;
        while (t.parent != null && t.parent != raiz) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
