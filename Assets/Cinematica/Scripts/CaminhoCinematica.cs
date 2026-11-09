using System;
using System.Collections.Generic;
using UnityEngine;

// Um ponto ao longo do caminho: ponto filho de referencia + deslocamento (metros ao longo do caminho, ou segundos depois do ponto).
[Serializable]
public class MarcadorCaminho
{
    [Tooltip("Indice do ponto filho de referencia (P0 = 0).")]
    public int ponto;
    [Tooltip("Metros ao longo do caminho a partir do ponto (negativo = antes). Se 'emSegundos', e o tempo depois do ponto.")]
    public float deslocamento;
    [Tooltip("Ligado: 'deslocamento' em segundos depois do ponto (ex.: 1 s depois do P4).")]
    public bool emSegundos;

    public MarcadorCaminho() { }
    public MarcadorCaminho(int ponto, float deslocamento, bool emSegundos = false)
    { this.ponto = ponto; this.deslocamento = deslocamento; this.emSegundos = emSegundos; }
}

// Trecho em que a Anfitria anda DE COSTAS (de frente para o player/camera que vem atras).
[Serializable]
public class TrechoCostas
{
    public string nome = "Costas";
    public bool ativo = true;
    [Tooltip("Onde ela VIRA DE COSTAS (se for no P0 com 0 m, ela ja comeca de costas).")]
    public MarcadorCaminho viraDeCostas = new MarcadorCaminho();
    [Tooltip("Onde ela VOLTA DE FRENTE.")]
    public MarcadorCaminho voltaDeFrente = new MarcadorCaminho();
}

// Fala (audio de voz) que comeca a tocar quando a Anfitria passa num ponto do caminho.
[Serializable]
public class FalaCaminho
{
    public string nome = "Fala";
    public bool ativo = true;
    [Tooltip("Arquivo de audio da fala (arraste aqui).")]
    public AudioClip audio;
    [Tooltip("Onde a fala COMECA: ponto + deslocamento (metros ao longo do caminho; ou, com 'emSegundos', segundos depois de ela passar no ponto - negativo = antes).")]
    public MarcadorCaminho inicio = new MarcadorCaminho();
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("AudioSource que toca esta fala. Vazio = usa o 'Anfitria_Voz' (criado sozinho na altura da cabeca da Anfitria).")]
    public AudioSource destino;
}

// Caminho da Anfitria para a cinematica de entrada (vestiario -> marca do penalti).
// Os FILHOS deste objeto sao os pontos do caminho, na ordem da Hierarchy (P0, P1, ...).
// Mova os pontos na Scene e clique em "Montar na Timeline" (ou Tools > Cinematica > Montar Entrada Anfitria).
// Este componente so guarda configuracao e desenha o caminho (Gizmos). Nao roda nada no Play.
public class CaminhoCinematica : MonoBehaviour
{
    [Header("Duracao total da caminhada (do inicio ate parar no ultimo ponto)")]
    [Tooltip("Se ligado, escala todas as velocidades (andar, escada, costas) para a caminhada durar exatamente 'duracaoTotalCaminhada'.")]
    public bool usarDuracaoTotal = true;
    [Range(3f, 60f)] public float duracaoTotalCaminhada = 15f;
    [Tooltip("Acelera/desacelera os clips (timeScale na Timeline) para os pes acompanharem a velocidade.")]
    public bool ajustarVelocidadeClips = true;
    [Tooltip("Calcula a velocidade natural dos clips pela distancia que o corpo anda em 1 ciclo (cadencia real). Se desligado, usa velocidadeNaturalAndar.")]
    public bool cadenciaAutomatica = true;
    [Tooltip("Velocidade (m/s) em que o clip Walking parece natural (timeScale 1). Usado se cadenciaAutomatica estiver desligado ou falhar.")]
    public float velocidadeNaturalAndar = 1.4f;
    [Tooltip("Limite do timeScale dos clips (acima disso fica caricato).")]
    public float limiteTimeScale = 3f;
    [Tooltip("Refaz a Timeline sozinho ~0,6 s depois de mudar um valor aqui no Inspector (so no Editor, fora do Play).")]
    public bool autoAtualizar = true;

    [Header("Torcida (Stadium / CrowdManager)")]
    [Tooltip("Celebrating quando ela sai da escada (pontoFimEscada) e Interested quando chega no ultimo ponto.")]
    public bool controlarTorcida = true;
    [Tooltip("Segundos somados ao tempo do Celebrating (negativo = antes).")]
    public float atrasoCelebrating = 0f;

    [Header("Tempo na Timeline")]
    [Tooltip("Segundo da Timeline em que a Anfitria comeca a andar.")]
    public float tempoInicio = 10f;
    [Tooltip("Segundos parada (idle) depois de chegar na marca.")]
    public float tempoParadaFinal = 4f;

