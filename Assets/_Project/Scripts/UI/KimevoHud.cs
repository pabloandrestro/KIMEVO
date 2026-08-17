using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Kimevo.AR;
using Kimevo.Core;
using Kimevo.Drawing;
using Kimevo.Placement;

namespace Kimevo.UI
{
    /// <summary>
    /// La interfaz de la experiencia, construida por codigo.
    ///
    /// Se monta en runtime a proposito. Este build existe para validar mecanicas en un
    /// telefono real, y una UI cableada a mano en la escena significa una referencia rota
    /// silenciosa por cada cosa que se mueva. Ademas asi la interfaz entera vive en git como
    /// texto revisable, en vez de dentro del binario de la escena.
    ///
    /// Cuando las mecanicas esten validadas, esto se sustituye por una UI de verdad con su
    /// arte. Hasta entonces, que funcione y no mienta es suficiente.
    /// </summary>
    public sealed class KimevoHud : MonoBehaviour
    {
        [Header("Dependencias")]
        [SerializeField] private AppModeController modes;
        [SerializeField] private ARSurfaceService surface;
        [SerializeField] private PlacementReticle reticle;
        [SerializeField] private PlacementController placement;
        [SerializeField] private DrawingController drawing;
        [SerializeField] private PlaneVisualizerToggle planeToggle;

        [Header("Aspecto")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1080f, 1920f);
        [SerializeField] private float rowHeight = 132f;
        [SerializeField] private float margin = 24f;

        private static readonly Color Ink = new Color(0.086f, 0.094f, 0.122f, 1f);
        private static readonly Color Panel = new Color(1f, 1f, 1f, 0.92f);
        private static readonly Color PanelDim = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color Magenta = new Color(0.839f, 0.169f, 0.388f, 1f);
        private static readonly Color Scrim = new Color(0.05f, 0.06f, 0.08f, 0.60f);

        private RectTransform safeRoot;
        private TextMeshProUGUI hintText;
        private TextMeshProUGUI diagText;

        private Button[] modeButtons;
        private Button[] shapeButtons;
        private Button[] colorButtons;
        private Button[] widthButtons;

        private GameObject placeRow;
        private GameObject drawRow;
        private GameObject actionRow;
        private Button undoButton;
        private Button clearButton;
        private Button planesButton;

        private Rect lastSafeArea;
        private readonly StringBuilder builder = new StringBuilder(160);

        private void Awake()
        {
            if (modes == null) modes = FindAnyObjectByType<AppModeController>(FindObjectsInactive.Include);
            if (surface == null) surface = FindAnyObjectByType<ARSurfaceService>(FindObjectsInactive.Include);
            if (reticle == null) reticle = FindAnyObjectByType<PlacementReticle>(FindObjectsInactive.Include);
            if (placement == null) placement = FindAnyObjectByType<PlacementController>(FindObjectsInactive.Include);
            if (drawing == null) drawing = FindAnyObjectByType<DrawingController>(FindObjectsInactive.Include);
            if (planeToggle == null) planeToggle = FindAnyObjectByType<PlaneVisualizerToggle>(FindObjectsInactive.Include);

            Build();
        }

        private void Update()
        {
            ApplySafeArea();
            RefreshRows();
            RefreshHint();
            RefreshDiagnostics();
        }

        // ---------------------------------------------------------------- construccion

        private void Build()
        {
            var canvasGo = new GameObject("KimevoHudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            safeRoot = NewRect("SafeArea", (RectTransform)canvasGo.transform);
            safeRoot.anchorMin = Vector2.zero;
            safeRoot.anchorMax = Vector2.one;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;

            BuildHint();
            BuildModeRow();
            BuildPlaceRow();
            BuildDrawRow();
            BuildActionRow();
            BuildDiagnostics();
        }

        private void BuildHint()
        {
            RectTransform pill = NewRect("Hint", safeRoot);
            pill.anchorMin = new Vector2(0f, 1f);
            pill.anchorMax = new Vector2(1f, 1f);
            pill.pivot = new Vector2(0.5f, 1f);
            pill.anchoredPosition = new Vector2(0f, -margin);
            pill.sizeDelta = new Vector2(-margin * 2f, 96f);

            Image bg = pill.gameObject.AddComponent<Image>();
            bg.color = Scrim;
            bg.raycastTarget = false;

            hintText = NewText(pill, string.Empty, 34f, Color.white, TextAlignmentOptions.Center);
        }

        private void BuildModeRow()
        {
            RectTransform row = NewRow("ModeRow", margin);
            string[] labels = { "Explorar", "Colocar", "Dibujar" };
            modeButtons = new Button[labels.Length];

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                modeButtons[i] = NewButton(row, labels[i], Panel, Ink, 34f, () => modes.SetModeIndex(index));
            }
        }

