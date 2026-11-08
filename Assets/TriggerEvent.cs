using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class ColliderUnityEvent : UnityEvent<Collider> { }

public class TriggerEvent : MonoBehaviour
{
    [Tooltip("Lista de tags que podem acionar este trigger. Se estiver vazia, qualquer tag é aceita.")]
    public string[] useTags;

    [Tooltip("Se marcado, dispara só uma vez.")]
    public bool playOnce = false;

    private HashSet<string> tagSet;
    private bool hasTriggered;

    public ColliderUnityEvent _OnTriggerEnter;
    public ColliderUnityEvent _OnTriggerStay;
    public ColliderUnityEvent _OnTriggerExit;

    private void Awake()
    {
        if (useTags != null && useTags.Length > 0)
            tagSet = new HashSet<string>(useTags);
        else
            tagSet = null;
    }

    private bool IsTagAllowed(Collider other)
    {
        if (tagSet == null || tagSet.Count == 0) return true;
        return tagSet.Contains(other.tag);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsTagAllowed(other)) return;
        if (playOnce && hasTriggered) return;

        hasTriggered = true;
        _OnTriggerEnter.Invoke(other);

        Invoke(nameof(ResetTrigger), 1f);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsTagAllowed(other)) return;
        if (playOnce && hasTriggered) return;

        _OnTriggerStay.Invoke(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTagAllowed(other)) return;
        _OnTriggerExit.Invoke(other);
    }

    /// <summary>
    /// Reseta para poder disparar de novo.
    /// </summary>
    public void ResetTrigger()
    {
        hasTriggered = false;
    }
}
