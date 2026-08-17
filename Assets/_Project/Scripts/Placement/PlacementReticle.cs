using UnityEngine;
using Kimevo.AR;

namespace Kimevo.Placement
{
    /// <summary>
    /// La reticula: un anillo que se posa sobre la superficie que hay en el centro de la pantalla.
    ///
    /// Apunta desde el CENTRO y no desde el dedo por dos razones. La mano tapa justo el sitio
    /// donde intentas colocar, y sostener el telefono con una mano mientras apuntas con la otra
    /// es incomodo de pie. Con una reticula central se apunta moviendo el telefono, que ademas
    /// es el gesto que el tracking ya esta pidiendo. Es el patron de las apps de medicion.
    ///
    /// Tambien es el unico sitio que lanza el raycast del centro: los demas leen su resultado,
    /// asi que hay un raycast por frame y no uno por sistema interesado.
    /// </summary>
    public sealed class PlacementReticle : MonoBehaviour
    {
        [SerializeField] private ARSurfaceService surface;

        [Header("Aspecto")]
        [SerializeField] private Material lineMaterial;
        [SerializeField] private float radius = 0.075f;
        [SerializeField] private int segments = 48;
        [SerializeField] private float lineWidth = 0.004f;

        [SerializeField]
        [Tooltip("Superficie confirmada bajo la reticula: se puede colocar y dibujar.")]
        private Color onPlaneColor = new Color(0.05f, 0.78f, 0.78f, 1f);

        [SerializeField]
        [Tooltip("Hay algo, pero no es superficie real: prolongacion de un plano o puntos sueltos. Guia la mirada, no deja anclar.")]
        private Color looseColor = new Color(0.85f, 0.40f, 0.05f, 1f);

        private LineRenderer ring;
        private Transform ringTransform;

        /// <summary>Hubo impacto este frame.</summary>
        public bool HasSurface { get; private set; }

        /// <summary>
        /// El impacto es sobre poligono confirmado. Antes bastaba con que hubiera un plano, y
        /// eso incluia su prolongacion infinita: por eso se podian plantar objetos en el aire.
        /// </summary>
        public bool CanAnchor => HasSurface && Current.CanAnchor;

        public SurfaceHit Current { get; private set; }

        private void Awake()
        {
            if (surface == null)
            {
                surface = FindAnyObjectByType<ARSurfaceService>(FindObjectsInactive.Include);
            }

            BuildRing();
        }

        private void Update()
        {
            if (surface == null)
            {
                return;
            }

            // El centro exacto de la pantalla, no el centro del area segura: es donde la
            // persona ha aprendido a mirar en cuanto ve el anillo la primera vez.
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            if (surface.TryGetSurfaceHit(center, out SurfaceHit hit))
            {
                HasSurface = true;
                Current = hit;

                ringTransform.SetPositionAndRotation(hit.Pose.position, hit.Pose.rotation);
                SetColor(hit.CanAnchor ? onPlaneColor : looseColor);
                Show(true);
            }
            else
            {
                HasSurface = false;
                Current = default;
                Show(false);
            }
        }

        private void BuildRing()
        {
            var go = new GameObject("Reticle");
            go.transform.SetParent(transform, false);
            ringTransform = go.transform;

            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.numCapVertices = 2;
            ring.numCornerVertices = 2;
            ring.widthMultiplier = lineWidth;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.textureMode = LineTextureMode.Stretch;

            if (lineMaterial != null)
            {
                ring.sharedMaterial = lineMaterial;
            }

            // El anillo vive en el plano XZ local. Como la pose de un impacto contra un plano
            // trae su normal en el eje Y, aplicar la rotacion tal cual deja el anillo tumbado
            // sobre la superficie, sea una mesa o una pared.
            ring.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }

            Show(false);
        }

        private void SetColor(Color color)
        {
            // startColor/endColor escriben el color en los vertices de la linea. El material se
            // comparte y no se instancia, que es lo que mantiene el batching intacto.
            ring.startColor = color;
            ring.endColor = color;
        }

        private void Show(bool value)
        {
            if (ring != null && ring.enabled != value)
            {
                ring.enabled = value;
            }
        }
    }
}