        private void BuildPlaceRow()
        {
            RectTransform row = NewRow("PlaceRow", margin + rowHeight + 12f);
            placeRow = row.gameObject;

            int count = placement != null ? placement.ShapeCount : 0;
            shapeButtons = new Button[count];

            for (int i = 0; i < count; i++)
            {
                int index = i;
                shapeButtons[i] = NewButton(row, placement.ShapeLabel(i), Panel, Ink, 30f,
                    () => placement.SelectShape(index));
            }
        }

        private void BuildDrawRow()
        {
            RectTransform row = NewRow("DrawRow", margin + rowHeight + 12f);
            drawRow = row.gameObject;

            int colors = drawing != null ? drawing.ColorCount : 0;
            colorButtons = new Button[colors];

            for (int i = 0; i < colors; i++)
            {
                int index = i;
                colorButtons[i] = NewButton(row, string.Empty, drawing.ColorAt(i), Color.white, 28f,
                    () => drawing.SetColor(index));
            }

            int widths = drawing != null ? drawing.WidthCount : 0;
            widthButtons = new Button[widths];
            string[] widthLabels = { "fino", "medio", "grueso" };

            for (int i = 0; i < widths; i++)
            {
                int index = i;
                string label = i < widthLabels.Length ? widthLabels[i] : (i + 1).ToString();
                widthButtons[i] = NewButton(row, label, Panel, Ink, 26f, () => drawing.SetWidth(index));
            }
        }

        private void BuildActionRow()
        {
            RectTransform row = NewRow("ActionRow", margin + (rowHeight + 12f) * 2f);
            actionRow = row.gameObject;

            undoButton = NewButton(row, "Deshacer", PanelDim, Ink, 30f, OnUndo);
            clearButton = NewButton(row, "Limpiar", PanelDim, Ink, 30f, OnClear);
            planesButton = NewButton(row, "Planos", PanelDim, Ink, 30f, () =>
            {
                if (planeToggle != null) planeToggle.Cycle();
            });
        }

        private void BuildDiagnostics()
        {
            RectTransform rect = NewRect("Diagnostics", safeRoot);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -(margin + 96f + 8f));
            rect.sizeDelta = new Vector2(-margin * 2f, 44f);

            diagText = NewText(rect, string.Empty, 24f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Center);
        }

        // ---------------------------------------------------------------- estado

        private void RefreshRows()
        {
            AppMode mode = modes != null ? modes.Mode : AppMode.Explore;

            SetActive(placeRow, mode == AppMode.Place);
            SetActive(drawRow, mode == AppMode.Draw);
            SetActive(actionRow, mode != AppMode.Explore);

            for (int i = 0; i < modeButtons.Length; i++)
            {
                Tint(modeButtons[i], (int)mode == i ? Magenta : Panel, (int)mode == i ? Color.white : Ink);
            }

            if (planesButton != null && planeToggle != null)
            {
                SetLabel(planesButton, "Planos: " + planeToggle.DisplayLabel);
            }

            if (mode == AppMode.Place && placement != null)
            {
                for (int i = 0; i < shapeButtons.Length; i++)
                {
                    Tint(shapeButtons[i], placement.Selected == i ? Magenta : Panel,
                        placement.Selected == i ? Color.white : Ink);
                }
            }

            if (mode == AppMode.Draw && drawing != null)
            {
                for (int i = 0; i < colorButtons.Length; i++)
                {
                    // El color del boton ES el color del trazo, asi que la seleccion no puede
                    // marcarse cambiandolo. Se marca con tamano.
                    RectTransform rt = (RectTransform)colorButtons[i].transform;
                    rt.localScale = Vector3.one * (drawing.ColorIndex == i ? 1f : 0.78f);
                }

                for (int i = 0; i < widthButtons.Length; i++)
                {
                    Tint(widthButtons[i], drawing.WidthIndex == i ? Magenta : Panel,
                        drawing.WidthIndex == i ? Color.white : Ink);
                }
            }
        }

