using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Kimevo.Interaction
{
    /// <summary>
    /// Los toques de pantalla, ya limpios de los que caen sobre la interfaz.
    ///
    /// Existe por dos motivos que cuestan una tarde cada uno si no estan escritos en ningun sitio:
    ///
    /// 1. Con el Input System nuevo, los toques NO llegan hasta que se llama a
    ///    EnhancedTouchSupport.Enable(). Sin esa linea la app parece rota, no da ningun error,
    ///    y no hay nada en pantalla que sugiera donde mirar.
    ///
    /// 2. Un toque que empieza sobre un boton tiene que seguir siendo del boton aunque el dedo
    ///    se salga. Filtrar frame a frame no basta: al arrastrar fuera de la paleta se empezaria
    ///    a dibujar a media pulsacion. Por eso se recuerda que dedos nacieron sobre la interfaz.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class TouchService : MonoBehaviour
    {
        public readonly struct TouchSample
        {
            public readonly int Id;
            public readonly Vector2 Position;
            public readonly bool Began;
            public readonly bool Ended;

            public TouchSample(int id, Vector2 position, bool began, bool ended)
            {
                Id = id;
                Position = position;
                Began = began;
                Ended = ended;
            }
        }

        private readonly List<TouchSample> samples = new List<TouchSample>(4);
        private readonly HashSet<int> bornOverUi = new HashSet<int>();

        /// <summary>Toques validos de este frame, en orden de llegada.</summary>
        public IReadOnlyList<TouchSample> Touches => samples;

        public bool TryGetPrimary(out TouchSample touch)
        {
            if (samples.Count > 0)
            {
                touch = samples[0];
                return true;
            }

            touch = default;
            return false;
        }

        private void OnEnable()
        {
            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
            }

#if UNITY_EDITOR
            // En el editor no hay dedos. La simulacion convierte el raton en un toque, que es
            // lo que permite probar el dibujo con XR Simulation sin conectar el telefono.
            TouchSimulation.Enable();
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            TouchSimulation.Disable();
#endif
            if (EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Disable();
            }

            samples.Clear();
            bornOverUi.Clear();
        }

        private void Update()
        {
            samples.Clear();

            var active = ETouch.activeTouches;

            for (int i = 0; i < active.Count; i++)
            {
                ETouch t = active[i];

                bool began = t.phase == UnityEngine.InputSystem.TouchPhase.Began;
                bool ended = t.phase == UnityEngine.InputSystem.TouchPhase.Ended
                             || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled;

                if (began && IsOverUi(t.touchId))
                {
                    bornOverUi.Add(t.touchId);
                }

                if (bornOverUi.Contains(t.touchId))
                {
                    if (ended)
                    {
                        bornOverUi.Remove(t.touchId);
                    }

                    continue;
                }

                samples.Add(new TouchSample(t.touchId, t.screenPosition, began, ended));
            }
        }

        private static bool IsOverUi(int touchId)
        {
            EventSystem system = EventSystem.current;
            return system != null && system.IsPointerOverGameObject(touchId);
        }
    }
}
