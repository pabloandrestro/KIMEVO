using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Kimevo.AR;
using Kimevo.Core;
using Kimevo.Interaction;

namespace Kimevo.Drawing
{
    /// <summary>
    /// Dibujar sobre una superficie. Aqui si manda el dedo directamente sobre la pantalla,
    /// porque el gesto de dibujar es tocar.
    ///
    /// Dos decisiones que conviene entender antes de tocar nada:
    ///
    /// UN SOLO ANCHOR POR DIBUJO. El primer contacto crea el anchor y bajo el un DrawingRoot;
    /// todos los trazos siguientes cuelgan del mismo. Un anchor por trazo produciria un dibujo
    /// que se descuartiza solo, porque cada anchor se recoloca por su cuenta cuando la sesion
    /// refina su mapa.
    ///
    /// LOS PUNTOS NO SALEN DEL RAYCAST DE AR. Solo el primero. Despues cada muestra se obtiene
    /// cortando el rayo del dedo contra el plano MATEMATICO del anchor, recalculado cada frame
    /// desde su transform. Es mas estable y mas barato: si el raycast de AR falla un frame o el
    /// dedo se sale del poligono detectado, el trazo daria un salto visible en mitad de la
    /// linea. Cortando contra el plano del anchor el trazo queda siempre coplanar, y como el
    /// plano se deriva del anchor, sigue al anchor cuando ARCore lo recoloca.
    /// </summary>
    public sealed class DrawingController : MonoBehaviour
    {
        [Header("Dependencias")]
        [SerializeField] private AppModeController modes;
        [SerializeField] private ARSurfaceService surface;
        [SerializeField] private TouchService touch;
        [SerializeField] private Camera arCamera;

        [Header("Trazo")]
        [SerializeField] private Material strokeMaterial;

        [SerializeField]
        [Tooltip("Separacion sobre la superficie, en metros. Sin este margen el trazo pelea con la pared por el mismo pixel y parpadea.")]
        private float surfaceOffset = 0.006f;

        [SerializeField]
        [Tooltip("Distancia minima entre puntos, en metros.")]
        private float minPointDistance = 0.012f;

        [SerializeField]
        [Tooltip("Grosores disponibles, en metros.")]
        private float[] widths = { 0.006f, 0.012f, 0.022f };

        [SerializeField]
        [Tooltip("Paleta de colores. Los cuatro primeros son los de la marca.")]
        private Color[] palette =
        {
            new Color(0.839f, 0.169f, 0.388f, 1f),
            new Color(0.851f, 0.400f, 0.031f, 1f),
            new Color(0.039f, 0.561f, 0.561f, 1f),
            new Color(0.380f, 0.251f, 0.839f, 1f),
            new Color(0.960f, 0.960f, 0.960f, 1f)
        };

        [SerializeField]
        [Tooltip("Distancia maxima de dibujo en metros: mas alla, el dedo apunta a una pared que ya no es la que empezaste.")]
        private float maxDrawDistance = 6f;

        [SerializeField]
        [Tooltip("Techo de trazos por dibujo. Cada trazo es una llamada de dibujado, y el telefono lo nota.")]
        private int maxStrokes = 60;

        private readonly List<StrokeRenderer> strokes = new List<StrokeRenderer>(64);

        private ARAnchor anchor;
        private Transform drawingRoot;
        private StrokeRenderer active;
        private int activeTouchId = -1;

        private int colorIndex;
        private int widthIndex = 1;

        public int ColorCount => palette.Length;
        public int WidthCount => widths.Length;
        public int ColorIndex => colorIndex;
        public int WidthIndex => widthIndex;
        public int StrokeCount => strokes.Count;
        public bool HasCanvas => anchor != null && drawingRoot != null;
        public bool IsDrawing => active != null;

        public Color ColorAt(int index) => palette[Mathf.Clamp(index, 0, palette.Length - 1)];

        private void Awake()
        {
            if (modes == null) modes = FindAnyObjectByType<AppModeController>(FindObjectsInactive.Include);
            if (surface == null) surface = FindAnyObjectByType<ARSurfaceService>(FindObjectsInactive.Include);
            if (touch == null) touch = FindAnyObjectByType<TouchService>(FindObjectsInactive.Include);

            if (arCamera == null)
            {
                XROrigin origin = FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
                arCamera = origin != null && origin.Camera != null ? origin.Camera : Camera.main;
            }

            if (arCamera == null)
            {
                Debug.LogError("[KIMEVO] DrawingController no encuentra la camara AR: sin ella no hay rayo que cortar.");
            }
        }

        private void OnEnable()
        {
            if (modes != null) modes.ModeChanged += OnModeChanged;
        }

        private void OnDisable()
        {
            if (modes != null) modes.ModeChanged -= OnModeChanged;
        }

        private void OnModeChanged(AppMode mode)
        {
            if (mode != AppMode.Draw)
            {
                EndStroke();
            }
        }

        private void Update()
        {
            if (modes == null || modes.Mode != AppMode.Draw || touch == null)
            {
                return;
            }

            IReadOnlyList<TouchService.TouchSample> touches = touch.Touches;

            for (int i = 0; i < touches.Count; i++)
            {
                TouchService.TouchSample sample = touches[i];

                if (sample.Began && activeTouchId == -1)
                {
                    BeginStroke(sample);
                    continue;
                }

                if (sample.Id != activeTouchId)
                {
                    continue;
                }

                if (sample.Ended)
                {
                    EndStroke();
                    continue;
                }

                Continue(sample);
            }
        }