        private void RefreshHint()
        {
            if (hintText == null)
            {
                return;
            }

            ARSessionState state = ARSession.state;
            if (state != ARSessionState.SessionTracking)
            {
                hintText.text = state == ARSessionState.SessionInitializing
                    ? "Mueve el telefono despacio para que AR entienda el sitio"
                    : "AR: " + state;
                return;
            }

            int planes = surface != null ? surface.PlaneCount : 0;
            if (planes == 0)
            {
                hintText.text = "Mueve el telefono despacio sobre una mesa o el suelo";
                return;
            }

            switch (modes != null ? modes.Mode : AppMode.Explore)
            {
                case AppMode.Place:
                    if (reticle == null || !reticle.HasSurface)
                    {
                        hintText.text = "Apunta con el centro a una superficie";
                    }
                    else if (reticle.CanAnchor)
                    {
                        hintText.text = "Toca la pantalla para colocar";
                    }
                    else
                    {
                        // La reticula esta naranja: hay plano, pero solo su prolongacion.
                        // Decirlo evita que la persona insista tocando sin que pase nada.
                        hintText.text = "Ahi la superficie aun no llega. Apunta mas cerca del centro de la mesa";
                    }
                    break;

                case AppMode.Draw:
                    if (drawing != null && drawing.HasCanvas)
                    {
                        hintText.text = "Arrastra el dedo para dibujar";
                    }
                    else
                    {
                        hintText.text = "Apoya el dedo sobre una superficie y arrastra";
                    }
                    break;

                default:
                    hintText.text = planes + (planes == 1 ? " superficie encontrada" : " superficies encontradas")
                                    + ". Elige Colocar o Dibujar.";
                    break;
            }
        }

        private void RefreshDiagnostics()
        {
            if (diagText == null)
            {
                return;
            }

            builder.Clear();
            builder.Append("planos ").Append(surface != null ? surface.PlaneCount : 0);
            builder.Append("  ·  objetos ").Append(placement != null ? placement.PlacedCount : 0);
            builder.Append("  ·  trazos ").Append(drawing != null ? drawing.StrokeCount : 0);
            builder.Append("  ·  ").Append(Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f))).Append(" fps");
            // La version va aqui y no en una pantalla de ajustes: sirve para responder "¿esto
            // es el build nuevo?" sin cable y sin creerse a nadie.
            builder.Append("  ·  v").Append(Application.version);

            diagText.text = builder.ToString();
        }

        private void OnUndo()
        {
            AppMode mode = modes != null ? modes.Mode : AppMode.Explore;

            if (mode == AppMode.Draw && drawing != null) drawing.Undo();
            else if (mode == AppMode.Place && placement != null) placement.UndoLast();
        }

        private void OnClear()
        {
            AppMode mode = modes != null ? modes.Mode : AppMode.Explore;

            if (mode == AppMode.Draw && drawing != null) drawing.Clear();
            else if (mode == AppMode.Place && placement != null) placement.ClearAll();
        }

        // ---------------------------------------------------------------- utilidades

        private void ApplySafeArea()
        {
            Rect area = Screen.safeArea;
            if (area == lastSafeArea || safeRoot == null)
            {
                return;
            }

            lastSafeArea = area;

            var min = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            var max = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);

            safeRoot.anchorMin = min;
            safeRoot.anchorMax = max;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        private RectTransform NewRow(string name, float bottom)
        {
            RectTransform rect = NewRect(name, safeRoot);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = new Vector2(-margin * 2f, rowHeight);

            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            return rect;
        }

        private static RectTransform NewRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Button NewButton(RectTransform parent, string label, Color background, Color foreground,
            float fontSize, Action onClick)
        {
            var go = new GameObject(string.IsNullOrEmpty(label) ? "Swatch" : label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.color = background;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            if (!string.IsNullOrEmpty(label))
            {
                NewText((RectTransform)go.transform, label, fontSize, foreground, TextAlignmentOptions.Center);
            }

            return button;
        }

        private static TextMeshProUGUI NewText(RectTransform parent, string content, float size, Color color,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = NewRect("Label", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;

            return text;
        }

        private static void Tint(Button button, Color background, Color foreground)
        {
            if (button == null)
            {
                return;
            }

            var image = button.targetGraphic as Image;
            if (image != null) image.color = background;

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = foreground;
        }

        private static void SetLabel(Button button, string text)
        {
            if (button == null)
            {
                return;
            }

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null && label.text != text)
            {
                label.text = text;
            }
        }

        private static void SetActive(GameObject go, bool value)
        {
            if (go != null && go.activeSelf != value)
            {
                go.SetActive(value);
            }
        }
    }
}
