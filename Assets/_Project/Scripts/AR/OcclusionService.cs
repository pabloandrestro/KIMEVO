using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Kimevo.AR
{
    /// <summary>
    /// Enciende la oclusion de entorno solo si el dispositivo puede con ella, y se aparta
    /// limpiamente si no.
    ///
    /// La comprobacion no se puede hacer en Awake. El descriptor del subsistema no existe
    /// hasta que la sesion arranca, y aqui la sesion arranca tarde a proposito: ARAvailabilityGate
    /// la mantiene apagada hasta confirmar que ARCore responde. Preguntar antes devuelve null y
    /// se interpretaria como "no soportado" en un telefono que si lo soporta. Por eso se
    /// pregunta en Update hasta obtener una respuesta que no sea Unknown, con un limite de
    /// paciencia para no quedarse preguntando para siempre.
    ///
    /// Si no hay depth, el AROcclusionManager se apaga en vez de dejarlo pidiendo imagenes que
    /// nadie va a producir, y se avisa a quien escuche para que la sombra de contacto pase a
    /// ser la unica pista de asentamiento. Degradar es dejar de pedir, no seguir pidiendo en
    /// voz baja.
    /// </summary>
    [DefaultExecutionOrder(-55)]
    public sealed class OcclusionService : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Si se deja vacio se busca el AROcclusionManager de la escena.")]
        private AROcclusionManager occlusion;

        [SerializeField]
        [Tooltip("Calidad del mapa de profundidad. Medium es el equilibrio: Best cuesta bateria y calienta, y ARCore degrada el tracking cuando el telefono entra en throttling.")]
        private EnvironmentDepthMode depthMode = EnvironmentDepthMode.Medium;

        [SerializeField]
        [Tooltip("Suavizado temporal. Sin el, el borde de la oclusion hierve entre fotogramas.")]
        private bool temporalSmoothing = true;

        [SerializeField]
        [Tooltip("Segundos que se espera una respuesta del subsistema antes de darla por negativa.")]
        private float resolveTimeout = 12f;

        /// <summary>True cuando ya sabemos la respuesta, sea cual sea.</summary>
        public bool Resolved { get; private set; }

        /// <summary>True solo si el dispositivo produce imagen de profundidad de entorno.</summary>
        public bool DepthSupported { get; private set; }

        /// <summary>Se dispara una vez, con la respuesta definitiva.</summary>
        public event Action<bool> DepthResolved;

        private float startedAt;
        private float nextCheckAt;

        private void Awake()
        {
            if (occlusion == null)
            {
                occlusion = FindAnyObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
            }

            if (occlusion == null)
            {
                Debug.LogWarning("[KIMEVO] No hay AROcclusionManager en la escena: la oclusion queda desactivada.");
                Resolve(false);
                return;
            }

            // Se piden las preferencias desde el principio. Son peticiones, no ordenes: si el
            // dispositivo no puede, current* se quedara en Disabled y eso es lo que leeremos.
            occlusion.requestedEnvironmentDepthMode = depthMode;
            occlusion.environmentDepthTemporalSmoothingRequested = temporalSmoothing;
            occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;

            startedAt = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (Resolved || occlusion == null || Time.realtimeSinceStartup < nextCheckAt)
            {
                return;
            }

            nextCheckAt = Time.realtimeSinceStartup + 0.5f;

            XROcclusionSubsystemDescriptor descriptor = occlusion.descriptor;

            if (descriptor == null)
            {
                if (Time.realtimeSinceStartup - startedAt > resolveTimeout)
                {
                    Debug.LogWarning("[KIMEVO] El subsistema de oclusion no ha arrancado en "
                                     + resolveTimeout + "s. Se asume sin profundidad.");
                    Resolve(false);
                }

                return;
            }

            Supported supported = descriptor.environmentDepthImageSupported;

            if (supported == Supported.Unknown)
            {
                // ARCore devuelve Unknown mientras decide. Insistir es correcto; darlo por
                // negativo aqui apagaria la oclusion en dispositivos que si la tienen.
                if (Time.realtimeSinceStartup - startedAt > resolveTimeout)
                {
                    Debug.LogWarning("[KIMEVO] Soporte de profundidad sigue en Unknown tras "
                                     + resolveTimeout + "s. Se asume sin profundidad.");
                    Resolve(false);
                }

                return;
            }

            Resolve(supported == Supported.Supported);
        }

        private void Resolve(bool supported)
        {
            if (Resolved)
            {
                return;
            }

            Resolved = true;
            DepthSupported = supported;

            if (occlusion != null)
            {
                if (supported)
                {
                    Debug.Log("[KIMEVO] Profundidad de entorno SOPORTADA. Oclusion activa en modo "
                              + occlusion.currentEnvironmentDepthMode
                              + ", suavizado " + occlusion.environmentDepthTemporalSmoothingEnabled + ".");
                }
                else
                {
                    // Dejar de pedir. Un manager encendido pidiendo profundidad a un dispositivo
                    // que no la da es trabajo por frame a cambio de nada.
                    occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
                    occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
                    occlusion.enabled = false;

                    Debug.Log("[KIMEVO] Sin profundidad de entorno en este dispositivo. "
                              + "Oclusion apagada; la sombra de contacto queda como unica pista de apoyo.");
                }
            }

            DepthResolved?.Invoke(supported);
        }
    }
}
