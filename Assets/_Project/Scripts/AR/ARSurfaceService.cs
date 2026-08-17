using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Kimevo.AR
{
    /// <summary>Resultado de preguntarle al mundo que hay bajo un punto de la pantalla.</summary>
    public readonly struct SurfaceHit
    {
        public readonly Pose Pose;

        /// <summary>Plano impactado, o null si el impacto fue sobre un punto suelto de la nube.</summary>
        public readonly ARPlane Plane;

        public bool IsPlane => Plane != null;

        public SurfaceHit(Pose pose, ARPlane plane)
        {
            Pose = pose;
            Plane = plane;
        }
    }

    /// <summary>
    /// El unico punto del proyecto que habla con el raycast de AR Foundation.
    ///
    /// No envuelve a ARRaycastManager ni a ARPlaneManager: esos ya son los managers, y
    /// duplicarlos solo crea una capa que no decide nada y se desincroniza con cada version
    /// del paquete. Lo que centraliza es la POLITICA, que es lo unico que de verdad es
    /// nuestro: contra que se lanza el raycast y en que orden, cuando un plano es demasiado
    /// pequeno para fiarse de el, y a partir de que distancia un impacto deja de ser creible.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class ARSurfaceService : MonoBehaviour
    {
        [Header("Managers (se buscan solos si se dejan vacios)")]
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARAnchorManager anchorManager;

        [Header("Politica")]
        [SerializeField]
        [Tooltip("Area minima en m2 para aceptar un plano. Los planos recien nacidos son de pocos centimetros y bailan; colocar sobre ellos produce objetos que saltan.")]
        private float minPlaneArea = 0.04f;

        [SerializeField]
        [Tooltip("Distancia maxima en metros. Mas alla de esto la estimacion de ARCore deja de ser fiable y el objeto acaba en cualquier sitio.")]
        private float maxDistance = 6f;

        [SerializeField]
        [Tooltip("Aceptar puntos sueltos de la nube cuando no hay ningun plano. Sirve para que la reticula de senal de vida, pero sobre ellos no se ancla nada.")]
        private bool allowFeaturePoints = true;

        private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>(8);

        public ARPlaneManager Planes => planeManager;
        public ARAnchorManager Anchors => anchorManager;

        /// <summary>Cuantos planos conoce la sesion ahora mismo. Solo para diagnostico y UI.</summary>
        public int PlaneCount => planeManager != null ? planeManager.trackables.count : 0;

        private void Awake()
        {
            if (raycastManager == null) raycastManager = FindAnyObjectByType<ARRaycastManager>(FindObjectsInactive.Include);
            if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
            if (anchorManager == null) anchorManager = FindAnyObjectByType<ARAnchorManager>(FindObjectsInactive.Include);

            if (raycastManager == null) Debug.LogError("[KIMEVO] Falta ARRaycastManager en el XR Origin.");
            if (planeManager == null) Debug.LogError("[KIMEVO] Falta ARPlaneManager en el XR Origin.");
            if (anchorManager == null) Debug.LogError("[KIMEVO] Falta ARAnchorManager en el XR Origin.");
        }

        /// <summary>
        /// Que hay bajo el punto de pantalla indicado. Prueba en tres pasos, de mas honesto a
        /// mas permisivo, y devuelve el primero que se sostenga.
        /// </summary>
        public bool TryGetSurfaceHit(Vector2 screenPoint, out SurfaceHit hit)
        {
            // 1. Dentro del poligono real del plano. Es el unico impacto sobre superficie confirmada.
            if (TryRaycast(screenPoint, TrackableType.PlaneWithinPolygon, true, out hit))
            {
                return true;
            }

            // 2. El plano extendido al infinito. ARCore tarda en crecer los bordes: la mesa ya
            //    esta detectada pero su poligono todavia no llega al borde real. Sin este paso
            //    la reticula se apaga justo donde la persona quiere colocar.
            if (TryRaycast(screenPoint, TrackableType.PlaneWithinInfinity, true, out hit))
            {
                return true;
            }

            // 3. Todavia no hay ningun plano. Un punto suelto no sirve para anclar, pero sirve
            //    para que la reticula se pose en algo y la app no parezca rota mientras ARCore
            //    reune informacion.
            if (allowFeaturePoints && TryRaycast(screenPoint, TrackableType.FeaturePoint, false, out hit))
            {
                return true;
            }

            hit = default;
            return false;
        }

        /// <summary>
        /// Crea el anchor de una creacion. Devuelve null si el impacto no era sobre un plano:
        /// anclar a un punto suelto no existe en ARCore, y fingir que si con un Transform
        /// suelto produce contenido que se despega en cuanto la sesion refina su mapa.
        /// </summary>
        public ARAnchor CreateAnchor(SurfaceHit hit)
        {
            if (!hit.IsPlane)
            {
                Debug.LogWarning("[KIMEVO] Se ha pedido un anchor sobre un impacto que no es un plano. No se crea.");
                return null;
            }

            if (anchorManager == null || !anchorManager.enabled)
            {
                Debug.LogError("[KIMEVO] ARAnchorManager ausente o desactivado: no se puede anclar.");
                return null;
            }

            ARAnchor anchor = anchorManager.AttachAnchor(hit.Plane, hit.Pose);
            if (anchor == null)
            {
                Debug.LogWarning("[KIMEVO] AttachAnchor ha devuelto null. El plano puede haber dejado de existir.");
            }

            return anchor;
        }

        private bool TryRaycast(Vector2 screenPoint, TrackableType types, bool requirePlane, out SurfaceHit hit)
        {
            hit = default;

            if (raycastManager == null || !raycastManager.enabled)
            {
                return false;
            }

            if (!raycastManager.Raycast(screenPoint, Hits, types))
            {
                return false;
            }

            // Los impactos vienen ordenados de cerca a lejos: el primero que pase el filtro vale.
            for (int i = 0; i < Hits.Count; i++)
            {
                ARRaycastHit candidate = Hits[i];

                if (candidate.distance > maxDistance)
                {
                    continue;
                }

                ARPlane plane = candidate.trackable as ARPlane;

                if (requirePlane)
                {
                    if (plane == null || plane.size.x * plane.size.y < minPlaneArea)
                    {
                        continue;
                    }

                    hit = new SurfaceHit(candidate.pose, plane);
                    return true;
                }

                hit = new SurfaceHit(candidate.pose, null);
                return true;
            }

            return false;
        }
    }
}
