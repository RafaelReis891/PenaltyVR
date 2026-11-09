using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.Events;
using UnityEditor.Events;

// Monta a entrada da Anfitria na Timeline que ja existe na cena (PlayableDirector "Timeline").
// Menu: Tools > Cinematica > Montar Entrada Anfitria
// - Cria (se nao existir) o objeto "Caminho_Anfitria" com os pontos do trajeto.
// - Coloca a Anfitria dentro de um pai "Anfitria_Cinematica" (o pai anda pelo caminho, a Anfitria so anima o corpo).
// - Cria tracks com prefixo CINE_ na Timeline. Ao rodar de novo, so as tracks CINE_ sao refeitas; as outras nao sao tocadas.
public static class MontarEntradaAnfitria
{
    const string PASTA_ANIM = "Assets/Cinematica/Animations";
    const string PASTA_GERADOS = "Assets/Cinematica/Gerados";
    const string WALK_FBX = PASTA_ANIM + "/Walking.fbx";
    const string IDLE_FBX = PASTA_ANIM + "/Anfitria_Standing_Idle.fbx";
    public const string ESCADA_FBX = PASTA_ANIM + "/Ascending_Stairs.fbx";
    const string CLIP_COSTAS = PASTA_GERADOS + "/Anfitria_AndarDeCostas.anim";
    const string CLIP_ANDAR = PASTA_GERADOS + "/Anfitria_Andar_InPlace.anim";
    const string CLIP_IDLE = PASTA_GERADOS + "/Anfitria_Parada_InPlace.anim";
    const string CLIP_ESCADA = PASTA_GERADOS + "/Anfitria_Escada_InPlace.anim";
    const string CLIP_CAMINHO = PASTA_GERADOS + "/Anfitria_Caminho.anim";
    const string CLIP_CAMERA = PASTA_GERADOS + "/Camera_Atras.anim";
    const string PREFIXO = "CINE_";
    const string NOME_ANFITRIA = "Anfitria";
    const string NOME_START = "AnfitrianStart";
    const string NOME_PAI = "Anfitria_Cinematica";
    const string NOME_CAMINHO = "Caminho_Anfitria";
    const string NOME_VOZ = "Anfitria_Voz";
    const float FPS_CURVA = 15f;

    // ---------------------------------------------------------------- menus

    public static bool Montando { get; private set; }
    static bool s_Silencioso;
    static bool s_BackupNestaSessao;

    [MenuItem("Tools/Cinematica/Montar Entrada Anfitria")]
    public static void MontarMenu() { Montar(false); }

