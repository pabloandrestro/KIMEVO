using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using TMPro;
public class ArTest : MonoBehaviour
{
    public TMP_Text statusText;

    private void OnEnable()
    {
        ARSession.stateChanged += OnStateChanged;
    }

    private void OnDisable()
    {
        ARSession.stateChanged -= OnStateChanged;
    }

    private IEnumerator Start()
    {
        SetText("Comprobando ARCore...\nEstado: " + ARSession.state);

        if (ARSession.state == ARSessionState.None ||
            ARSession.state == ARSessionState.CheckingAvailability)
        {
            yield return ARSession.CheckAvailability();
        }

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            SetText("ARCore detectado.\nIntentando instalar...");
            yield return ARSession.Install();
        }

        UpdateStatus(ARSession.state);
    }

    private void OnStateChanged(ARSessionStateChangedEventArgs args)
    {
        UpdateStatus(args.state);
    }

    private void UpdateStatus(ARSessionState state)
    {
        switch (state)
        {
            case ARSessionState.Unsupported:
                SetText(
                    "ARCORE: NO COMPATIBLE\n\n" +
                    "Estado: Unsupported\n\n" +
                    "ARCore esta instalado, pero el dispositivo esta siendo rechazado."
                );
                break;

            case ARSessionState.NeedsInstall:
                SetText(
                    "ARCORE: NECESITA INSTALACION\n\n" +
                    "Estado: NeedsInstall"
                );
                break;

            case ARSessionState.Ready:
                SetText(
                    "ARCORE: COMPATIBLE\n\n" +
                    "Estado: Ready\n\n" +
                    "El dispositivo acepta ARCore."
                );
                break;

            case ARSessionState.SessionInitializing:
                SetText(
                    "ARCORE: INICIALIZANDO\n\n" +
                    "Estado: SessionInitializing\n\n" +
                    "Mueve lentamente el telefono."
                );
                break;

            case ARSessionState.SessionTracking:
                SetText(
                    "ARCORE FUNCIONANDO\n\n" +
                    "Estado: SessionTracking\n\n" +
                    "AR Foundation + ARCore estan funcionando."
                );
                break;

            case ARSessionState.CheckingAvailability:
                SetText("Comprobando disponibilidad de ARCore...");
                break;

            case ARSessionState.Installing:
                SetText("Instalando componentes de ARCore...");
                break;

            default:
                SetText("Estado AR: " + state);
                break;
        }
    }

    private void SetText(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log("AR_STATUS: " + message);
    }
}
