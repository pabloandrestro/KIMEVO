using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Kimevo.AR;
using Kimevo.Core;
using Kimevo.Interaction;

namespace Kimevo.Placement
{
    /// <summary>
    /// Colocar una forma sobre una superficie: previsualizar, confirmar y anclar.
    ///
    /// El objeto real no cuelga de la escena, cuelga de un ARAnchor. Es la diferencia entre
    /// un objeto que se queda donde lo pusiste y uno que se desliza en cuanto ARCore reconoce
    /// una zona ya vista y reajusta su mapa. Un anchor por objeto colocado, ni uno mas: los
    /// anchors cuestan tracking, y los huerfanos lo cuestan sin dar nada a cambio.
    /// </summary>
    public sealed class PlacementController : MonoBehaviour
    {
        [Header("Dependencias")]
        [SerializeField] private AppModeController modes;
        [SerializeField] private ARSurfaceService surface;
        [SerializeField] private PlacementReticle reticle;
        [SerializeField] private TouchService touch;

        [Header("Formas")]
        [SerializeField]
        [Tooltip("Prefabs de las primitivas del MVP. El orden manda: es el que aparece en la paleta.")]
        private GameObject[] shapePrefabs = new GameObject[0];

        [SerializeField]
        [Tooltip("Nombres visibles de cada forma, en el mismo orden que los prefabs.")]
        private string[] shapeLabels = new string[0];

        [SerializeField]
        [Tooltip("Material translucido del preview. Deja claro que todavia no esta puesto.")]
        private Material previewMaterial;

        [SerializeField]
        [Tooltip("Tamano en metros del lado mayor del objeto colocado. 12 cm cabe en una mesa y se ve de pie.")]
        private float objectSize = 0.12f;

        [Header("Gesto")]
        [SerializeField]
        [Tooltip("Desplazamiento maximo en pixeles para que un toque siga contando como toque y no como arrastre.")]
        private float tapSlop = 40f;

        [SerializeField]
        [Tooltip("Duracion maxima de un toque, en segundos.")]
        private float tapMaxDuration = 0.6f;

        private readonly List<ARAnchor> placedAnchors = new List<ARAnchor>(32);

        private GameObject preview;
        private float previewHalfHeight;
        private int selected;

        private int trackedTouchId = -1;
        private Vector2 trackedTouchStart;
        private float trackedTouchTime;

        public int ShapeCount => shapePrefabs.Length;
        public int Selected => selected;
        public int PlacedCount => placedAnchors.Count;

        public string ShapeLabel(int index)
        {
            if (index >= 0 && index < shapeLabels.Length && !string.IsNullOrEmpty(shapeLabels[index]))
            {
                return shapeLabels[index];
            }

            return index >= 0 && index < shapePrefabs.Length && shapePrefabs[index] != null
                ? shapePrefabs[index].name
                : "?";
        }

        private void Awake()
        {
            if (modes == null) modes = FindAnyObjectByType<AppModeController>(FindObjectsInactive.Include);
            if (surface == null) surface = FindAnyObjectByType<ARSurfaceService>(FindObjectsInactive.Include);
            if (reticle == null) reticle = FindAnyObjectByType<PlacementReticle>(FindObjectsInactive.Include);
            if (touch == null) touch = FindAnyObjectByType<TouchService>(FindObjectsInactive.Include);
        }

        private void OnEnable()
        {
            if (modes != null)
            {
                modes.ModeChanged += OnModeChanged;
            }
        }

        private void OnDisable()
        {
            if (modes != null)
            {
                modes.ModeChanged -= OnModeChanged;
            }
        }

        private void Start()
        {
            SelectShape(0);
            OnModeChanged(modes != null ? modes.Mode : AppMode.Explore);
        }

        private void OnModeChanged(AppMode mode)
        {
            bool active = mode == AppMode.Place;

            if (preview != null)
            {
                preview.SetActive(false);
            }

            if (!active)
            {
                trackedTouchId = -1;
            }
        }

        public void SelectShape(int index)
        {
            if (shapePrefabs.Length == 0)
            {
                return;
            }

            selected = Mathf.Clamp(index, 0, shapePrefabs.Length - 1);
            RebuildPreview();
        }

        private void Update()
        {
            if (modes == null || modes.Mode != AppMode.Place)
            {
                return;
            }

            UpdatePreview();
            UpdateTap();
        }