    [Header("Velocidades (metros por segundo)")]
    public float velocidade = 1.4f;
    public float velocidadeEscada = 0.8f;
    public float velocidadeCostas = 1.0f;
    [Tooltip("Metros usados para acelerar no inicio e frear no fim.")]
    public float distanciaAceleracao = 1.2f;
    [Tooltip("Raio (m) para arredondar as curvas do caminho.")]
    public float raioCurvas = 0.8f;

    [Header("Andar de costas (marcadores no caminho; arraste as bolinhas na Scene)")]
    [Tooltip("Trechos em que ela anda de costas. Na escada ela fica sempre de frente (o trecho e cortado).")]
    public List<TrechoCostas> trechosCostas = new List<TrechoCostas>
    {
        new TrechoCostas { nome = "A - saida do vestiario", viraDeCostas = new MarcadorCaminho(0, 0f), voltaDeFrente = new MarcadorCaminho(3, -2f) },
        new TrechoCostas { nome = "B - depois da escada", viraDeCostas = new MarcadorCaminho(4, 1f, true), voltaDeFrente = new MarcadorCaminho(5, -3f) },
    };
    [Tooltip("Segundos para girar de frente -> costas (e de volta).")]
    public float duracaoVirada = 0.6f;

    [Header("Falas / audios de voz (bolinhas azuis na Scene; track CINE_Anfitria_Voz)")]
    [Tooltip("Cada fala toca quando ela passa no marcador. Clique em + para adicionar, arraste o audio e escolha ponto + deslocamento.")]
    public List<FalaCaminho> falas = new List<FalaCaminho>();
    [Tooltip("Ligado: som 3D saindo da Anfitria (bom para VR). Desligado: som 2D (igual nos dois ouvidos).")]
    public bool vozEspacial = true;
    [Tooltip("Ate esta distancia (m) da Anfitria o volume da voz fica cheio.")]
    public float vozDistanciaMin = 3f;
    [Tooltip("A partir desta distancia (m) a voz para de diminuir.")]
    public float vozDistanciaMax = 30f;
    [Tooltip("Altura (m) do AudioSource 'Anfitria_Voz' acima dos pes dela (boca).")]
    public float vozAltura = 1.6f;

    [Header("Escada (indices dos pontos filhos)")]
    public int pontoInicioEscada = 3;
    public int pontoFimEscada = 4;
    [Tooltip("Usa a animacao Ascending_Stairs (Mixamo) no trecho da escada, se existir em Assets/Cinematica/Animations.")]
    public bool usarAnimacaoEscada = true;
    [Tooltip("Calcula a velocidadeEscada pela cadencia do clip (degraus por ciclo x tamanho do degrau / duracao do clip).")]
    public bool velocidadeEscadaAutomatica = true;
    [Tooltip("Quantos degraus ela sobe em 1 ciclo do clip Ascending_Stairs (Mixamo costuma ser 2: um passo de cada pe).")]
    public float degrausPorCiclo = 2f;
    [Tooltip("Profundidade (horizontal) de cada degrau, em metros.")]
    public float profundidadeDegrau = 0.30f;
    [Tooltip("Altura de cada degrau, em metros.")]
    public float alturaDegrau = 0.18f;
    [Tooltip("Segundos de transicao Andar -> Escada -> Andar.")]
    public float duracaoBlendEscada = 0.4f;

    [Header("Rotacao final (Y) na marca do penalti")]
    public float rotacaoFinalY = -101.156f;
    public float duracaoGiroFinal = 1f;

    [Header("Camera do player ATRAS da Anfitria (track CINE_Camera_Atras)")]
    [Tooltip("Gera a camera seguindo atras dela. A 'Animation Track' antiga da Camera fica MUTADA (nao e apagada).")]
    public bool gerarTrackCamera = true;
    [Tooltip("Distancia (horizontal) GARANTIDA entre a camera e a Anfitria. A camera anda pelo mesmo caminho, atras dela.")]
    public float cameraAtras = 2.0f;
    [Tooltip("Metros acima da altura inicial da camera (a altura inicial vem da track antiga, em relacao ao chao do P0).")]
    public float cameraAcima = 0.25f;
    [Tooltip("Segundos de transicao suave da posicao inicial para o follow (comeca em tempoInicio).")]
    public float cameraTransicao = 2.5f;
    [Tooltip("Giro maximo da camera em graus por segundo (VR: valores baixos evitam enjoo).")]
    public float cameraGiroMax = 45f;
    [Tooltip("Suavizacao do giro (segundos). Maior = mais suave.")]
    public float cameraSuavizacao = 0.6f;
    [Tooltip("Move o Signal ActivatePlayer para o fim da cinematica (senao ele troca para o player aos 22 s, no meio da caminhada).")]
    public bool moverSinalActivatePlayer = true;

#if UNITY_EDITOR
    // Preenchido pelo script de Editor (MontarEntradaAnfitria). Nao roda no Play nem em build.
    public static System.Action<CaminhoCinematica> PedidoAutoAtualizar;