    // silencioso = chamado pelo 'autoAtualizar' (sem janelas, backup so 1x por sessao)
    public static void Montar(bool silencioso = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (!silencioso) EditorUtility.DisplayDialog("Cinematica", "Saia do Play antes de montar.", "OK");
            return;
        }
        if (Montando) return;
        Montando = true;
        s_Silencioso = silencioso;
        try { MontarInterno(); }
        finally { Montando = false; s_Silencioso = false; }
    }

    static void MontarInterno()
    {

        GameObject anf = Achar(NOME_ANFITRIA);
        if (anf == null) { Erro("Nao achei o objeto '" + NOME_ANFITRIA + "' na cena."); return; }

        PlayableDirector director = AcharDirector();
        if (director == null) { Erro("Nao achei um PlayableDirector com Timeline na cena."); return; }
        var timeline = director.playableAsset as TimelineAsset;

        // Caminho (cria o padrao se ainda nao existir)
        GameObject goCaminho = Achar(NOME_CAMINHO);
        CaminhoCinematica caminho = goCaminho != null ? goCaminho.GetComponent<CaminhoCinematica>() : null;
        if (caminho == null)
        {
            caminho = CriarCaminhoPadrao(anf);
            if (caminho == null) return;
        }
        List<Transform> pontos = caminho.Pontos();
        if (pontos.Count < 2) { Erro("O Caminho_Anfitria precisa de pelo menos 2 pontos filhos."); return; }

        // Animacoes
        AnimationClip walk = CarregarClipFbx(WALK_FBX);
        AnimationClip idle = CarregarClipFbx(IDLE_FBX);
        if (walk == null) { Erro("Nao achei o clip em " + WALK_FBX); return; }
        if (idle == null) Debug.LogWarning("[Cinematica] Idle nao encontrado em " + IDLE_FBX + ". Vou usar o Walking parado no fim.");

        // Animacao de escada (opcional). Se nao existir, fica o Walking no trecho da escada (comportamento antigo).
        AnimationClip escada = null;
        if (caminho.usarAnimacaoEscada)
        {
            ImportAnimacoesCinematica.GarantirImportEscada();
            escada = CarregarClipFbx(ESCADA_FBX);
            if (escada == null) Debug.Log("[Cinematica] Ascending_Stairs nao encontrado em " + ESCADA_FBX + ". Escada usa o Walking.");
            else if (caminho.velocidadeEscadaAutomatica && escada.length > 0.05f)
            {
                float passo = Mathf.Sqrt(caminho.profundidadeDegrau * caminho.profundidadeDegrau + caminho.alturaDegrau * caminho.alturaDegrau);
                float v = Mathf.Max(0.1f, caminho.degrausPorCiclo * passo / escada.length);
                Undo.RecordObject(caminho, "Velocidade escada");
                caminho.velocidadeEscada = v;
                EditorUtility.SetDirty(caminho);
                Debug.Log("[Cinematica] Clip escada " + escada.length.ToString("0.00") + " s -> velocidadeEscada = " + v.ToString("0.00") + " m/s");
            }
        }


        if (!s_Silencioso || !s_BackupNestaSessao) { Backup(timeline); s_BackupNestaSessao = true; }

        // Hierarquia: pai que anda + Anfitria que anima o corpo
        GameObject pai = GarantirPai(anf);
        Animator paiAnimator = pai.GetComponent<Animator>();
        Animator anfAnimator = anf.GetComponent<Animator>();
        if (anfAnimator == null) anfAnimator = anf.GetComponentInChildren<Animator>();
        if (anfAnimator == null) anfAnimator = Undo.AddComponent<Animator>(anf);
        if (anfAnimator.avatar == null || !anfAnimator.avatar.isHuman)
            Debug.LogWarning("[Cinematica] O Animator da Anfitria nao tem Avatar Humanoid valido. A animacao de andar pode nao aparecer. Confira o Rig do FBX da Anfitria (Humanoid).");

        if (anfAnimator.applyRootMotion) { Undo.RecordObject(anfAnimator, "Root motion off"); anfAnimator.applyRootMotion = false; }

        // Clips IN PLACE gerados: o deslocamento do corpo (RootT) e removido do ciclo, entao o corpo nao anda
        // na frente do pai nem da "snap" para tras no loop. Quem anda e o pai (Anfitria_Cinematica).
        Vector3 dWalk, dTmp, dEsc;
        AnimationClip walkFbx = walk;
        walk = CriarClipInPlace(walkFbx, CLIP_ANDAR, false, out dWalk);
        AnimationClip costas = CriarClipInPlace(walkFbx, CLIP_COSTAS, true, out dTmp);
        if (idle != null) idle = CriarClipInPlace(idle, CLIP_IDLE, false, out dTmp);
        dEsc = Vector3.zero;
        if (escada != null) escada = CriarClipInPlace(escada, CLIP_ESCADA, false, out dEsc);

        // Cadencia: metros por unidade humanoid ~= altura do quadril na cena / RootT.y medio do clip
        float mPorUnidade = MetrosPorUnidadeHumanoid(anf, anfAnimator, walkFbx);
        float vNatAndar = Mathf.Max(0.1f, caminho.velocidadeNaturalAndar);
        float vNatEscada = Mathf.Max(0.05f, caminho.velocidadeEscada);
        if (caminho.cadenciaAutomatica && mPorUnidade > 0f)
        {
            float vw = new Vector2(dWalk.x, dWalk.z).magnitude * mPorUnidade / Mathf.Max(0.05f, walk.length);
            if (vw > 0.3f && vw < 4f) vNatAndar = vw;
            if (escada != null && caminho.velocidadeEscadaAutomatica)
            {
                float vy = dEsc.y * mPorUnidade / Mathf.Max(0.05f, escada.length); // subida por segundo do clip
                float seno = SenoEscada(pontos, caminho);
                if (vy > 0.05f && seno > 0.05f)
                {
                    float ve = vy / seno;
                    Undo.RecordObject(caminho, "Velocidade escada");
                    caminho.velocidadeEscada = ve;
                    EditorUtility.SetDirty(caminho);
                }
            }
            if (escada != null) vNatEscada = Mathf.Max(0.05f, caminho.velocidadeEscada);
            Debug.Log("[Cinematica] Cadencia: " + mPorUnidade.ToString("0.000") + " m/unid, andar natural " + vNatAndar.ToString("0.00") +
                      " m/s, escada " + caminho.velocidadeEscada.ToString("0.00") + " m/s (subida do clip " + (dEsc.y * mPorUnidade).ToString("0.00") + " m/ciclo)");
        }

        // Calcula o trajeto
        Trajeto tr = CalcularTrajeto(caminho, pontos, 1f);
        float duracaoNatural = tr.duracao;
        float fator = 1f;
        if (caminho.usarDuracaoTotal && caminho.duracaoTotalCaminhada > 0.5f && tr.duracao > 0.01f)
        {
            // tudo escala junto (inclusive os trechos de costas). Itera porque marcadores em segundos mudam com a velocidade.
            float alvo = Mathf.Max(0.5f, caminho.duracaoTotalCaminhada);
            for (int it = 0; it < 4; it++)
            {
                fator *= tr.duracao / alvo;
                tr = CalcularTrajeto(caminho, pontos, fator);
                if (Mathf.Abs(tr.duracao - alvo) < 0.02f) break;
            }
        }
        if (Mathf.Abs(caminho.fatorGizmo - fator) > 0.001f) { caminho.fatorGizmo = fator; EditorUtility.SetDirty(caminho); }
        foreach (var av in tr.avisos) Debug.LogWarning("[Cinematica] " + av);
        float lim = Mathf.Max(1f, caminho.limiteTimeScale);
        Func<float, double> escalaClip = (x) => caminho.ajustarVelocidadeClips ? Mathf.Clamp(x, 1f / lim, lim) : 1.0;
        double tsAndar = escalaClip(fator * caminho.velocidade / vNatAndar);
        double tsCostas = escalaClip(fator * caminho.velocidadeCostas / vNatAndar);
        double tsEscada = escalaClip(fator * caminho.velocidadeEscada / vNatEscada);

        // Gera os clips de Transform (pai da Anfitria e camera)
        AnimationClip clipCaminho = CarregarOuCriarClip(CLIP_CAMINHO);
        PreencherClipCaminho(clipCaminho, caminho, tr);
        AnimationClip clipCamera = null;
        string msgDist = "";

        AnimationTrack trkCamAntiga = null;
        Animator camAnimator = caminho.gerarTrackCamera ? AcharAnimatorCamera(director, timeline, out trkCamAntiga) : null;
        if (caminho.gerarTrackCamera && camAnimator != null)
        {
            Transform paiCam = camAnimator.transform.parent;
            Vector3 camPos0; Vector3 camEul0;
            PoseInicialCamera(trkCamAntiga, camAnimator.transform, out camPos0, out camEul0);
            clipCamera = CarregarOuCriarClip(CLIP_CAMERA);
            msgDist = PreencherClipCamera(clipCamera, caminho, tr, paiCam, camPos0, camEul0);
        }

        // Timeline: remove apenas as tracks CINE_ antigas
        foreach (var t in timeline.GetRootTracks().ToList())
        {
            if (t != null && t.name.StartsWith(PREFIXO))
            {
                foreach (var filho in t.GetChildTracks()) director.ClearGenericBinding(filho);
                director.ClearGenericBinding(t);
                timeline.DeleteTrack(t);
            }
        }

        double tInicio = caminho.tempoInicio;
        double tChegada = tInicio + tr.duracao;
        double tFim = tChegada + Mathf.Max(caminho.duracaoGiroFinal, caminho.tempoParadaFinal);

        // 1) Track que move o pai (posicao + rotacao)
        var trkCaminho = timeline.CreateTrack<AnimationTrack>(null, PREFIXO + "Anfitria_Caminho");
        ConfigurarTrack(trkCaminho);
        AddClip(trkCaminho, clipCaminho, 0, tFim, "Caminho", false, false);
        director.SetGenericBinding(trkCaminho, paiAnimator);

        // 2) Track do corpo: Walking em loop
        var trkCorpo = timeline.CreateTrack<AnimationTrack>(null, PREFIXO + "Anfitria_Corpo");
        ConfigurarTrack(trkCorpo);
        AddClip(trkCorpo, walk, 0, tChegada + 0.6, "Andar (frente)", true, true).timeScale = tsAndar;
        director.SetGenericBinding(trkCorpo, anfAnimator);

        // 3) Overrides (com ease = transicao suave):
        //    CINE_Anfitria_Costas_Escada: trechos de costas + escada (de frente)
        //    CINE_Anfitria_Parada (por cima): idle no inicio e no fim
        var trkOver = timeline.CreateTrack<AnimationTrack>(trkCorpo, PREFIXO + "Anfitria_Costas_Escada");
        double vir = Mathf.Max(0.1f, caminho.duracaoVirada);
        double fimAnterior = 0;
        var pedacos = new List<object[]>();   // ini, fim, clip, nome, ts, easeIn, easeOut
        foreach (var tc in tr.trechos)
        {
            double ini = tc.desdeInicio ? 0.0 : tInicio + tc.tIni - vir * 0.5;
            double fim = tInicio + tc.tFim + vir * 0.5;
            pedacos.Add(new object[] { ini, fim, costas, "De costas (" + tc.nome + ")", tsCostas, tc.desdeInicio ? 0.0 : vir, vir });
        }
        if (escada != null && tr.tEscadaFim > tr.tEscadaIni + 0.05f)
        {
            double bl = Mathf.Max(0.05f, caminho.duracaoBlendEscada);
            pedacos.Add(new object[] { tInicio + tr.tEscadaIni - bl * 0.5, tInicio + tr.tEscadaFim + bl * 0.5, escada, "Subir escada", tsEscada, bl, bl });
        }
        foreach (var pd in pedacos.OrderBy(x => (double)x[0]))
        {
            double ini = Math.Max((double)pd[0], fimAnterior + 0.01);
            double fim = (double)pd[1];
            if (fim < ini + 0.2) continue;
            var c = AddClip(trkOver, (AnimationClip)pd[2], ini, fim - ini, (string)pd[3], true, true);
            c.timeScale = (double)pd[4];
            c.easeInDuration = Math.Min((double)pd[5], (fim - ini) * 0.45);
            c.easeOutDuration = Math.Min((double)pd[6], (fim - ini) * 0.45);
            fimAnterior = fim;
        }

        if (idle != null)
        {
            var trkPar = timeline.CreateTrack<AnimationTrack>(trkCorpo, PREFIXO + "Anfitria_Parada");
            if (tInicio > 0.05)
            {
                double fimIdle = tInicio + 0.5;
                var c = AddClip(trkPar, idle, 0, fimIdle, "Parada (inicio)", true, true);
                c.easeOutDuration = Math.Min(0.5, fimIdle * 0.45);
            }
            double ini = Math.Max(tChegada - 0.5, tInicio + 0.6);
            double dur = Math.Max(tFim - ini, 1.0);
            var cf = AddClip(trkPar, idle, ini, dur, "Parada (final)", true, true);
            cf.easeInDuration = Math.Min(0.7, dur * 0.45);
        }

        // 4) Camera atras da Anfitria (ATIVA). A track antiga da Camera fica MUTADA (nao e apagada; backup do .playable feito acima).
        string msgCam = "";
        if (clipCamera != null)
        {
            var trkCam = timeline.CreateTrack<AnimationTrack>(null, PREFIXO + "Camera_Atras");
            ConfigurarTrack(trkCam);
            AddClip(trkCam, clipCamera, 0, tFim, "Camera atras", false, false);
            director.SetGenericBinding(trkCam, camAnimator);
            trkCam.muted = false;
            if (trkCamAntiga != null)
            {
                trkCamAntiga.muted = true;
                msgCam = "Camera: CINE_Camera_Atras ATIVA. A track antiga '" + trkCamAntiga.name + "' foi MUTADA (nao apagada). Para voltar, desmute ela e mute a CINE_Camera_Atras.\n";
            }
        }

        // 5) Signal ActivatePlayer
        string msgSinal = "";
        foreach (var t in timeline.GetRootTracks())
        {
            foreach (var m in t.GetMarkers().ToList())
            {
                var se = m as SignalEmitter;
                if (se == null || se.asset == null || se.asset.name != "ActivatePlayer") continue;
                if (caminho.moverSinalActivatePlayer)
                {
                    double antes = se.time;
                    se.time = tFim;
                    EditorUtility.SetDirty(se);
                    EditorUtility.SetDirty(t);
                    msgSinal = "Signal ActivatePlayer movido de " + antes.ToString("0.0") + " s para " + tFim.ToString("0.0") + " s.\n";
                }
                else if (se.time < tFim)
                    msgSinal = "ATENCAO: Signal ActivatePlayer em " + se.time.ToString("0.0") + " s corta a cinematica (fim em " + tFim.ToString("0.0") + " s).\n";
            }
        }

        // 6) Torcida: Celebrating ao sair da escada, Interested ao chegar
        string msgTorcida = "";
        if (caminho.controlarTorcida)
            msgTorcida = MontarTorcida(timeline, director, caminho, tr, tInicio, tChegada);

        // 7) Falas (audios de voz) nos marcadores do caminho
        string msgFalas = MontarFalas(timeline, director, caminho, tr, pai, tInicio, tChegada, tFim);

        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(director);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(anf.scene);

        string msg =
            "Pronto! Tracks CINE_ criadas na Timeline '" + timeline.name + "'.\n\n" +
            "Comeca a andar: " + tInicio.ToString("0.0") + " s\n" +
            TextoTrechos(tr, tInicio) +
            "Escada: " + (tInicio + tr.tEscadaIni).ToString("0.0") + " a " + (tInicio + tr.tEscadaFim).ToString("0.0") + " s (" + (escada != null ? "Ascending_Stairs, " + caminho.velocidadeEscada.ToString("0.00") + " m/s" : "Walking") + ")\n" +
            "Chega na marca: " + tChegada.ToString("0.0") + " s (caminho de " + tr.comprimento.ToString("0") + " m)\n" +
            "Duracao: " + tr.duracao.ToString("0.0") + " s (natural " + duracaoNatural.ToString("0.0") + " s, velocidades x" + fator.ToString("0.00") +
            ", media " + (tr.comprimento / Mathf.Max(0.01f, tr.duracao)).ToString("0.0") + " m/s, pico andar " + (caminho.velocidade * fator).ToString("0.0") + " m/s)\n" +
            "TimeScale clips: andar " + tsAndar.ToString("0.00") + ", costas " + tsCostas.ToString("0.00") + ", escada " + tsEscada.ToString("0.00") + "\n\n" +
            msgCam + msgDist + msgSinal + msgTorcida + msgFalas +
            "Confira tambem as Activation Tracks (Locker/Porta ~18-22 s), que eram do 'voo' antigo.\n\n" +
            "Salve a cena (Ctrl+S).";
        Debug.Log("[Cinematica] " + msg.Replace("\n", " | "));
        if (!s_Silencioso)
        {
            EditorUtility.DisplayDialog("Cinematica - Entrada Anfitria", msg, "OK");
            Selection.activeObject = director.gameObject;
        }
    }

    static string TextoTrechos(Trajeto tr, double tInicio)
    {
        if (tr.trechos.Count == 0) return "De costas: nenhum trecho\n";
        string r = "";
        foreach (var tc in tr.trechos)
            r += "De costas (" + tc.nome + "): " + (tc.desdeInicio ? "ja comeca de costas" : "vira aos " + (tInicio + tc.tIni).ToString("0.0") + " s") +
                 ", volta de frente aos " + (tInicio + tc.tFim).ToString("0.0") + " s (" + tc.sIni.ToString("0.0") + " a " + tc.sFim.ToString("0.0") + " m do caminho)\n";
        return r;
    }

    [MenuItem("Tools/Cinematica/Recriar Caminho Padrao (apaga os pontos)")]
    public static void RecriarCaminho()
    {
        GameObject anf = Achar(NOME_ANFITRIA);
        if (anf == null) { Erro("Nao achei a Anfitria."); return; }
        GameObject velho = Achar(NOME_CAMINHO);
        if (velho != null)
        {
            if (!EditorUtility.DisplayDialog("Cinematica", "Apagar o Caminho_Anfitria atual e criar o padrao de novo?", "Sim", "Cancelar")) return;
            Undo.DestroyObjectImmediate(velho);
        }
        CriarCaminhoPadrao(anf);
        EditorSceneManager.MarkSceneDirty(anf.scene);
    }

    // ---------------------------------------------------------------- caminho padrao

    static CaminhoCinematica CriarCaminhoPadrao(GameObject anf)
    {
        GameObject start = Achar(NOME_START);
        if (start == null) { Erro("Nao achei o cubo '" + NOME_START + "' na cena."); return null; }

        // Posicao/rotacao final = onde a Anfitria esta hoje (se ja estiver no pai, usa o pai)
        Transform refFinal = anf.transform;
        if (anf.transform.parent != null && anf.transform.parent.name == NOME_PAI) refFinal = anf.transform.parent;
        Vector3 fim = refFinal.position;
        float rotFinal = refFinal.eulerAngles.y;

        Vector3 s = start.transform.position;
        float yChao = s.y;
        var rend = start.GetComponent<Renderer>();
        if (rend != null) yChao = rend.bounds.min.y;
        float yCampo = fim.y;

        // Pontos estimados a partir do screenshot (vista Top) e da camera atual da Timeline.
        // Ajuste movendo os pontos na Scene e rode o menu de novo.
        var pts = new List<KeyValuePair<string, Vector3>>
        {
            new KeyValuePair<string, Vector3>("P0_Inicio_AnfitrianStart", new Vector3(s.x, yChao, s.z)),
            new KeyValuePair<string, Vector3>("P1_SaidaVestiario_Costas", new Vector3(s.x, yChao, s.z - 2.0f)),
            new KeyValuePair<string, Vector3>("P2_Curva", new Vector3(s.x, yChao, -43.4f)),
            new KeyValuePair<string, Vector3>("P3_InicioEscada", new Vector3(-52.8f, yChao, -43.5f)),
            new KeyValuePair<string, Vector3>("P4_FimEscada", new Vector3(-48.4f, yCampo, -44.3f)),
            new KeyValuePair<string, Vector3>("P5_Final_MarcaPenalti", fim),
        };

        var go = new GameObject(NOME_CAMINHO);
        SceneManager.MoveGameObjectToScene(go, anf.scene);
        Undo.RegisterCreatedObjectUndo(go, "Criar Caminho Anfitria");
        var cam = go.AddComponent<CaminhoCinematica>();
        cam.rotacaoFinalY = rotFinal;
        foreach (var kv in pts)
        {
            var p = new GameObject(kv.Key);
            Undo.RegisterCreatedObjectUndo(p, "Criar ponto");
            p.transform.SetParent(go.transform, false);
            p.transform.position = kv.Value;
        }
        Debug.Log("[Cinematica] Caminho_Anfitria criado com " + pts.Count + " pontos (estimados). Ajuste na Scene se precisar.");
        return cam;
    }

    static GameObject GarantirPai(GameObject anf)
    {
        Transform t = anf.transform;
        GameObject pai;
        if (t.parent != null && t.parent.name == NOME_PAI)
        {
            pai = t.parent.gameObject;
        }
        else
        {
            pai = new GameObject(NOME_PAI);
            SceneManager.MoveGameObjectToScene(pai, anf.scene);
            Undo.RegisterCreatedObjectUndo(pai, "Criar pai Anfitria");
            if (t.parent != null) pai.transform.SetParent(t.parent, false);
            pai.transform.position = t.position;
            pai.transform.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
            pai.transform.SetSiblingIndex(t.GetSiblingIndex());
            Undo.SetTransformParent(t, pai.transform, "Anfitria dentro do pai");
            Undo.RecordObject(t, "Zerar Anfitria");
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
        }
        var anim = pai.GetComponent<Animator>();
        if (anim == null) anim = Undo.AddComponent<Animator>(pai);
        anim.applyRootMotion = false;
        anim.runtimeAnimatorController = null;
        anim.avatar = null;
        return pai;
    }

    // ---------------------------------------------------------------- trajeto

    class Trajeto
    {
        public List<float> t = new List<float>();
        public List<Vector3> pos = new List<Vector3>();
        public List<float> yawCorpo = new List<float>();
        public List<float> yawCam = new List<float>();
        public List<Vector3> dirMov = new List<Vector3>();
        public float duracao, comprimento;
        public float tEscadaIni, tEscadaFim;
        public List<TrechoResolvido> trechos = new List<TrechoResolvido>();
        public List<string> avisos = new List<string>();
        public List<float> s = new List<float>();
        public Func<float, Vector3> posEm;
        public Vector3 dir0;
        // usados pelas falas (mesmo mapeamento dos marcadores de costas)
        public Func<float, float> tempoEm;
        public Func<MarcadorCaminho, float> sMarcador;
        public Func<int, float> sPonto;
        public int nPontos;
    }

    class TrechoResolvido
    {
        public string nome;
        public float sIni, sFim, tIni, tFim;
        public bool desdeInicio;
    }

    static Trajeto CalcularTrajeto(CaminhoCinematica c, List<Transform> pontos, float fatorVel)
    {
        var tr = new Trajeto();
        var orig = pontos.Select(p => p.position).ToList();
        int n = orig.Count;

        // 1) Polilinha com curvas arredondadas
        var d = new List<Vector3>();
        var mapa = new int[n];
        d.Add(orig[0]); mapa[0] = 0;
        for (int i = 1; i < n - 1; i++)
        {
            Vector3 a = orig[i - 1], p = orig[i], b = orig[i + 1];
            float l1 = Vector3.Distance(a, p), l2 = Vector3.Distance(p, b);
            float r = Mathf.Min(c.raioCurvas, 0.45f * l1, 0.45f * l2);
            if (r < 0.01f) { d.Add(p); mapa[i] = d.Count - 1; continue; }
            Vector3 A = p - (p - a).normalized * r;
            Vector3 B = p + (b - p).normalized * r;
            for (int k = 0; k <= 8; k++)
            {
                float u = k / 8f;
                d.Add((1 - u) * (1 - u) * A + 2 * (1 - u) * u * p + u * u * B);
                if (k == 4) mapa[i] = d.Count - 1;
            }
        }
        d.Add(orig[n - 1]); mapa[n - 1] = d.Count - 1;

        var cum = new float[d.Count];
        for (int i = 1; i < d.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(d[i - 1], d[i]);
        float L = cum[d.Count - 1];
        tr.comprimento = L;

        Func<float, Vector3> posEm = (s) =>
        {
            s = Mathf.Clamp(s, 0, L);
            int i = Array.BinarySearch(cum, s);
            if (i >= 0) return d[i];
            i = ~i;
            if (i <= 0) return d[0];
            if (i >= d.Count) return d[d.Count - 1];
            float seg = cum[i] - cum[i - 1];
            float u = seg > 1e-5f ? (s - cum[i - 1]) / seg : 0f;
            return Vector3.Lerp(d[i - 1], d[i], u);
        };

        tr.posEm = posEm;
        {
            Vector3 d0 = posEm(Mathf.Min(L, 0.5f)) - d[0]; d0.y = 0f;
            tr.dir0 = d0.sqrMagnitude > 1e-6f ? d0.normalized : Vector3.forward;
        }
        Func<int, float> sPonto = (idx) => (idx >= 0 && idx < n) ? cum[mapa[idx]] : -1f;
        float sEscIni = sPonto(c.pontoInicioEscada), sEscFim = sPonto(c.pontoFimEscada);
        bool temEscada = sEscIni >= 0 && sEscFim > sEscIni;
        float vir = Mathf.Max(0.1f, c.duracaoVirada);

        // Trechos de costas (marcadores) -> distancias no caminho
        Func<float, float> velBase = (s) => (temEscada && s >= sEscIni && s < sEscFim ? c.velocidadeEscada : c.velocidade) * fatorVel;
        Func<MarcadorCaminho, float> sMarcador = (m) =>
        {
            float b = sPonto(Mathf.Clamp(m.ponto, 0, n - 1));
            float metros = m.emSegundos ? m.deslocamento * velBase(Mathf.Min(L, b + 0.05f)) : m.deslocamento;
            return Mathf.Clamp(b + metros, 0f, L);
        };
        var brutos = new List<TrechoResolvido>();
        if (c.trechosCostas != null)
        {
            foreach (var tc in c.trechosCostas)
            {
                if (tc == null || !tc.ativo || tc.viraDeCostas == null || tc.voltaDeFrente == null) continue;
                var r = new TrechoResolvido { nome = string.IsNullOrEmpty(tc.nome) ? "Costas" : tc.nome, sIni = sMarcador(tc.viraDeCostas), sFim = sMarcador(tc.voltaDeFrente) };
                if (r.sFim < r.sIni) { float x = r.sIni; r.sIni = r.sFim; r.sFim = x; }
                // escada sempre de frente: corta o trecho
                if (temEscada && r.sIni < sEscFim && r.sFim > sEscIni)
                {
                    float margem = vir * c.velocidade * fatorVel * 0.6f + 0.3f;
                    if (r.sIni < sEscIni) r.sFim = Mathf.Min(r.sFim, sEscIni - margem);
                    else r.sIni = Mathf.Max(r.sIni, sEscFim + margem);
                    tr.avisos.Add("Trecho de costas '" + r.nome + "' encostava na escada e foi cortado (na escada ela fica de frente).");
                }
                if (r.sFim - r.sIni < 0.3f) { tr.avisos.Add("Trecho de costas '" + r.nome + "' ficou curto demais e foi ignorado."); continue; }
                brutos.Add(r);
            }
        }
        foreach (var r in brutos.OrderBy(x => x.sIni))
        {
            var ult = tr.trechos.Count > 0 ? tr.trechos[tr.trechos.Count - 1] : null;
            if (ult != null && r.sIni <= ult.sFim + 0.5f) { ult.sFim = Mathf.Max(ult.sFim, r.sFim); ult.nome += "+" + r.nome; continue; }
            r.desdeInicio = r.sIni <= 0.05f;
            tr.trechos.Add(r);
        }
        Func<float, bool> deCostas = (s) => { foreach (var r in tr.trechos) if (s >= r.sIni && s < r.sFim) return true; return false; };

        Func<float, float> vel = (s) =>
        {
            float v = c.velocidade;
            if (temEscada && s >= sEscIni && s < sEscFim) v = c.velocidadeEscada;
            if (deCostas(s)) v = Mathf.Min(v, c.velocidadeCostas);
            float f = Mathf.Clamp01(Mathf.Min(s, L - s) / Mathf.Max(0.01f, c.distanciaAceleracao));
            return Mathf.Max(0.05f, v * fatorVel * Mathf.SmoothStep(0.3f, 1f, f));
        };

        // 2) Integra tempo ao longo da distancia
        var sL = new List<float> { 0f };
        var tL = new List<float> { 0f };
        const float ds = 0.02f;
        float sa = 0f, ta = 0f;
        while (sa < L)
        {
            float passo = Mathf.Min(ds, L - sa);
            ta += passo / vel(sa + passo * 0.5f);
            sa += passo;
            sL.Add(sa); tL.Add(ta);
        }
        tr.duracao = ta;

        Func<float, float> tempoEm = (s) =>
        {
            int i = sL.BinarySearch(s);
            if (i >= 0) return tL[i];
            i = ~i;
            if (i <= 0) return 0f;
            if (i >= sL.Count) return ta;
            return Mathf.Lerp(tL[i - 1], tL[i], (s - sL[i - 1]) / Mathf.Max(1e-6f, sL[i] - sL[i - 1]));
        };
        Func<float, float> distEm = (t) =>
        {
            int i = tL.BinarySearch(t);
            if (i >= 0) return sL[i];
            i = ~i;
            if (i <= 0) return 0f;
            if (i >= tL.Count) return L;
            return Mathf.Lerp(sL[i - 1], sL[i], (t - tL[i - 1]) / Mathf.Max(1e-6f, tL[i] - tL[i - 1]));
        };

        foreach (var r in tr.trechos) { r.tIni = r.desdeInicio ? 0f : tempoEm(r.sIni); r.tFim = tempoEm(r.sFim); }
        tr.tempoEm = tempoEm;
        tr.sMarcador = sMarcador;
        tr.sPonto = sPonto;
        tr.nPontos = n;
        tr.tEscadaIni = temEscada ? tempoEm(sEscIni) : 0f;
        tr.tEscadaFim = temEscada ? tempoEm(sEscFim) : 0f;

        // 3) Amostra no tempo
        float yawPrevC = float.NaN, yawPrevK = float.NaN;
        int total = Mathf.CeilToInt(ta * FPS_CURVA);
        for (int k = 0; k <= total; k++)
        {
            float t = Mathf.Min(k / FPS_CURVA, ta);
            float s = distEm(t);
            Vector3 p = posEm(s);

            float yawMov = YawDir(posEm(s + 0.6f) - posEm(s - 0.6f), yawPrevC, out Vector3 dirMov);
            float yawCam = YawDir(posEm(s + 1.5f) - posEm(s - 1.5f), yawPrevK, out _);
            if (!float.IsNaN(yawPrevC)) yawMov = yawPrevC + Mathf.DeltaAngle(yawPrevC, yawMov);
            if (!float.IsNaN(yawPrevK)) yawCam = yawPrevK + Mathf.DeltaAngle(yawPrevK, yawCam);
            yawPrevC = yawMov; yawPrevK = yawCam;

            float off = 0f;
            foreach (var r in tr.trechos)
            {
                float sobe = r.desdeInicio ? 1f : Mathf.SmoothStep(0f, 1f, (t - (r.tIni - vir * 0.5f)) / vir);
                float desce = Mathf.SmoothStep(0f, 1f, (t - (r.tFim - vir * 0.5f)) / vir);
                off += 180f * sobe * (1f - desce);
            }
            off = Mathf.Min(off, 180f);

            tr.t.Add(t);
            tr.s.Add(s);
            tr.pos.Add(p);
            tr.yawCorpo.Add(yawMov + off);
            tr.yawCam.Add(yawCam);
            tr.dirMov.Add(dirMov);
            if (t >= ta) break;
        }
        return tr;
    }

    static float YawDir(Vector3 v, float anterior, out Vector3 dir)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 1e-6f)
        {
            float y = float.IsNaN(anterior) ? 0f : anterior;
            dir = Quaternion.Euler(0, y, 0) * Vector3.forward;
            return y;
        }
        dir = v.normalized;
        return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }

    static void PreencherClipCaminho(AnimationClip clip, CaminhoCinematica c, Trajeto tr)
    {
        var px = new List<Keyframe>(); var py = new List<Keyframe>(); var pz = new List<Keyframe>(); var ry = new List<Keyframe>();
        float t0 = c.tempoInicio;
        Action<float, Vector3, float> add = (t, p, yaw) =>
        {
            if (px.Count > 0 && t <= px[px.Count - 1].time + 1e-4f) return;
            px.Add(new Keyframe(t, p.x)); py.Add(new Keyframe(t, p.y)); pz.Add(new Keyframe(t, p.z)); ry.Add(new Keyframe(t, yaw));
        };
        if (t0 > 0.01f) add(0f, tr.pos[0], tr.yawCorpo[0]);
        for (int i = 0; i < tr.t.Count; i++) add(t0 + tr.t[i], tr.pos[i], tr.yawCorpo[i]);

        // Giro final para a rotacao de chegada e parada
        float tCh = t0 + tr.duracao;
        Vector3 pf = tr.pos[tr.pos.Count - 1];
        float yawUlt = tr.yawCorpo[tr.yawCorpo.Count - 1];
        float yawFinal = yawUlt + Mathf.DeltaAngle(yawUlt, c.rotacaoFinalY);
        float giro = Mathf.Max(0.1f, c.duracaoGiroFinal);
        for (int k = 1; k <= 8; k++)
        {
            float u = k / 8f;
            add(tCh + u * giro, pf, Mathf.Lerp(yawUlt, yawFinal, Mathf.SmoothStep(0f, 1f, u)));
        }
        add(tCh + Mathf.Max(giro, c.tempoParadaFinal), pf, yawFinal);

        GravarCurvas(clip, px, py, pz, ry, null);
    }

    // Pose inicial da camera = o que a track antiga mostra no tempo 0 (clip + offsets da track), no espaco do pai da Camera.
    static void PoseInicialCamera(AnimationTrack antiga, Transform cam, out Vector3 pos, out Vector3 eul)
    {
        pos = cam.localPosition;
        eul = cam.localEulerAngles;
        if (antiga == null) return;

        AnimationClip clip = null;
        Vector3 offPos = Vector3.zero;
        Quaternion offRot = Quaternion.identity;
        bool removerOffset = false;

        var so = new SerializedObject(antiga);
        var pClip = so.FindProperty("m_InfiniteClip");
        if (pClip != null) clip = pClip.objectReferenceValue as AnimationClip;
        if (clip != null && !clip.empty)
        {
            var pp = so.FindProperty("m_InfiniteClipOffsetPosition");
            var pe = so.FindProperty("m_InfiniteClipOffsetEulerAngles");
            var pr = so.FindProperty("m_InfiniteClipRemoveOffset");
            if (pp != null) offPos = pp.vector3Value;
            if (pe != null) offRot = Quaternion.Euler(pe.vector3Value);
            if (pr != null) removerOffset = pr.boolValue;
        }
        else
        {
            var tc = antiga.GetClips().OrderBy(x => x.start).FirstOrDefault();
            var a = tc != null ? tc.asset as AnimationPlayableAsset : null;
            if (a == null || a.clip == null) return;
            clip = a.clip; offPos = a.position; offRot = a.rotation; removerOffset = a.removeStartOffset;
        }

        Func<string, float, float> val = (prop, padrao) =>
        {
            var cv = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), prop));
            return cv != null && cv.length > 0 ? cv.Evaluate(0f) : padrao;
        };
        Vector3 cp = new Vector3(val("m_LocalPosition.x", 0f), val("m_LocalPosition.y", 0f), val("m_LocalPosition.z", 0f));
        Vector3 ce = new Vector3(val("localEulerAnglesRaw.x", 0f), val("localEulerAnglesRaw.y", 0f), val("localEulerAnglesRaw.z", 0f));
        if (removerOffset) { cp = Vector3.zero; ce = Vector3.zero; }
        pos = offPos + offRot * cp;
        eul = (offRot * Quaternion.Euler(ce)).eulerAngles;
        Debug.Log("[Cinematica] Pose inicial da camera (track antiga, t=0): pos " + pos.ToString("F2") + " rot " + eul.ToString("F1"));
    }

    // Camera ATRAS da Anfitria: anda pelo mesmo caminho, 'cameraAtras' metros atras dela, olhando para ela.
    // Mantem a pose inicial da track antiga ate tempoInicio e faz transicao suave para o follow.
    static string PreencherClipCamera(AnimationClip clip, CaminhoCinematica c, Trajeto tr, Transform paiCam, Vector3 pos0Local, Vector3 eul0Local)
    {
        var px = new List<Keyframe>(); var py = new List<Keyframe>(); var pz = new List<Keyframe>();
        var rx = new List<Keyframe>(); var ry = new List<Keyframe>(); var rz = new List<Keyframe>();
        float t0 = c.tempoInicio;
        float yawPai = paiCam != null ? paiCam.eulerAngles.y : 0f;
        Func<Vector3, Vector3> paraMundo = (v) => paiCam != null ? paiCam.TransformPoint(v) : v;
        Func<Vector3, Vector3> paraLocal = (v) => paiCam != null ? paiCam.InverseTransformPoint(v) : v;

        Vector3 pos0 = paraMundo(pos0Local);
        float pitch0 = Mathf.DeltaAngle(0f, eul0Local.x);
        float roll0 = Mathf.DeltaAngle(0f, eul0Local.z);
        float yaw0 = eul0Local.y + yawPai;                       // yaw em mundo
        float alturaBase = pos0.y - tr.pos[0].y + c.cameraAcima;  // altura acima do chao do caminho

        Func<float, Vector3> posCaminho = (sc) =>
            sc >= 0f ? tr.posEm(sc) : tr.pos[0] + tr.dir0 * sc;   // antes do P0: prolonga para tras

        float yawAnt = yaw0;
        float yawSuave = yaw0;
        float dtAm = 1f / FPS_CURVA;
        float tAnt = -1f;
        Action<float, Vector3, float, float, float> add = (t, pMundo, pitch, yawMundo, roll) =>
        {
            if (px.Count > 0 && t <= px[px.Count - 1].time + 1e-4f) return;
            Vector3 p = paraLocal(pMundo);
            px.Add(new Keyframe(t, p.x)); py.Add(new Keyframe(t, p.y)); pz.Add(new Keyframe(t, p.z));
            rx.Add(new Keyframe(t, pitch)); ry.Add(new Keyframe(t, yawMundo - yawPai)); rz.Add(new Keyframe(t, roll));
        };

        // segura a pose inicial ate a Anfitria comecar a andar
        add(0f, pos0, pitch0, yaw0, roll0);
        if (t0 > 0.01f) add(t0, pos0, pitch0, yaw0, roll0);

        // Distancia GARANTIDA: a camera fica no caminho, num ponto cuja distancia HORIZONTAL ate ela e exatamente 'cameraAtras'.
        // (nas curvas a distancia "pelo caminho" encurta em linha reta; aqui a camera recua ate a distancia real bater.)
        // Nao depende da rotacao do corpo (virar de costas nao mexe na camera).
        float atras = Mathf.Max(0.3f, c.cameraAtras);
        Func<Vector3, Vector3, float> distH = (a, b) => { Vector3 v = a - b; v.y = 0f; return v.magnitude; };
        Func<float, Vector3, float> sCamera = (sAnf, anf) =>
        {
            float sc = sAnf - atras;
            if (distH(posCaminho(sc), anf) >= atras) return sc;
            float passo = 0.05f, sLonge = sc;
            for (int it = 0; it < 2000 && distH(posCaminho(sLonge), anf) < atras; it++) sLonge -= passo;
            float sPerto = sLonge + passo;
            for (int it = 0; it < 20; it++)
            {
                float m = 0.5f * (sLonge + sPerto);
                if (distH(posCaminho(m), anf) >= atras) sLonge = m; else sPerto = m;
            }
            return sLonge;
        };

        Vector3 desvio0 = Vector3.zero;   // diferenca entre a pose inicial e o follow no t=0 (some durante a transicao)
        float dMinH = float.MaxValue, dMaxH = 0f, dMin3 = float.MaxValue, dMax3 = 0f;
        float dMinHf = float.MaxValue, dMaxHf = 0f;
        Vector3 ult = pos0; float ultYaw = yaw0, ultPitch = pitch0, ultRoll = roll0;
        for (int i = 0; i < tr.t.Count; i++)
        {
            float t = tr.t[i];
            Vector3 alvoAnf = tr.pos[i];
            Vector3 follow = posCaminho(sCamera(tr.s[i], alvoAnf)) + Vector3.up * alturaBase;
            if (i == 0) desvio0 = pos0 - follow;

            float w = Mathf.SmoothStep(0f, 1f, c.cameraTransicao > 0.01f ? t / c.cameraTransicao : 1f);
            Vector3 p = follow + desvio0 * (1f - w);   // anda junto com ela desde o 1o frame

            float dh = distH(p, alvoAnf), d3 = Vector3.Distance(p, alvoAnf + Vector3.up * alturaBase);
            dMinH = Mathf.Min(dMinH, dh); dMaxH = Mathf.Max(dMaxH, dh);
            dMin3 = Mathf.Min(dMin3, d3); dMax3 = Mathf.Max(dMax3, d3);
            if (w >= 0.999f) { dMinHf = Mathf.Min(dMinHf, dh); dMaxHf = Mathf.Max(dMaxHf, dh); }

            Vector3 olhar = alvoAnf - p; olhar.y = 0f;
            float yawAlvo = olhar.sqrMagnitude > 0.09f ? Mathf.Atan2(olhar.x, olhar.z) * Mathf.Rad2Deg : yawAnt;
            yawAlvo = yawAnt + Mathf.DeltaAngle(yawAnt, yawAlvo);
            yawAnt = yawAlvo;
            float yawFollow = Mathf.LerpAngle(yaw0, yawAlvo, w);
            yawFollow = yaw0 + Mathf.DeltaAngle(yaw0, yawFollow);

            // suaviza e limita a velocidade de giro (VR)
            float dt = tAnt < 0f ? dtAm : Mathf.Max(1e-3f, t - tAnt);
            tAnt = t;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, c.cameraSuavizacao));
            float delta = Mathf.DeltaAngle(yawSuave, yawFollow) * k;
            float max = Mathf.Max(1f, c.cameraGiroMax) * dt;
            yawSuave += Mathf.Clamp(delta, -max, max);

            float pitch = Mathf.Lerp(pitch0, 0f, w);
            float roll = Mathf.Lerp(roll0, 0f, w);
            add(t0 + t, p, pitch, yawSuave, roll);
            ult = p; ultYaw = yawSuave; ultPitch = pitch; ultRoll = roll;
        }

        // parada no fim: termina de girar suavemente para ela
        float tCh = t0 + tr.duracao;
        float tFimC = tCh + Mathf.Max(c.duracaoGiroFinal, c.tempoParadaFinal);
        Vector3 vf = tr.pos[tr.pos.Count - 1] - ult; vf.y = 0f;
        float yawFim = vf.sqrMagnitude > 0.09f ? Mathf.Atan2(vf.x, vf.z) * Mathf.Rad2Deg : ultYaw;
        yawFim = ultYaw + Mathf.DeltaAngle(ultYaw, yawFim);
        add(Mathf.Min(tCh + 1.5f, tFimC - 0.05f), ult, ultPitch, yawFim, ultRoll);
        add(tFimC, ult, ultPitch, yawFim, ultRoll);

        GravarCurvas(clip, px, py, pz, ry, rx, rz);

        if (dMinHf > dMaxHf) { dMinHf = dMinH; dMaxHf = dMaxH; }
        string r = "Distancia camera-Anfitria (horizontal): " + dMinH.ToString("0.00") + " a " + dMaxH.ToString("0.00") + " m na caminhada toda; " +
                   dMinHf.ToString("0.00") + " a " + dMaxHf.ToString("0.00") + " m depois da transicao (alvo " + atras.ToString("0.00") + " m). " +
                   "3D (mesma altura de olhos): " + dMin3.ToString("0.00") + " a " + dMax3.ToString("0.00") + " m.\n";
        Debug.Log("[Cinematica] " + r.TrimEnd());
        return r;
    }

    static void GravarCurvas(AnimationClip clip, List<Keyframe> px, List<Keyframe> py, List<Keyframe> pz, List<Keyframe> ry, List<Keyframe> rx, List<Keyframe> rz = null)
    {
        clip.ClearCurves();
        clip.frameRate = 60f;
        var zero = new List<Keyframe> { new Keyframe(0f, 0f), new Keyframe(px[px.Count - 1].time, 0f) };
        SetCurva(clip, "m_LocalPosition.x", px);
        SetCurva(clip, "m_LocalPosition.y", py);
        SetCurva(clip, "m_LocalPosition.z", pz);
        SetCurva(clip, "localEulerAnglesRaw.x", rx ?? zero);
        SetCurva(clip, "localEulerAnglesRaw.y", ry);
        SetCurva(clip, "localEulerAnglesRaw.z", rz ?? zero);
        var st = AnimationUtility.GetAnimationClipSettings(clip);
        st.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, st);
        EditorUtility.SetDirty(clip);
    }

    static void SetCurva(AnimationClip clip, string prop, List<Keyframe> keys)
    {
        var curva = new AnimationCurve(keys.ToArray());
        for (int i = 0; i < curva.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curva, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curva, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), prop), curva);
    }

    // ---------------------------------------------------------------- clips humanoid

    // Copia um clip humanoid para um .anim "in place":
    // - opcionalmente invertido no tempo (andar de costas);
    // - remove a tendencia linear das curvas RootT.x/y/z (o quanto o corpo anda/sobe por ciclo), mantendo o balanco.
    //   Assim o loop fecha sem "snap" e o corpo fica sempre em cima do transform (quem anda e o pai).
    // deslocamento = RootT(fim) - RootT(inicio) do clip ORIGINAL (unidades humanoid), usado para a cadencia.
    static AnimationClip CriarClipInPlace(AnimationClip src, string caminho, bool inverter, out Vector3 deslocamento)
    {
        deslocamento = Vector3.zero;
        AnimationClip dst = CarregarOuCriarClip(caminho);
        dst.ClearCurves();
        dst.frameRate = src.frameRate;
        float L = Mathf.Max(0.0001f, src.length);
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            var c = AnimationUtility.GetEditorCurve(src, b);
            if (c == null || c.length == 0) continue;
            var k = c.keys;
            var nk = new Keyframe[k.Length];
            for (int i = 0; i < k.Length; i++)
            {
                if (!inverter) { nk[i] = k[i]; continue; }
                var o = k[k.Length - 1 - i];
                var nKey = new Keyframe(L - o.time, o.value, -o.outTangent, -o.inTangent, o.outWeight, o.inWeight);
                if (o.weightedMode == WeightedMode.In) nKey.weightedMode = WeightedMode.Out;
                else if (o.weightedMode == WeightedMode.Out) nKey.weightedMode = WeightedMode.In;
                else nKey.weightedMode = o.weightedMode;
                nk[i] = nKey;
            }

            string prop = b.propertyName;
            if (b.type == typeof(Animator) && (prop == "RootT.x" || prop == "RootT.y" || prop == "RootT.z"))
            {
                float v0 = c.Evaluate(0f), v1 = c.Evaluate(L);
                float d = v1 - v0;
                if (prop == "RootT.x") deslocamento.x = d; else if (prop == "RootT.y") deslocamento.y = d; else deslocamento.z = d;
                float t0 = nk[0].time, tN = nk[nk.Length - 1].time;
                float slope = (nk[nk.Length - 1].value - nk[0].value) / Mathf.Max(0.0001f, tN - t0);
                for (int i = 0; i < nk.Length; i++)
                {
                    var q = nk[i];
                    q.value -= slope * (q.time - t0);
                    if (!float.IsInfinity(q.inTangent)) q.inTangent -= slope;
                    if (!float.IsInfinity(q.outTangent)) q.outTangent -= slope;
                    nk[i] = q;
                }
                // XZ: comeca em 0 -> o corpo fica EM CIMA do transform (antes o "de costas" ficava ~1,5 m deslocado
                // para o lado da camera, e por isso parecia que a camera chegava perto quando ela virava).
                if (prop != "RootT.y")
                {
                    float off = nk[0].value;
                    for (int i = 0; i < nk.Length; i++) { var q = nk[i]; q.value -= off; nk[i] = q; }
                }
            }
            AnimationUtility.SetEditorCurve(dst, b, new AnimationCurve(nk));
        }
        var st = AnimationUtility.GetAnimationClipSettings(src);
        st.startTime = 0f;
        st.stopTime = L;
        st.loopTime = true;
        st.loopBlend = false;
        st.loopBlendOrientation = true;     // Bake Into Pose: rotacao
        st.loopBlendPositionY = true;       // Bake Into Pose: Y
        st.loopBlendPositionXZ = true;      // Bake Into Pose: XZ
        st.keepOriginalOrientation = true;
        st.keepOriginalPositionY = true;
        st.keepOriginalPositionXZ = true;
        st.cycleOffset = 0f;
        st.mirror = false;
        AnimationUtility.SetAnimationClipSettings(dst, st);
        EditorUtility.SetDirty(dst);
        return dst;
    }

    static float MetrosPorUnidadeHumanoid(GameObject anf, Animator anim, AnimationClip clipRef)
    {
        try
        {
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return -1f;
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var cy = AnimationUtility.GetEditorCurve(clipRef, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"));
            if (hips == null || cy == null || cy.length == 0) return -1f;
            float alturaQuadril = hips.position.y - anim.transform.position.y;
            float rootY = cy.Evaluate(0f);
            if (alturaQuadril < 0.1f || rootY < 0.1f) return -1f;
            return alturaQuadril / rootY;
        }
        catch { return -1f; }
    }

    static float SenoEscada(List<Transform> pontos, CaminhoCinematica c)
    {
        int a = c.pontoInicioEscada, b = c.pontoFimEscada;
        if (a < 0 || b >= pontos.Count || b <= a) return -1f;
        Vector3 d = pontos[b].position - pontos[a].position;
        float m = d.magnitude;
        return m > 0.01f ? Mathf.Abs(d.y) / m : -1f;
    }

    static AnimationClip CarregarClipFbx(string caminho)
    {
        return AssetDatabase.LoadAllAssetsAtPath(caminho).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    static AnimationClip CarregarOuCriarClip(string caminho)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(caminho);
        if (clip != null) return clip;
        string pasta = Path.GetDirectoryName(caminho).Replace('\\', '/');
        CriarPasta(pasta);
        clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(caminho) };
        AssetDatabase.CreateAsset(clip, caminho);
        return clip;
    }

    static void CriarPasta(string pasta)
    {
        if (AssetDatabase.IsValidFolder(pasta)) return;
        string pai = Path.GetDirectoryName(pasta).Replace('\\', '/');
        CriarPasta(pai);
        AssetDatabase.CreateFolder(pai, Path.GetFileName(pasta));
    }

    // ---------------------------------------------------------------- timeline helpers

    static void ConfigurarTrack(AnimationTrack t)
    {
        t.trackOffset = TrackOffset.ApplyTransformOffsets;
        t.position = Vector3.zero;
        t.rotation = Quaternion.identity;
    }

    static TimelineClip AddClip(AnimationTrack trk, AnimationClip ac, double ini, double dur, string nome, bool loop, bool removerOffset)
    {
        var c = trk.CreateClip(ac);
        c.start = ini;
        c.duration = dur;
        c.displayName = nome;
        var a = c.asset as AnimationPlayableAsset;
        if (a != null)
        {
            a.loop = loop ? AnimationPlayableAsset.LoopMode.On : AnimationPlayableAsset.LoopMode.Off;
            a.removeStartOffset = removerOffset;
            a.position = Vector3.zero;
            a.rotation = Quaternion.identity;
        }
        return c;
    }

    static PlayableDirector AcharDirector()
    {
        PlayableDirector melhor = null;
        foreach (var go in TodosObjetos())
        {
            var d = go.GetComponent<PlayableDirector>();
            if (d == null || !(d.playableAsset is TimelineAsset)) continue;
            if (go.name == "Timeline") return d;
            if (melhor == null) melhor = d;
        }
        return melhor;
    }

    static Animator AcharAnimatorCamera(PlayableDirector director, TimelineAsset timeline, out AnimationTrack trackAntiga)
    {
        trackAntiga = null;
        foreach (var t in timeline.GetOutputTracks())
        {
            if (!(t is AnimationTrack) || t.name.StartsWith(PREFIXO)) continue;
            var a = director.GetGenericBinding(t) as Animator;
            if (a != null && a.gameObject.name == "Camera") { trackAntiga = (AnimationTrack)t; return a; }
        }
        Debug.LogWarning("[Cinematica] Nao achei a Camera ligada na Timeline. A track da camera nao foi criada.");
        return null;
    }

    static void Backup(TimelineAsset timeline)
    {
        try
        {
            string raiz = Path.GetDirectoryName(Application.dataPath);
            string pasta = Path.Combine(raiz, "_Backup_Deletados", "CinematicaAnfitria_Auto_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string tl = AssetDatabase.GetAssetPath(timeline);
            var cena = SceneManager.GetActiveScene().path;
            foreach (var rel in new[] { tl, cena })
            {
                if (string.IsNullOrEmpty(rel)) continue;
                string orig = Path.Combine(raiz, rel);
                if (!File.Exists(orig)) continue;
                string dest = Path.Combine(pasta, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(orig, dest, true);
            }
            Debug.Log("[Cinematica] Backup em " + pasta);
        }
        catch (Exception e) { Debug.LogWarning("[Cinematica] Backup falhou: " + e.Message); }
    }

    static IEnumerable<GameObject> TodosObjetos()
    {
        var cena = SceneManager.GetActiveScene();
        foreach (var r in cena.GetRootGameObjects())
            foreach (var t in r.GetComponentsInChildren<Transform>(true))
                yield return t.gameObject;
    }

    static GameObject Achar(string nome)
    {
        return TodosObjetos().FirstOrDefault(g => g.name == nome);
    }

    static void Erro(string msg)
    {
        Debug.LogError("[Cinematica] " + msg);
        if (!s_Silencioso) EditorUtility.DisplayDialog("Cinematica", msg, "OK");
    }

    // ---------------------------------------------------------------- falas (voz)

    // Tempo (Timeline) em que a fala comeca.
    // Metros: mesmo calculo dos marcadores de costas (ponto + metros no caminho -> tempo em que ela passa ali).
    // Segundos: tempo em que ela passa no ponto + 'deslocamento' segundos (exato, ja com a duracao total aplicada).
    static double TempoFala(Trajeto tr, MarcadorCaminho m, double tInicio)
    {
        if (m == null) return tInicio;
        if (m.emSegundos)
        {
            float sB = tr.sPonto(Mathf.Clamp(m.ponto, 0, tr.nPontos - 1));
            return tInicio + tr.tempoEm(sB) + m.deslocamento;
        }
        return tInicio + tr.tempoEm(tr.sMarcador(m));
    }

    class FalaResolvida
    {
        public FalaCaminho fala;
        public string nome;
        public double ini, fim;
        public AudioSource fonte;
    }

    static string MontarFalas(TimelineAsset timeline, PlayableDirector director, CaminhoCinematica c, Trajeto tr, GameObject pai, double tInicio, double tChegada, double tFim)
    {
        if (c.falas == null || c.falas.Count == 0) return "";
        var lista = new List<FalaResolvida>();
        AudioSource vozPadrao = null;
        int idx = 0;
        foreach (var f in c.falas)
        {
            idx++;
            if (f == null || !f.ativo) continue;
            string nome = string.IsNullOrEmpty(f.nome) ? "Fala " + idx : f.nome;
            if (f.audio == null) { Debug.LogWarning("[Cinematica] Fala '" + nome + "' sem audio: ignorada (arraste um AudioClip no campo 'audio')."); continue; }
            double ini = TempoFala(tr, f.inicio, tInicio);
            if (ini < 0) { Debug.LogWarning("[Cinematica] Fala '" + nome + "' ficaria antes do 0 s da Timeline: comeca no 0 s."); ini = 0; }
            if (f.volume <= 0.001f) Debug.LogWarning("[Cinematica] Fala '" + nome + "' esta com volume 0 (muda).");
            AudioSource fonte = f.destino;
            if (fonte == null)
            {
                if (vozPadrao == null) vozPadrao = GarantirVoz(pai, c);
                fonte = vozPadrao;
            }
            lista.Add(new FalaResolvida { fala = f, nome = nome, ini = ini, fim = ini + f.audio.length, fonte = fonte });
        }
        if (lista.Count == 0) return "Falas: nenhuma fala ativa com audio.\n";

        // Uma AudioTrack por AudioSource (normalmente so a CINE_Anfitria_Voz)
        var tracks = new Dictionary<AudioSource, AudioTrack>();
        var nomesUsados = new HashSet<string>();
        foreach (var fr in lista)
        {
            if (tracks.ContainsKey(fr.fonte)) continue;
            string nomeTrk = fr.fonte == vozPadrao ? PREFIXO + "Anfitria_Voz" : PREFIXO + "Voz_" + fr.fonte.gameObject.name;
            string base0 = nomeTrk; int k = 2;
            while (nomesUsados.Contains(nomeTrk)) nomeTrk = base0 + "_" + (k++);
            nomesUsados.Add(nomeTrk);
            var trk = timeline.CreateTrack<AudioTrack>(null, nomeTrk);
            director.SetGenericBinding(trk, fr.fonte);
            // A track da Timeline sobrescreve o spatialBlend do AudioSource (padrao 0 = 2D). Aqui fica igual ao desejado.
            float blend = fr.fonte == vozPadrao ? (c.vozEspacial ? 1f : 0f) : fr.fonte.spatialBlend;
            var so = new SerializedObject(trk);
            var pb = so.FindProperty("m_TrackProperties.spatialBlend");
            if (pb != null) { pb.floatValue = blend; so.ApplyModifiedPropertiesWithoutUndo(); }
            tracks[fr.fonte] = trk;
        }

        string r = "";
        FalaResolvida ant = null;
        foreach (var fr in lista.OrderBy(x => x.ini))
        {
            var trk = tracks[fr.fonte];
            var clip = trk.CreateClip(fr.fala.audio);
            clip.start = fr.ini;
            clip.duration = fr.fala.audio.length;
            clip.displayName = fr.nome;
            var asset = clip.asset as AudioPlayableAsset;
            if (asset != null)
            {
                asset.loop = false;
                var sa = new SerializedObject(asset);
                var pv = sa.FindProperty("m_ClipProperties.volume");
                if (pv != null) { pv.floatValue = Mathf.Clamp01(fr.fala.volume); sa.ApplyModifiedPropertiesWithoutUndo(); }
                else Debug.LogWarning("[Cinematica] Nao consegui aplicar o volume da fala '" + fr.nome + "' (versao da Timeline diferente).");
                EditorUtility.SetDirty(asset);
            }

            var m = fr.fala.inicio;
            string onde = m == null ? "" : " (P" + m.ponto + " " + (m.deslocamento >= 0 ? "+" : "") + m.deslocamento.ToString("0.##") + (m.emSegundos ? " s" : " m") + ")";
            string linha = "Fala '" + fr.nome + "': " + fr.ini.ToString("0.0") + " a " + fr.fim.ToString("0.0") + " s" + onde + ", audio " + fr.fala.audio.length.ToString("0.0") + " s, volume " + fr.fala.volume.ToString("0.00") + ", track " + trk.name;
            if (fr.fim > tChegada + 0.01 && fr.fim <= tFim + 0.01) linha += " (termina depois da chegada, durante a parada)";
            Debug.Log("[Cinematica] " + linha);
            r += linha + "\n";

            if (ant != null && fr.ini < ant.fim - 0.01)
            {
                string av = "Fala '" + fr.nome + "' (" + fr.ini.ToString("0.0") + " s) comeca antes de terminar a fala '" + ant.nome + "' (" + ant.fim.ToString("0.0") + " s): vao se sobrepor " + (ant.fim - fr.ini).ToString("0.0") + " s.";
                Debug.LogWarning("[Cinematica] " + av);
                r += "ATENCAO: " + av + "\n";
            }
            if (fr.fim > tFim + 0.01)
            {
                string av = "Fala '" + fr.nome + "' termina aos " + fr.fim.ToString("0.0") + " s, depois do fim da cinematica (" + tFim.ToString("0.0") + " s" + (c.moverSinalActivatePlayer ? ", ActivatePlayer" : "") + "). Ela pode ser cortada.";
                Debug.LogWarning("[Cinematica] " + av);
                r += "ATENCAO: " + av + "\n";
            }
            if (ant == null || fr.fim > ant.fim) ant = fr;
        }
        foreach (var trk in tracks.Values) EditorUtility.SetDirty(trk);
        return "Falas (" + lista.Count + "):\n" + r;
    }

    // AudioSource padrao das falas: filho 'Anfitria_Voz' do pai Anfitria_Cinematica (anda junto com ela), na altura da boca.
    static AudioSource GarantirVoz(GameObject pai, CaminhoCinematica c)
    {
        Transform t = pai.transform.Find(NOME_VOZ);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(NOME_VOZ);
            Undo.RegisterCreatedObjectUndo(go, "Criar voz Anfitria");
            go.transform.SetParent(pai.transform, false);
        }
        else go = t.gameObject;
        Undo.RecordObject(go.transform, "Altura voz");
        go.transform.localPosition = new Vector3(0f, c.vozAltura, 0f);
        go.transform.localRotation = Quaternion.identity;

        AudioSource src = go.GetComponent<AudioSource>();
        if (src == null) src = Undo.AddComponent<AudioSource>(go);
        Undo.RecordObject(src, "Config voz");
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = c.vozEspacial ? 1f : 0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = Mathf.Max(0.1f, c.vozDistanciaMin);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, c.vozDistanciaMax);
        src.dopplerLevel = 0f;
        EditorUtility.SetDirty(src);
        return src;
    }

    // ---------------------------------------------------------------- torcida

    const string SINAL_CELEBRATING = PASTA_GERADOS + "/Torcida_Celebrating.signal";
    const string SINAL_INTERESTED = PASTA_GERADOS + "/Torcida_Interested.signal";

    static string MontarTorcida(TimelineAsset timeline, PlayableDirector director, CaminhoCinematica c, Trajeto tr, double tInicio, double tChegada)
    {
        GameObject stadium = Achar("Stadium");
        var crowd = stadium != null ? stadium.GetComponent<AiKodex.StadiumCreator2.CrowdManager>() : null;
        if (crowd == null)
        {
            Debug.LogWarning("[Cinematica] Nao achei 'Stadium' com CrowdManager. Sinais da torcida nao criados.");
            return "Torcida: Stadium/CrowdManager nao encontrado.\n";
        }

        var ponte = stadium.GetComponent<TorcidaCinematicaPonte>();
        if (ponte == null) ponte = Undo.AddComponent<TorcidaCinematicaPonte>(stadium);
        Undo.RecordObject(ponte, "Ponte torcida");
        ponte.torcida = crowd;
        EditorUtility.SetDirty(ponte);

        var receiver = stadium.GetComponent<SignalReceiver>();
        if (receiver == null) receiver = Undo.AddComponent<SignalReceiver>(stadium);

        SignalAsset sCel = CarregarOuCriarSinal(SINAL_CELEBRATING);
        SignalAsset sInt = CarregarOuCriarSinal(SINAL_INTERESTED);
        Undo.RecordObject(receiver, "Reacoes torcida");
        DefinirReacao(receiver, sCel, ponte.Celebrating);
        DefinirReacao(receiver, sInt, ponte.Interested);
        EditorUtility.SetDirty(receiver);

        var trk = timeline.CreateTrack<SignalTrack>(null, PREFIXO + "Torcida");
        director.SetGenericBinding(trk, receiver);

        string r = "";
        bool temEscada = tr.tEscadaFim > tr.tEscadaIni + 0.01f;
        if (temEscada)
        {
            double tCel = Math.Max(0, tInicio + tr.tEscadaFim + c.atrasoCelebrating);
            var e1 = trk.CreateMarker<SignalEmitter>(tCel);
            e1.asset = sCel; e1.retroactive = false; e1.emitOnce = true;
            EditorUtility.SetDirty(e1);
            r += "Torcida: Celebrating aos " + tCel.ToString("0.0") + " s";
        }
        else r += "Torcida: sem trecho de escada, Celebrating nao criado";
        var e2 = trk.CreateMarker<SignalEmitter>(tChegada);
        e2.asset = sInt; e2.retroactive = false; e2.emitOnce = true;
        EditorUtility.SetDirty(e2);
        r += ", Interested aos " + tChegada.ToString("0.0") + " s.\n";
        return r;
    }

    static SignalAsset CarregarOuCriarSinal(string caminho)
    {
        var s = AssetDatabase.LoadAssetAtPath<SignalAsset>(caminho);
        if (s != null) return s;
        CriarPasta(Path.GetDirectoryName(caminho).Replace('\\', '/'));
        s = ScriptableObject.CreateInstance<SignalAsset>();
        AssetDatabase.CreateAsset(s, caminho);
        return s;
    }

    static void DefinirReacao(SignalReceiver r, SignalAsset s, UnityAction acao)
    {
        if (r.GetReaction(s) != null) r.Remove(s);
        var ev = new UnityEvent();
        UnityEventTools.AddPersistentListener(ev, acao);
        ev.SetPersistentListenerState(0, UnityEventCallState.RuntimeOnly);
        r.AddReaction(s, ev);
    }
}

