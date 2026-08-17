using System;
using UnityEngine;

namespace Kimevo.Core
{
    /// <summary>
    /// Maquina de estados de la aplicacion. Su unica responsabilidad es decir en que modo
    /// estamos y avisar cuando cambia; quien se enciende y se apaga con cada modo lo decide
    /// cada controlador escuchando el evento, no esta clase.
    ///
    /// Hacerlo al reves - que este controlador conociera a todos los demas - convertiria un
    /// enum en el centro de gravedad del proyecto.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class AppModeController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Modo con el que arranca la experiencia. Explorar deja ver los planos sin poder tocar nada.")]
        private AppMode startMode = AppMode.Explore;

        public AppMode Mode { get; private set; }

        public event Action<AppMode> ModeChanged;

        private void Awake()
        {
            Mode = startMode;
        }

        private void Start()
        {
            // En Start y no en Awake: los oyentes se suscriben en su propio Awake/OnEnable.
            Emit();
        }

        public void SetMode(AppMode mode)
        {
            if (Mode == mode)
            {
                return;
            }

            Mode = mode;
            Emit();
        }

        /// <summary>Puente para botones de UI, que solo saben pasar int.</summary>
        public void SetModeIndex(int index)
        {
            SetMode((AppMode)Mathf.Clamp(index, 0, 2));
        }

        private void Emit()
        {
            Debug.Log("[KIMEVO] Modo: " + Mode);
            ModeChanged?.Invoke(Mode);
        }
    }
}
