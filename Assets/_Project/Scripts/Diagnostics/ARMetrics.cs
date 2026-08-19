using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Kimevo.Diagnostics
{
    /// <summary>
    /// Mide la sesion AR para poder comparar un build con el siguiente.
    ///
    /// Existe porque "tarda demasiado en detectar superficies" no es un dato. Sin un numero,
    /// cualquier cambio que hagamos parecera una mejora, y la unica forma de saber si el
    /// coaching de movimiento sirve de algo es tener el TTFP de antes y el de despues medidos
    /// con la MISMA definicion.
    ///
    /// Esa definicion, fijada aqui y a proposito independiente de los umbrales internos de la
    /// app: TTFP es el tiempo desde que la sesion entra en SessionTracking hasta que existe un
    /// plano cuyo poligono real mide al menos <see cref="ValidPlaneArea"/> m2. Se usa el area
    /// del poligono y no la de su caja envolvente porque un plano en L tiene una caja mucho
    /// mayor que su superficie, y eso adelantaria el cronometro sin que haya donde colocar nada.
    ///
    /// Todo se vuelca a logcat con el prefijo [KIMEVO-M], de modo que las metricas se leen por
    /// cable sin depender de que alguien lea bien un overlay mientras mueve el telefono.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class ARMetrics : MonoBehaviour
    {
        /// <summary>Area minima en m2 para que un plano cuente como "superficie util".</summary>
        public const float ValidPlaneArea = 0.10f;

        /// <summary>
        /// Instancia global. Los diagnosticos no deberian obligar al codigo de juego a
        /// declarar una dependencia serializada hacia ellos: si medir cuesta cableado, se
        /// deja de medir. Por eso este es el unico singleton del proyecto.
        /// </summary>
        public static ARMetrics Instance { get; private set; }

        [SerializeField]
        [Tooltip("Cada cuantos segundos se vuelca la linea de metricas a logcat.")]
        private float logInterval = 5f;

        [SerializeField]
        [Tooltip("Segundos iniciales que se ignoran para el FPS minimo. El arranque siempre da un pico que no representa el uso.")]
        private float fpsWarmup = 2.5f;

        [SerializeField]
        [Tooltip("A los cuantos segundos se mide el desplazamiento de cada anchor.")]
        private float driftDelay = 30f;

        private struct AnchorRecord
        {
            public ARAnchor Anchor;
            public Vector3 BornPosition;
            public float BornTime;
            public bool Measured;
        }

        private ARPlaneManager planeManager;
        private ARAnchorManager anchorManager;
        private AROcclusionManager occlusionManager;

        private readonly List<AnchorRecord> anchorRecords = new List<AnchorRecord>(32);
        private readonly StringBuilder builder = new StringBuilder(256);

        private float sceneLoadedAt;
        private float trackingStartedAt = -1f;
        private float lostSince = -1f;
        private float nextLogAt;
        private float nextSurveyAt;

        private float fpsAccum;
        private int fpsFrames;

        // ------------------------------------------------------------------ resultados

        /// <summary>Segundos desde SessionTracking hasta el primer plano util. -1 si aun no ha pasado.</summary>
        public float Ttfp { get; private set; } = -1f;

        /// <summary>Segundos desde SessionTracking hasta el primer plano de cualquier tamano.</summary>
        public float TimeToFirstPlane { get; private set; } = -1f;

        /// <summary>Segundos desde que cargo la escena hasta que la sesion empezo a trackear.</summary>
        public float TimeToTracking { get; private set; } = -1f;

        public int PlanesHorizontalUp { get; private set; }
        public int PlanesHorizontalDown { get; private set; }
        public int PlanesVertical { get; private set; }
        public int PlanesOther { get; private set; }
        public int PlaneCount => PlanesHorizontalUp + PlanesHorizontalDown + PlanesVertical + PlanesOther;

        /// <summary>Superficie util acumulada, en m2, sumando el poligono real de cada plano.</summary>
        public float TotalPlaneArea { get; private set; }

        public float FpsNow { get; private set; }
        public float FpsAvg { get; private set; }
        public float FpsMin { get; private set; } = float.MaxValue;

        /// <summary>Veces que la sesion perdio el tracking despues de haberlo tenido.</summary>
        public int TrackingLosses { get; private set; }

        /// <summary>Segundos totales sin tracking, sumados.</summary>
        public float TrackingLostSeconds { get; private set; }

        public int PlaceAttempts { get; private set; }
        public int PlaceSuccesses { get; private set; }

        /// <summary>
        /// Si la PRIMERA colocacion de la sesion salio a la primera. Es la metrica que pide el
        /// brief - "% de sesiones con colocacion exitosa sin reintento" - y solo tiene sentido
        /// medida una vez por sesion: los intentos posteriores ya son de alguien entrenado.
        /// </summary>
        public bool HasFirstPlace { get; private set; }
        public bool FirstPlaceSucceeded { get; private set; }

        public int AnchorCount { get; private set; }

        /// <summary>Mayor desplazamiento observado de un anchor a los 30s, en metros.</summary>
        public float MaxDrift { get; private set; } = -1f;

        public string DepthStatus { get; private set; } = "sin manager";

        public ARSessionState SessionState => ARSession.state;
        public NotTrackingReason NotTracking => ARSession.notTrackingReason;

        // ------------------------------------------------------------------ ciclo

        private void Awake()
        {
            Instance = this;
            sceneLoadedAt = Time.realtimeSinceStartup;

            planeManager = FindAnyObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
            anchorManager = FindAnyObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
            occlusionManager = FindAnyObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
        }

        private void OnEnable()
        {
            ARSession.stateChanged += OnSessionStateChanged;

            if (planeManager != null) planeManager.trackablesChanged.AddListener(OnPlanesChanged);
            if (anchorManager != null) anchorManager.trackablesChanged.AddListener(OnAnchorsChanged);
        }

        private void OnDisable()
        {
            ARSession.stateChanged -= OnSessionStateChanged;

            if (planeManager != null) planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            if (anchorManager != null) anchorManager.trackablesChanged.RemoveListener(OnAnchorsChanged);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Dump("final");
                Instance = null;
            }
        }

        private void Update()
        {
            // El FPS se muestrea siempre: es la unica metrica que se destruye si la miras a
            // intervalos, porque el minimo vive justo en el frame que te saltarias.
            SampleFps();

            // Todo lo demas va a 4 Hz. Recorrer los planos calculando el area de su poligono
            // sesenta veces por segundo seria exactamente el trabajo por frame que este mismo
            // build existe para medir: el instrumento acabaria falseando su propia lectura.
            if (Time.realtimeSinceStartup < nextSurveyAt)
            {
                return;
            }

            nextSurveyAt = Time.realtimeSinceStartup + 0.25f;

            RecountPlanes();
            MeasureDrift();
            RefreshDepthStatus();

            if (Time.realtimeSinceStartup >= nextLogAt)
            {
                nextLogAt = Time.realtimeSinceStartup + logInterval;
                Dump("tick");
            }
        }

        // ------------------------------------------------------------------ sesion

        private void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            if (args.state == ARSessionState.SessionTracking)
            {
                if (trackingStartedAt < 0f)
                {
                    trackingStartedAt = Time.realtimeSinceStartup;
                    TimeToTracking = trackingStartedAt - sceneLoadedAt;
                    Debug.Log("[KIMEVO-M] tracking iniciado a los " + TimeToTracking.ToString("F2") + "s de cargar la escena");
                }

                if (lostSince > 0f)
                {
                    TrackingLostSeconds += Time.realtimeSinceStartup - lostSince;
                    lostSince = -1f;
                }

                return;
            }

            // Solo cuenta como perdida si antes hubo tracking. Los estados de arranque
            // (CheckingAvailability, SessionInitializing) no son una perdida, son el principio.
            if (trackingStartedAt > 0f && lostSince < 0f)
            {
                TrackingLosses++;
                lostSince = Time.realtimeSinceStartup;
                Debug.Log("[KIMEVO-M] tracking perdido: estado=" + args.state + " motivo=" + ARSession.notTrackingReason);
            }
        }

        // ------------------------------------------------------------------ planos

        private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            if (trackingStartedAt < 0f)
            {
                return;
            }

            if (TimeToFirstPlane < 0f && args.added.Count > 0)
            {
                TimeToFirstPlane = Time.realtimeSinceStartup - trackingStartedAt;
                Debug.Log("[KIMEVO-M] primer plano (cualquier tamano) a los " + TimeToFirstPlane.ToString("F2") + "s");
            }

            if (Ttfp >= 0f)
            {
                return;
            }

            // El TTFP no se decide en added: un plano nace de pocos centimetros y crece.
            // Hay que mirar tambien los actualizados, que es donde alcanza el area util.
            CheckForFirstValidPlane(args.added);
            CheckForFirstValidPlane(args.updated);
        }

        private void CheckForFirstValidPlane(IReadOnlyList<ARPlane> planes)
        {
            if (Ttfp >= 0f || planes == null)
            {
                return;
            }

            for (int i = 0; i < planes.Count; i++)
            {
                ARPlane plane = planes[i];
                if (plane == null)
                {
                    continue;
                }

                float area = PolygonArea(plane);
                if (area < ValidPlaneArea)
                {
                    continue;
                }

                Ttfp = Time.realtimeSinceStartup - trackingStartedAt;
                Debug.Log("[KIMEVO-M] TTFP = " + Ttfp.ToString("F2") + "s  (plano " + plane.alignment
                          + " de " + area.ToString("F2") + " m2)");
                return;
            }
        }

        private void RecountPlanes()
        {
            PlanesHorizontalUp = 0;
            PlanesHorizontalDown = 0;
            PlanesVertical = 0;
            PlanesOther = 0;
            TotalPlaneArea = 0f;

            if (planeManager == null)
            {
                return;
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane == null)
                {
                    continue;
                }

                // Un plano absorbido por otro sigue existiendo como trackable pero ya no es
                // superficie propia. Contarlo inflaria el numero de superficies encontradas.
                if (plane.subsumedBy != null)
                {
                    continue;
                }

                TotalPlaneArea += PolygonArea(plane);

                switch (plane.alignment)
                {
                    case PlaneAlignment.HorizontalUp: PlanesHorizontalUp++; break;
                    case PlaneAlignment.HorizontalDown: PlanesHorizontalDown++; break;
                    case PlaneAlignment.Vertical: PlanesVertical++; break;
                    default: PlanesOther++; break;
                }
            }
        }

        /// <summary>
        /// Area real del poligono del plano, en m2, por la formula del cordon de zapato.
        /// El boundary viene en espacio del plano, asi que el area sale directamente en
        /// metros cuadrados sin transformar nada.
        /// </summary>
        public static float PolygonArea(ARPlane plane)
        {
            if (plane == null)
            {
                return 0f;
            }

            NativeArray<Vector2> boundary = plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3)
            {
                // Sin poligono todavia, la caja envolvente es lo unico que hay.
                return plane.size.x * plane.size.y;
            }

            float twiceArea = 0f;
            for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
            {
                twiceArea += (boundary[j].x * boundary[i].y) - (boundary[i].x * boundary[j].y);
            }

            return Mathf.Abs(twiceArea) * 0.5f;
        }

        // ------------------------------------------------------------------ anchors y drift

        private void OnAnchorsChanged(ARTrackablesChangedEventArgs<ARAnchor> args)
        {
            for (int i = 0; i < args.added.Count; i++)
            {
                ARAnchor anchor = args.added[i];
                if (anchor == null)
                {
                    continue;
                }

                anchorRecords.Add(new AnchorRecord
                {
                    Anchor = anchor,
                    BornPosition = anchor.transform.position,
                    BornTime = Time.realtimeSinceStartup,
                    Measured = false
                });
            }
        }

        private void MeasureDrift()
        {
            AnchorCount = 0;

            for (int i = 0; i < anchorRecords.Count; i++)
            {
                AnchorRecord record = anchorRecords[i];

                if (record.Anchor == null)
                {
                    continue;
                }

                AnchorCount++;

                if (record.Measured || Time.realtimeSinceStartup - record.BornTime < driftDelay)
                {
                    continue;
                }

                // Esto NO es deriva contra el mundo real: sin verdad de campo no hay forma de
                // medir eso desde dentro. Es cuanto ha movido ARCore el anchor en espacio de
                // sesion al refinar su mapa, que es el proxy honesto y el que se nota en
                // pantalla como "el objeto se ha ido de sitio".
                float drift = Vector3.Distance(record.Anchor.transform.position, record.BornPosition);
                if (drift > MaxDrift)
                {
                    MaxDrift = drift;
                }

                record.Measured = true;
                anchorRecords[i] = record;

                Debug.Log("[KIMEVO-M] anchor a los " + driftDelay.ToString("F0") + "s: desplazamiento "
                          + (drift * 100f).ToString("F1") + " cm");
            }
        }

        // ------------------------------------------------------------------ fps y depth

        private void SampleFps()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            FpsNow = 1f / dt;

            fpsAccum += FpsNow;
            fpsFrames++;
            FpsAvg = fpsAccum / fpsFrames;

            if (Time.realtimeSinceStartup - sceneLoadedAt > fpsWarmup && FpsNow < FpsMin)
            {
                FpsMin = FpsNow;
            }
        }

        private void RefreshDepthStatus()
        {
            if (occlusionManager == null)
            {
                occlusionManager = FindAnyObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
            }

            if (occlusionManager == null)
            {
                DepthStatus = "sin manager";
                return;
            }

            if (occlusionManager.descriptor == null)
            {
                DepthStatus = "subsistema no arrancado";
                return;
            }

            bool supported = occlusionManager.descriptor.environmentDepthImageSupported == Supported.Supported;
            DepthStatus = supported
                ? (occlusionManager.enabled ? "soportado y activo" : "soportado, manager apagado")
                : "NO soportado";
        }

        // ------------------------------------------------------------------ colocacion

        /// <summary>Lo llama PlacementController en cada intento de colocar.</summary>
        public void NotePlacement(bool success)
        {
            PlaceAttempts++;
            if (success)
            {
                PlaceSuccesses++;
            }

            if (!HasFirstPlace)
            {
                HasFirstPlace = true;
                FirstPlaceSucceeded = success;
                Debug.Log("[KIMEVO-M] primera colocacion de la sesion: " + (success ? "exito" : "fallo"));
            }
        }

        // ------------------------------------------------------------------ volcado

        /// <summary>Una linea greppable con todo el estado. El formato lo lee un humano y un grep.</summary>
        public string Line()
        {
            builder.Clear();
            builder.Append("ttfp=").Append(Ttfp >= 0f ? Ttfp.ToString("F2") : "-");
            builder.Append(" t1p=").Append(TimeToFirstPlane >= 0f ? TimeToFirstPlane.ToString("F2") : "-");
            builder.Append(" t2track=").Append(TimeToTracking >= 0f ? TimeToTracking.ToString("F2") : "-");
            builder.Append(" planos=").Append(PlaneCount);
            builder.Append(" h=").Append(PlanesHorizontalUp);
            builder.Append(" v=").Append(PlanesVertical);
            builder.Append(" techo=").Append(PlanesHorizontalDown);
            builder.Append(" otros=").Append(PlanesOther);
            builder.Append(" area=").Append(TotalPlaneArea.ToString("F2")).Append("m2");
            builder.Append(" fps=").Append(Mathf.RoundToInt(FpsNow));
            builder.Append(" fpsAvg=").Append(Mathf.RoundToInt(FpsAvg));
            builder.Append(" fpsMin=").Append(FpsMin < float.MaxValue ? Mathf.RoundToInt(FpsMin).ToString() : "-");
            builder.Append(" perdidas=").Append(TrackingLosses);
            builder.Append(" sinTrack=").Append(TrackingLostSeconds.ToString("F1")).Append("s");
            builder.Append(" colocar=").Append(PlaceSuccesses).Append("/").Append(PlaceAttempts);
            builder.Append(" primera=").Append(HasFirstPlace ? (FirstPlaceSucceeded ? "ok" : "fallo") : "-");
            builder.Append(" anchors=").Append(AnchorCount);
            builder.Append(" drift30=").Append(MaxDrift >= 0f ? (MaxDrift * 100f).ToString("F1") + "cm" : "-");
            builder.Append(" depth=").Append(DepthStatus);
            builder.Append(" estado=").Append(SessionState);
            builder.Append(" motivo=").Append(NotTracking);
            builder.Append(" v=").Append(Application.version);
            return builder.ToString();
        }

        public void Dump(string tag)
        {
            Debug.Log("[KIMEVO-M] " + tag + " " + Line());
        }
    }
}