    void OnValidate()
    {
        if (!autoAtualizar || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (PedidoAutoAtualizar != null) PedidoAutoAtualizar(this);
    }
#endif

    public List<Transform> Pontos()
    {
        var lista = new List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
            lista.Add(transform.GetChild(i));
        return lista;
    }

    // ---- geometria simples do caminho (linha reta entre os pontos), usada pelos gizmos/handles
    public float DistanciaDoPonto(int indice)
    {
        var p = Pontos();
        float s = 0f;
        for (int i = 1; i < p.Count && i <= indice; i++) s += Vector3.Distance(p[i - 1].position, p[i].position);
        return s;
    }

    public float ComprimentoTotal() { return DistanciaDoPonto(int.MaxValue); }

    public Vector3 PosicaoEm(float s)
    {
        var p = Pontos();
        if (p.Count == 0) return transform.position;
        if (s <= 0f) return p[0].position;
        for (int i = 1; i < p.Count; i++)
        {
            float l = Vector3.Distance(p[i - 1].position, p[i].position);
            if (s <= l) return Vector3.Lerp(p[i - 1].position, p[i].position, l > 1e-5f ? s / l : 0f);
            s -= l;
        }
        return p[p.Count - 1].position;
    }

    // Distancia ao longo do caminho do marcador (segundos convertidos com 'velocidade' sem escala: e so para o gizmo).
    public float DistanciaMarcadorAprox(MarcadorCaminho m)
    {
        float metros = m.emSegundos ? m.deslocamento * velocidade * fatorGizmo : m.deslocamento;
        return Mathf.Clamp(DistanciaDoPonto(m.ponto) + metros, 0f, ComprimentoTotal());
    }

    // Preenchido pelo Montar (fator de velocidade da duracao total) para o gizmo de marcadores em segundos ficar certo.
    [HideInInspector] public float fatorGizmo = 1f;

    public float ProjetarNoCaminho(Vector3 pos)
    {
        var p = Pontos();
        float melhor = float.MaxValue, sMelhor = 0f, acc = 0f;
        for (int i = 1; i < p.Count; i++)
        {
            Vector3 a = p[i - 1].position, b = p[i].position;
            Vector3 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float u = l2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(pos - a, ab) / l2) : 0f;
            float d = (a + ab * u - pos).sqrMagnitude;
            if (d < melhor) { melhor = d; sMelhor = acc + Mathf.Sqrt(l2) * u; }
            acc += Mathf.Sqrt(l2);
        }
        return sMelhor;
    }

    void DesenharMarcador(MarcadorCaminho m, Color cor, string rotulo, float altura = 1.2f)
    {
        if (m == null) return;
        Vector3 pos = PosicaoEm(DistanciaMarcadorAprox(m));
        Gizmos.color = cor;
        Gizmos.DrawWireSphere(pos, 0.35f);
        Gizmos.DrawLine(pos, pos + Vector3.up * altura);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(pos + Vector3.up * (altura + 0.2f), rotulo);
#endif
    }

    public static readonly Color CorFala = new Color(0.2f, 0.6f, 1f);

    void OnDrawGizmos()
    {
        var p = Pontos();
        for (int i = 0; i < p.Count; i++)
        {
            if (p[i] == null) continue;
            Color cor = Color.cyan;
            if (i == pontoInicioEscada || i == pontoFimEscada) cor = Color.red;
            if (i == 0 || i == p.Count - 1) cor = Color.green;
            Gizmos.color = cor;
            Gizmos.DrawSphere(p[i].position, 0.25f);

            if (i < p.Count - 1 && p[i + 1] != null)
            {
                bool escada = i >= pontoInicioEscada && i < pontoFimEscada;
                Gizmos.color = escada ? Color.red : Color.cyan;
                Gizmos.DrawLine(p[i].position, p[i + 1].position);
            }
#if UNITY_EDITOR
            UnityEditor.Handles.Label(p[i].position + Vector3.up * 0.6f, p[i].name);
#endif
        }

        // Marcadores de costas
        if (trechosCostas != null)
        {
            foreach (var tc in trechosCostas)
            {
                if (tc == null || !tc.ativo) continue;
                DesenharMarcador(tc.viraDeCostas, new Color(1f, 0.3f, 1f), "Vira de costas (" + tc.nome + ")");
                DesenharMarcador(tc.voltaDeFrente, new Color(0.3f, 1f, 0.3f), "Volta de frente (" + tc.nome + ")");
            }
        }

        // Marcadores das falas (azul): onde cada audio comeca
        if (falas != null)
        {
            foreach (var f in falas)
            {
                if (f == null || !f.ativo) continue;
                DesenharMarcador(f.inicio, CorFala, "Fala: " + f.nome + (f.audio == null ? " (sem audio)" : ""), 1.8f);
            }
        }

        if (p.Count > 0 && p[p.Count - 1] != null)
        {
            Vector3 fim = p[p.Count - 1].position;
            Vector3 frente = Quaternion.Euler(0f, rotacaoFinalY, 0f) * Vector3.forward;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(fim + Vector3.up * 0.1f, fim + Vector3.up * 0.1f + frente * 1.5f);
        }
    }
}
