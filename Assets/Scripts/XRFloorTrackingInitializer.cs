using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Mantém o XROrigin em escala real. O prefab MR Interaction Setup pode
/// sobrescrever o modo de tracking e aplicar um offset de 1.4 m; este
/// componente corrige isso depois que o subsistema XR estiver disponível.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class XRFloorTrackingInitializer : MonoBehaviour
{
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private bool fallbackToDeviceTracking = true;
    [SerializeField] private float retryInterval = 0.25f;
    [SerializeField] private int maxRetries = 20;

    private IEnumerator Start()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (TryConfigureFloorTracking())
                yield break;

            yield return new WaitForSecondsRealtime(retryInterval);
        }

        Debug.LogWarning("[XRFloorTracking] Não foi possível configurar tracking de chão.");
    }

    private bool TryConfigureFloorTracking()
    {
        if (xrOrigin == null)
            return false;

        XROrigin[] origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (origins.Length == 0)
            origins = new[] { xrOrigin };

        List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);

        if (subsystems.Count == 0)
            return false;

        bool floorAccepted = false;

        foreach (XRInputSubsystem subsystem in subsystems)
        {
            if (subsystem == null || !subsystem.running)
                continue;

            if (subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                floorAccepted = true;
        }

        if (floorAccepted)
        {
            foreach (XROrigin origin in origins)
            {
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                origin.CameraYOffset = 0f;
            }
            Debug.Log("[XRFloorTracking] Tracking de chão configurado. CameraYOffset=0.");
            return true;
        }

        if (!fallbackToDeviceTracking)
            return false;

        foreach (XRInputSubsystem subsystem in subsystems)
        {
            if (subsystem == null || !subsystem.running)
                continue;

            subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
        }

        foreach (XROrigin origin in origins)
        {
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;
        }
        Debug.LogWarning("[XRFloorTracking] Tracking Floor indisponível; usando Device sem offset artificial.");
        return true;
    }
}
