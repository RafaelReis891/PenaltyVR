using AiKodex.StadiumCreator2;
using UnityEngine;

// Ponte entre a Timeline (SignalReceiver) e o CrowdManager do Stadium Creator 2.
// O CrowdManager.ApplyEmotion tem 2 parametros (emocao + intensidade) e o UnityEvent do Signal nao chama isso direto,
// entao este componente expoe metodos sem parametro. Nao altera o script do Stadium.
public class TorcidaCinematicaPonte : MonoBehaviour
{
    public CrowdManager torcida;
    [Range(0, 1)] public float intensidadeCelebrating = 1f;
    [Range(0, 1)] public float intensidadeInterested = 0.8f;

    CrowdManager Torcida()
    {
        if (torcida == null) torcida = GetComponent<CrowdManager>();
        return torcida;
    }

    public void Celebrating()
    {
        var t = Torcida();
        if (t != null) t.ApplyEmotion(CrowdEmotion.Celebrating, intensidadeCelebrating);
    }

    public void Interested()
    {
        var t = Torcida();
        if (t != null) t.ApplyEmotion(CrowdEmotion.Interested, intensidadeInterested);
    }
}