[CustomEditor(typeof(CaminhoCinematica))]
public class CaminhoCinematicaEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Os filhos deste objeto sao os pontos do caminho (na ordem). Mova os pontos na Scene e clique no botao para refazer as tracks CINE_ da Timeline.", MessageType.Info);
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Montar na Timeline", GUILayout.Height(30)))
            MontarEntradaAnfitria.Montar(false);
    }

    // Bolinhas arrastaveis na Scene: rosa = "Vira de costas", verde = "Volta de frente", azul = inicio de uma fala.
    // Arrastar move o marcador AO LONGO do caminho (o ponto de referencia continua o mesmo; muda o deslocamento).
    void OnSceneGUI()
    {
        var c = (CaminhoCinematica)target;
        if (c == null || c.Pontos().Count < 2) return;
        bool mudou = false;
        if (c.trechosCostas != null)
        {
            foreach (var tc in c.trechosCostas)
            {
                if (tc == null || !tc.ativo) continue;
                mudou |= Marcador(c, tc.viraDeCostas, new Color(1f, 0.3f, 1f));
                mudou |= Marcador(c, tc.voltaDeFrente, new Color(0.3f, 1f, 0.3f));
            }
        }
        // Falas (azul): arrastar muda onde o audio comeca
        if (c.falas != null)
        {
            foreach (var f in c.falas)
            {
                if (f == null || !f.ativo) continue;
                mudou |= Marcador(c, f.inicio, CaminhoCinematica.CorFala);
            }
        }
        if (mudou && c.autoAtualizar && CaminhoCinematica.PedidoAutoAtualizar != null)
            CaminhoCinematica.PedidoAutoAtualizar(c);
    }

    static bool Marcador(CaminhoCinematica c, MarcadorCaminho m, Color cor)
    {
        if (m == null) return false;
        Vector3 pos = c.PosicaoEm(c.DistanciaMarcadorAprox(m));
        float tam = HandleUtility.GetHandleSize(pos) * 0.15f;
        Handles.color = cor;
        EditorGUI.BeginChangeCheck();
        Vector3 nova = Handles.FreeMoveHandle(pos, tam, Vector3.zero, Handles.SphereHandleCap);
        if (!EditorGUI.EndChangeCheck()) return false;
        Undo.RecordObject(c, "Mover marcador");
        int idx = Mathf.Clamp(m.ponto, 0, c.Pontos().Count - 1);
        float metros = c.ProjetarNoCaminho(nova) - c.DistanciaDoPonto(idx);
        if (m.emSegundos)
        {
            float v = Mathf.Max(0.05f, c.velocidade * c.fatorGizmo);
            m.deslocamento = Mathf.Round(metros / v * 100f) / 100f;
        }
        else m.deslocamento = Mathf.Round(metros * 100f) / 100f;
        EditorUtility.SetDirty(c);
        return true;
    }
}

