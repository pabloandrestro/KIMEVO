using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Kimevo.Diagnostics
{
    /// <summary>
    /// El panel de depuracion. Empieza apagado y se enciende desde el boton "Debug" del HUD.
    ///
    /// Se refresca cuatro veces por segundo y no una vez por frame. La diferencia no es
    /// estetica: reasignar el texto de un TextMeshPro marca su malla como sucia y obliga a
    /// reconstruirla, y este panel tiene una linea por plano detectado. A sesenta fotogramas
    /// por segundo con veinte planos, eso es exactamente el tipo de trabajo por frame que el
    /// brief prohibe, y ademas robaria presupuesto justo al render AR que venimos a medir.
    /// Cuatro veces por segundo se lee igual de bien y cuesta la vigesima parte.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Refrescos por segundo del panel. Subirlo cuesta reconstrucciones de malla de texto.")]
        private float refreshRate = 4f;

        [SerializeField]
        [Tooltip("Cuantos planos se listan como maximo. Mas de esto no cabe en pantalla.")]
        private int maxPlanesListed = 10;

        private ARMetrics metrics;
        private CalibrationCube calibration;
        private ARPlaneManager planeManager;

        private Canvas canvas;
        private TextMeshProUGUI body;
        private Button cubeButton;

        private readonly StringBuilder builder = new StringBuilder(1024);
        private readonly List<ARPlane> sorted = new List<ARPlane>(32);

        private float nextRefreshAt;

        public bool IsOpen => canvas != null && canvas.enabled;

        private void Awake()
        {
            metrics = FindAnyObjectByType<ARMetrics>(FindObjectsInactive.Include);
            calibration = FindAnyObjectByType<CalibrationCube>(FindObjectsInactive.Include);
            planeManager = FindAnyObjectByType<ARPlaneManager>(FindObjectsInactive.Include);

            Build();
            SetOpen(false);
        }

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        public void SetOpen(bool value)
        {
            if (canvas != null)
            {
                canvas.enabled = value;
            }

            if (value)
            {
                // Refresco inmediato: abrir el panel y ver datos de hace un cuarto de segundo
                // hace dudar de si el panel esta vivo.
                nextRefreshAt = 0f;
                Refresh();
            }
        }

        private void Update()
        {
            if (!IsOpen || Time.unscaledTime < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = Time.unscaledTime + (1f / Mathf.Max(refreshRate, 1f));
            Refresh();
        }

        // ---------------------------------------------------------------- contenido

        private void Refresh()
        {
            if (body == null)
            {
                return;
            }

            builder.Clear();

            builder.Append("<b>KIMEVO debug</b>   v").Append(Application.version).Append('\n');

            if (metrics == null)
            {
                builder.Append("ARMetrics ausente: no hay nada que medir.");
                body.text = builder.ToString();
                return;
            }

            builder.Append("sesion  ").Append(metrics.SessionState);
            if (metrics.NotTracking != NotTrackingReason.None)
            {
                builder.Append("  (").Append(metrics.NotTracking).Append(')');
            }
            builder.Append('\n');

            builder.Append("fps     ").Append(Mathf.RoundToInt(metrics.FpsNow))
                   .Append("   avg ").Append(Mathf.RoundToInt(metrics.FpsAvg))
                   .Append("   min ").Append(metrics.FpsMin < float.MaxValue ? Mathf.RoundToInt(metrics.FpsMin).ToString() : "-")
                   .Append('\n');

            builder.Append("TTFP    ").Append(metrics.Ttfp >= 0f ? metrics.Ttfp.ToString("F2") + " s" : "pendiente")
                   .Append("   1er plano ").Append(metrics.TimeToFirstPlane >= 0f ? metrics.TimeToFirstPlane.ToString("F2") + " s" : "-")
                   .Append('\n');

            builder.Append("tracking a los ").Append(metrics.TimeToTracking >= 0f ? metrics.TimeToTracking.ToString("F2") + " s" : "-")
                   .Append("   perdidas ").Append(metrics.TrackingLosses)
                   .Append(" (").Append(metrics.TrackingLostSeconds.ToString("F1")).Append(" s)\n");

            builder.Append("planos  ").Append(metrics.PlaneCount)
                   .Append("   suelo ").Append(metrics.PlanesHorizontalUp)
                   .Append("   pared ").Append(metrics.PlanesVertical)
                   .Append("   techo ").Append(metrics.PlanesHorizontalDown)
                   .Append("   otros ").Append(metrics.PlanesOther)
                   .Append("   ").Append(metrics.TotalPlaneArea.ToString("F2")).Append(" m2\n");

            builder.Append("anchors ").Append(metrics.AnchorCount)
                   .Append("   drift 30s ").Append(metrics.MaxDrift >= 0f ? (metrics.MaxDrift * 100f).ToString("F1") + " cm" : "pendiente")
                   .Append('\n');

            builder.Append("colocar ").Append(metrics.PlaceSuccesses).Append('/').Append(metrics.PlaceAttempts)
                   .Append("   primera ").Append(metrics.HasFirstPlace ? (metrics.FirstPlaceSucceeded ? "ok" : "fallo") : "-")
                   .Append('\n');

            builder.Append("depth   ").Append(metrics.DepthStatus).Append('\n');

            AppendPlaneList();

            body.text = builder.ToString();
        }

        private void AppendPlaneList()
        {
            if (planeManager == null)
            {
                return;
            }

            sorted.Clear();
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane != null && plane.subsumedBy == null)
                {
                    sorted.Add(plane);
                }
            }

            if (sorted.Count == 0)
            {
                return;
            }

            // De mayor a menor: el plano grande es el que decide si hay donde colocar, y es el
            // que interesa ver primero cuando la lista no cabe entera.
            sorted.Sort((a, b) => ARMetrics.PolygonArea(b).CompareTo(ARMetrics.PolygonArea(a)));

            builder.Append("\n<b>planos detectados</b>\n");

            int shown = Mathf.Min(sorted.Count, maxPlanesListed);
            for (int i = 0; i < shown; i++)
            {
                ARPlane plane = sorted[i];
                Vector3 n = plane.normal;

                builder.Append("  ").Append(Label(plane.alignment).PadRight(9))
                       .Append(ARMetrics.PolygonArea(plane).ToString("F2")).Append(" m2")
                       .Append("  n(").Append(n.x.ToString("F2")).Append(',')
                       .Append(n.y.ToString("F2")).Append(',')
                       .Append(n.z.ToString("F2")).Append(")\n");
            }

            if (sorted.Count > shown)
            {
                builder.Append("  ... y ").Append(sorted.Count - shown).Append(" mas\n");
            }
        }

        private static string Label(PlaneAlignment alignment)
        {
            switch (alignment)
            {
                case PlaneAlignment.HorizontalUp: return "suelo";
                case PlaneAlignment.HorizontalDown: return "techo";
                case PlaneAlignment.Vertical: return "pared";
                case PlaneAlignment.NotAxisAligned: return "inclin.";
                default: return "?";
            }
        }

        // ---------------------------------------------------------------- construccion

        private void Build()
        {
            var canvasGo = new GameObject("DebugOverlayCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Por encima del HUD (100): si el panel de depuracion queda debajo de la interfaz
            // que esta depurando, no sirve de nada.
            canvas.sortingOrder = 200;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform panel = NewRect("Panel", (RectTransform)canvasGo.transform);
            panel.anchorMin = new Vector2(0f, 0.30f);
            panel.anchorMax = new Vector2(1f, 0.97f);
            panel.offsetMin = new Vector2(24f, 0f);
            panel.offsetMax = new Vector2(-24f, 0f);

            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.04f, 0.06f, 0.86f);
            bg.raycastTarget = true;

            RectTransform textRect = NewRect("Body", panel);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 96f);
            textRect.offsetMax = new Vector2(-20f, -16f);

            body = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            body.fontSize = 26f;
            body.color = new Color(0.85f, 0.95f, 0.98f, 1f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.raycastTarget = false;
            body.textWrappingMode = TextWrappingModes.NoWrap;

            BuildButtons(panel);
        }

        private void BuildButtons(RectTransform panel)
        {
            RectTransform row = NewRect("Buttons", panel);
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(1f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, 16f);
            row.sizeDelta = new Vector2(-40f, 68f);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            cubeButton = NewButton(row, "Cubo 1 m", () =>
            {
                if (calibration != null)
                {
                    calibration.Toggle();
                }
            });

            NewButton(row, "Volcar log", () =>
            {
                if (metrics != null)
                {
                    metrics.Dump("manual");
                }
            });

            NewButton(row, "Cerrar", () => SetOpen(false));
        }

        private static RectTransform NewRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Button NewButton(RectTransform parent, string label, Action onClick)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.14f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            RectTransform textRect = NewRect("Label", (RectTransform)go.transform);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 26f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            return button;
        }
    }
}
