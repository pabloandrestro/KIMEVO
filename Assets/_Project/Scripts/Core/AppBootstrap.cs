using UnityEngine;

namespace Kimevo.Core
{
    /// <summary>
    /// Ajustes de arranque de la aplicacion.
    ///
    /// En movil vSync no impone ningun techo de framerate, asi que sin un limite explicito
    /// el dispositivo renderiza todo lo que puede. Eso calienta el telefono, y ARCore degrada
    /// la calidad del tracking cuando el dispositivo entra en throttling termico: mas fps
    /// acaba produciendo peor AR.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Techo de fotogramas por segundo. 60 es el equilibrio habitual en AR entre fluidez y temperatura.")]
        private int targetFrameRate = 60;

        [SerializeField]
        [Tooltip("Evita que la pantalla se apague mientras la persona explora o dibuja.")]
        private bool keepScreenOn = true;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;

            if (keepScreenOn)
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
        }
    }
}