// Configura o import do Ascending_Stairs.fbx (Mixamo, In Place) so na primeira importacao
// (ou quando o menu acha o rig errado). Depois disso, o que voce mudar no Inspector e respeitado.
public class ImportAnimacoesCinematica : AssetPostprocessor
{
    public override uint GetVersion() { return 1; }

    static bool EhEscada(string caminho)
    {
        return string.Equals(caminho, MontarEntradaAnfitria.ESCADA_FBX, StringComparison.OrdinalIgnoreCase);
    }

    void OnPreprocessModel()
    {
        if (!EhEscada(assetPath) || !assetImporter.importSettingsMissing) return;
        ConfigurarRig((ModelImporter)assetImporter);
    }

    void OnPreprocessAnimation()
    {
        if (!EhEscada(assetPath) || !assetImporter.importSettingsMissing) return;
        ConfigurarClips((ModelImporter)assetImporter);
    }

    static void ConfigurarRig(ModelImporter mi)
    {
        mi.importAnimation = true;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    }

    static void ConfigurarClips(ModelImporter mi)
    {
        var clips = mi.clipAnimations;
        if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.loopTime = true;
            c.lockRootRotation = true;        // Bake Into Pose: rotacao (quem gira e o pai)
            c.keepOriginalOrientation = true;
            c.lockRootHeightY = true;         // Bake Into Pose: Y (a subida vem do pai pelo caminho)
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true;      // Bake Into Pose: XZ (In Place)
            c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
    }