        private void BeginStroke(TouchService.TouchSample sample)
        {
            if (!EnsureCanvas(sample.Position))
            {
                return;
            }

            if (strokes.Count >= maxStrokes)
            {
                Debug.LogWarning("[KIMEVO] Limite de trazos alcanzado (" + maxStrokes + ").");
                return;
            }

            var go = new GameObject("Stroke " + strokes.Count);
            go.transform.SetParent(drawingRoot, false);

            active = go.AddComponent<StrokeRenderer>();
            active.Init(strokeMaterial, ColorAt(colorIndex), widths[Mathf.Clamp(widthIndex, 0, widths.Length - 1)]);

            activeTouchId = sample.Id;

            if (TrySample(sample.Position, out Vector3 local))
            {
                active.TryAddPoint(local, 0f);
            }
        }

        private void Continue(TouchService.TouchSample sample)
        {
            if (active == null)
            {
                return;
            }

            if (TrySample(sample.Position, out Vector3 local))
            {
                active.TryAddPoint(local, minPointDistance);
            }
        }

        private void EndStroke()
        {
            if (active != null)
            {
                if (active.IsMeaningful)
                {
                    strokes.Add(active);
                    Debug.Log("[KIMEVO] Trazo cerrado con " + active.PointCount + " puntos. Total: " + strokes.Count);
                }
                else
                {
                    // Un toque suelto no es un trazo. Guardarlo llenaria la pila de deshacer
                    // de fantasmas invisibles.
                    Destroy(active.gameObject);
                }
            }

            active = null;
            activeTouchId = -1;
        }

        /// <summary>
        /// Asegura que existe el lienzo: un anchor sobre un plano real y un DrawingRoot debajo.
        /// Solo se crea una vez por dibujo, en el primer contacto.
        /// </summary>
        private bool EnsureCanvas(Vector2 screenPoint)
        {
            if (HasCanvas)
            {
                return true;
            }

            if (surface == null || !surface.TryGetSurfaceHit(screenPoint, out SurfaceHit hit) || !hit.CanAnchor)
            {
                Debug.Log("[KIMEVO] No se puede empezar a dibujar: bajo el dedo no hay poligono confirmado.");
                return false;
            }

            anchor = surface.CreateAnchor(hit);
            if (anchor == null)
            {
                return false;
            }

            var root = new GameObject("DrawingRoot");
            root.transform.SetParent(anchor.transform, false);
            drawingRoot = root.transform;

            Debug.Log("[KIMEVO] Lienzo creado sobre el plano " + hit.Plane.trackableId + ".");
            return true;
        }

        /// <summary>
        /// Un punto del trazo, en el espacio local del dibujo. Corta el rayo del dedo contra
        /// el plano del anchor y separa el resultado unos milimetros a lo largo de la normal.
        /// </summary>
        private bool TrySample(Vector2 screenPoint, out Vector3 localPoint)
        {
            localPoint = default;

            if (!HasCanvas || arCamera == null)
            {
                return false;
            }

            // El plano se recalcula cada muestra desde el transform del anchor. Cachearlo seria
            // mas barato y estaria mal: cuando ARCore recoloca el anchor, un plano cacheado se
            // queda donde estaba y el dibujo se separa de la pared.
            Vector3 normal = anchor.transform.up;
            var mathPlane = new Plane(normal, anchor.transform.position);

            Ray ray = arCamera.ScreenPointToRay(screenPoint);

            // Devuelve false si el rayo apunta al otro lado, que es justo lo que evita dibujar
            // detras de uno mismo al girar el telefono.
            if (!mathPlane.Raycast(ray, out float distance) || distance > maxDrawDistance)
            {
                return false;
            }

            Vector3 world = ray.GetPoint(distance) + (normal * surfaceOffset);
            localPoint = drawingRoot.InverseTransformPoint(world);
            return true;
        }

        public void SetColor(int index)
        {
            colorIndex = Mathf.Clamp(index, 0, palette.Length - 1);
        }

        public void SetWidth(int index)
        {
            widthIndex = Mathf.Clamp(index, 0, widths.Length - 1);
        }

        public void Undo()
        {
            EndStroke();

            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                StrokeRenderer stroke = strokes[i];
                strokes.RemoveAt(i);

                if (stroke != null)
                {
                    Destroy(stroke.gameObject);
                    Debug.Log("[KIMEVO] Trazo deshecho. Quedan: " + strokes.Count);
                    return;
                }
            }
        }

        public void Clear()
        {
            EndStroke();

            for (int i = 0; i < strokes.Count; i++)
            {
                if (strokes[i] != null)
                {
                    Destroy(strokes[i].gameObject);
                }
            }

            strokes.Clear();

            // Se libera tambien el anchor: sin trazos no representa nada, y un anchor vivo
            // sigue costando tracking. El siguiente contacto creara uno nuevo donde toque.
            if (anchor != null)
            {
                Destroy(anchor.gameObject);
            }

            anchor = null;
            drawingRoot = null;
            Debug.Log("[KIMEVO] Dibujo limpiado y anchor liberado.");
        }
    }
}