        private void UpdatePreview()
        {
            if (preview == null)
            {
                return;
            }

            // Sin superficie no hay preview: un objeto flotando en el aire promete una
            // colocacion que despues no se puede cumplir.
            if (!reticle.HasSurface)
            {
                if (preview.activeSelf) preview.SetActive(false);
                return;
            }

            if (!preview.activeSelf)
            {
                preview.SetActive(true);
            }

            Pose pose = reticle.Current.Pose;
            preview.transform.SetPositionAndRotation(
                pose.position + (pose.up * previewHalfHeight),
                pose.rotation);
        }

        private void UpdateTap()
        {
            if (touch == null)
            {
                return;
            }

            IReadOnlyList<TouchService.TouchSample> touches = touch.Touches;

            for (int i = 0; i < touches.Count; i++)
            {
                TouchService.TouchSample sample = touches[i];

                if (sample.Began && trackedTouchId == -1)
                {
                    trackedTouchId = sample.Id;
                    trackedTouchStart = sample.Position;
                    trackedTouchTime = Time.time;
                    continue;
                }

                if (sample.Id != trackedTouchId)
                {
                    continue;
                }

                if (sample.Ended)
                {
                    bool isTap = Vector2.Distance(sample.Position, trackedTouchStart) <= tapSlop
                                 && (Time.time - trackedTouchTime) <= tapMaxDuration;

                    trackedTouchId = -1;

                    if (isTap)
                    {
                        Place();
                    }
                }
            }
        }

        public bool Place()
        {
            if (!reticle.HasPlane)
            {
                Debug.Log("[KIMEVO] Colocar cancelado: no hay plano confirmado bajo la reticula.");
                return false;
            }

            if (shapePrefabs.Length == 0 || shapePrefabs[selected] == null)
            {
                Debug.LogError("[KIMEVO] No hay prefab asignado para la forma " + selected + ".");
                return false;
            }

            SurfaceHit hit = reticle.Current;
            ARAnchor anchor = surface.CreateAnchor(hit);
            if (anchor == null)
            {
                return false;
            }

            GameObject instance = Instantiate(shapePrefabs[selected], anchor.transform);
            instance.transform.localScale = Vector3.one * objectSize;

            // El anchor esta EN la superficie. El objeto se levanta media altura para apoyarse
            // sobre ella en vez de quedar medio hundido; como se usa la normal del plano, esto
            // vale igual para una mesa que para una pared.
            instance.transform.localPosition = Vector3.up * HalfHeightOf(shapePrefabs[selected], objectSize);
            instance.transform.localRotation = Quaternion.identity;

            placedAnchors.Add(anchor);
            Debug.Log("[KIMEVO] Colocado " + ShapeLabel(selected) + ". Total: " + placedAnchors.Count);
            return true;
        }

        public void UndoLast()
        {
            for (int i = placedAnchors.Count - 1; i >= 0; i--)
            {
                ARAnchor anchor = placedAnchors[i];
                placedAnchors.RemoveAt(i);

                if (anchor != null)
                {
                    Destroy(anchor.gameObject);
                    Debug.Log("[KIMEVO] Objeto retirado. Quedan: " + placedAnchors.Count);
                    return;
                }
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < placedAnchors.Count; i++)
            {
                if (placedAnchors[i] != null)
                {
                    Destroy(placedAnchors[i].gameObject);
                }
            }

            placedAnchors.Clear();
            Debug.Log("[KIMEVO] Objetos limpiados.");
        }

        private void RebuildPreview()
        {
            if (preview != null)
            {
                Destroy(preview);
            }

            GameObject prefab = shapePrefabs[selected];
            if (prefab == null)
            {
                return;
            }

            preview = Instantiate(prefab, transform);
            preview.name = "Preview";
            preview.transform.localScale = Vector3.one * objectSize;
            previewHalfHeight = HalfHeightOf(prefab, objectSize);

            // El preview es solo imagen: ni choca, ni proyecta sombra, ni se comporta como
            // objeto real hasta que se confirma.
            foreach (Collider collider in preview.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }

            if (previewMaterial != null)
            {
                foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>())
                {
                    renderer.sharedMaterial = previewMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            preview.SetActive(false);
        }

        /// <summary>
        /// Media altura del prefab ya escalado, leida de la malla. Se calcula asi en vez de
        /// asumir 0.5 porque la capsula de Unity mide 2 unidades y quedaria medio enterrada,
        /// y porque el dia que entren modelos de verdad esto sigue siendo correcto.
        /// </summary>
        private static float HalfHeightOf(GameObject prefab, float scale)
        {
            MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return 0.5f * scale;
            }

            return filter.sharedMesh.bounds.extents.y * scale;
        }
    }
}