    // Chamado pelo menu: se o FBX existe mas nao esta Humanoid, corrige e reimporta.
    public static void GarantirImportEscada()
    {
        var mi = AssetImporter.GetAtPath(MontarEntradaAnfitria.ESCADA_FBX) as ModelImporter;
        if (mi == null) return;
        if (mi.animationType == ModelImporterAnimationType.Human && mi.clipAnimations != null && mi.clipAnimations.Length > 0) return;
        ConfigurarRig(mi);
        mi.SaveAndReimport();
        mi = AssetImporter.GetAtPath(MontarEntradaAnfitria.ESCADA_FBX) as ModelImporter;
        ConfigurarClips(mi);
        mi.SaveAndReimport();
        Debug.Log("[Cinematica] Import do Ascending_Stairs ajustado para Humanoid + Bake Into Pose.");
    }
}

// Auto atualizar: quando um valor do CaminhoCinematica muda no Inspector, refaz a Timeline ~0,6 s depois (debounce).
// So no Editor, fora do Play, e so se o Caminho_Anfitria estiver selecionado (evita rodar ao abrir a cena/recompilar).
[InitializeOnLoad]
static class AutoAtualizarCinematica
{
    static double s_Quando = -1;
    static readonly double s_IgnorarAte;

    static AutoAtualizarCinematica()
    {
        s_IgnorarAte = EditorApplication.timeSinceStartup + 3.0;
        CaminhoCinematica.PedidoAutoAtualizar = Pedir;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Pedir(CaminhoCinematica c)
    {
        if (c == null || MontarEntradaAnfitria.Montando) return;
        if (EditorApplication.timeSinceStartup < s_IgnorarAte) return;
        if (Selection.activeGameObject != c.gameObject) return;
        s_Quando = EditorApplication.timeSinceStartup + 0.6;
    }

    static void Tick()
    {
        if (s_Quando < 0 || EditorApplication.timeSinceStartup < s_Quando) return;
        s_Quando = -1;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        MontarEntradaAnfitria.Montar(true);
    }
}
