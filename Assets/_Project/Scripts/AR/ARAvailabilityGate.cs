using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Kimevo.AR
{
    /// <summary>
    /// Comprueba que el dispositivo soporta AR antes de dejar arrancar la sesion.
    ///
    /// Sin esta puerta, ARSession empieza a llamar a ARCore desde el primer frame.
    /// Si ARCore aun esta descargando el perfil de calibracion del dispositivo, o si el
    /// dispositivo no esta soportado, su capa nativa (libarpresto_api.so) revienta con
    /// SIGSEGV y la app muere sin mensaje ni excepcion gestionable.
    ///
    /// Comprobar disponibilidad primero es el patron que recomienda AR Foundation, y ademas
    /// es lo que permite mostrar un mensaje digno en vez de un cierre en seco.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ARAvailabilityGate : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Si se deja vacio se busca la ARSession de la escena al arrancar.")]
        private ARSession session;

        [SerializeField]
        [Tooltip("Intentar instalar Google Play Services for AR si falta pero el dispositivo es compatible.")]
        private bool installIfNeeded = true;

        /// <summary>Estado devuelto por la comprobacion de disponibilidad.</summary>
        public ARSessionState Availability { get; private set; } = ARSessionState.None;

        /// <summary>True solo si ARCore confirma que puede correr en este dispositivo.</summary>
        public bool IsSupported { get; private set; }

        private IEnumerator Start()
        {
            if (session == null)
            {
                session = FindAnyObjectByType<ARSession>(FindObjectsInactive.Include);
            }

            if (session == null)
            {
                Debug.LogError("[KIMEVO] No hay ARSession en la escena.");
                yield break;
            }

            // La sesion no debe tocar ARCore hasta saber que responde.
            session.enabled = false;

            yield return ARSession.CheckAvailability();
            Availability = ARSession.state;
            Debug.Log("[KIMEVO] Disponibilidad AR tras CheckAvailability: " + ARSession.state);

            if (ARSession.state == ARSessionState.NeedsInstall && installIfNeeded)
            {
                Debug.Log("[KIMEVO] Falta Google Play Services for AR. Intentando instalar.");
                yield return ARSession.Install();
                Availability = ARSession.state;
                Debug.Log("[KIMEVO] Disponibilidad AR tras Install: " + ARSession.state);
            }

            if (ARSession.state == ARSessionState.Unsupported)
            {
                IsSupported = false;
                Debug.LogError("[KIMEVO] Este dispositivo NO soporta ARCore. La sesion AR queda desactivada.");
                yield break;
            }

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                IsSupported = false;
                Debug.LogError("[KIMEVO] Google Play Services for AR no esta disponible. Sesion AR desactivada.");
                yield break;
            }

            IsSupported = true;
            session.enabled = true;
            Debug.Log("[KIMEVO] Dispositivo compatible. Sesion AR activada. Estado: " + ARSession.state);
        }
    }
}
