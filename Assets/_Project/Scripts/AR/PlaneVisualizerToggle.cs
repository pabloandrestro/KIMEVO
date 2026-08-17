using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Kimevo.AR
{
    /// <summary>
    /// Como se dibujan los planos detectados.
    ///
    /// Arranca en contorno por una razon medida: el relleno translucido se acumula. Con alfa
    /// 0.22 un plano solo es discreto, pero lo que atraviesa N planos superpuestos es
    /// 0.78^N; con cinco encima ya solo pasa el 29% del mundo real y con veintitres, el 0.3%.
    /// En una habitacion normal ARCore encuentra veintitantos planos en un minuto, asi que el
    /// relleno deja de ser un matiz y se convierte en una lamina opaca que tapa justo lo que
    /// hay que juzgar. El contorno da la misma informacion - donde hay superficie y hasta
    /// donde llega - sin acumular nada.
    /// </summary>
    public sealed class PlaneVisualizerToggle : MonoBehaviour
    {
        public enum PlaneDisplay
        {
            /// <summary>Solo el borde. Se ve donde hay superficie y el mundo real sigue visible.</summary>
            Outline = 0,

            /// <summary>Borde y relleno. Util para depurar solapes, incomodo para mirar.</summary>
            OutlineAndFill = 1,

            /// <summary>Nada. Para juzgar una creacion sin andamiaje alrededor.</summary>
            Hidden = 2
        }

        [SerializeField] private ARPlaneManager planeManager;

        [SerializeField]
        [Tooltip("Estado inicial. Contorno: se ve la superficie sin que el relleno tape la habitacion.")]
        private PlaneDisplay display = PlaneDisplay.Outline;

        public PlaneDisplay Display => display;

        /// <summary>Etiqueta corta para la interfaz.</summary>
        public string DisplayLabel
        {
            get
            {
                switch (display)
                {
                    case PlaneDisplay.Outline: return "Borde";
                    case PlaneDisplay.OutlineAndFill: return "Lleno";
                    default: return "Off";
                }
            }
        }

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
                // apareceria con el aspecto por defecto en vez de con el elegido.
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
                ApplyTo(args.added[i]);
            }
        }

        public void SetDisplay(PlaneDisplay value)
        {
            display = value;
            Apply();
        }

        /// <summary>Avanza al siguiente estado. Es lo que dispara el boton de la interfaz.</summary>
        public void Cycle()
        {
            SetDisplay((PlaneDisplay)(((int)display + 1) % 3));
            Debug.Log("[KIMEVO] Planos: " + display);
        }

        private void Apply()
        {
            if (planeManager == null)
            {
                return;
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                ApplyTo(plane);
            }
        }

        private void ApplyTo(ARPlane plane)
        {
            if (plane == null)
            {
                return;
            }

            bool outline = display != PlaneDisplay.Hidden;
            bool fill = display == PlaneDisplay.OutlineAndFill;

            MeshRenderer mesh = plane.GetComponent<MeshRenderer>();
            if (mesh != null) mesh.enabled = fill;

            LineRenderer line = plane.GetComponent<LineRenderer>();
            if (line != null) line.enabled = outline;
        }
    }
}
