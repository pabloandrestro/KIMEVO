using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Kimevo.AR
{
    /// <summary>
    /// Enciende y apaga el dibujo de los planos detectados.
    ///
    /// Durante el desarrollo es el mejor diagnostico que existe: si no ves planos, no es que
    /// el codigo falle, es que ARCore todavia no ha entendido la habitacion. En produccion
    /// interesa poder apagarlos para que la creacion se lea sobre el mundo real y no sobre
    /// una malla de colores.
    /// </summary>
    public sealed class PlaneVisualizerToggle : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;

        [SerializeField]
        [Tooltip("Estado inicial. Conviene arrancar mostrandolos: son la senal visible de que el tracking funciona.")]
        private bool visible = true;

        public bool Visible => visible;

        private void Awake()
        {
            if (planeManager == null)
            {
                planeManager = FindAnyObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
            }
        }

        private void OnEnable()
        {
            if (planeManager != null)
            {
                // Los planos nacen constantemente. Sin escuchar el evento, cada plano nuevo
                // apareceria visible aunque la persona los hubiera apagado.
                planeManager.trackablesChanged.AddListener(OnTrackablesChanged);
            }

            Apply();
        }

        private void OnDisable()
        {
            if (planeManager != null)
            {
                planeManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
            }
        }

        private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            for (int i = 0; i < args.added.Count; i++)
            {
                SetPlaneVisible(args.added[i], visible);
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;
            Apply();
        }

        public void Toggle()
        {
            SetVisible(!visible);
        }

        private void Apply()
        {
            if (planeManager == null)
            {
                return;
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                SetPlaneVisible(plane, visible);
            }
        }

        private static void SetPlaneVisible(ARPlane plane, bool value)
        {
            if (plane == null)
            {
                return;
            }

            MeshRenderer mesh = plane.GetComponent<MeshRenderer>();
            if (mesh != null) mesh.enabled = value;

            LineRenderer line = plane.GetComponent<LineRenderer>();
            if (line != null) line.enabled = value;
        }
    }
}
