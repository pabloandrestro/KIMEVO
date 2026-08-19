using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kimevo.UI
{
    /// <summary>
    /// El laboratorio de la barra de modos. Escena aparte, sin AR y sin sesion que esperar.
    ///
    /// Existe por una razon de tiempo: iterar el movimiento de la barra dentro de la app
    /// cuesta un APK de veinte minutos y una habitacion con superficies. Aqui se entra en Play
    /// y se ve en dos segundos.
    ///
    /// Lo que de verdad justifica la escena es el fondo conmutable. La barra vive encima de
    /// video que no elegimos, y un neon fino que se lee precioso sobre negro puede
    /// desaparecer sobre una pared blanca a pleno sol. Sobre fondo negro - como la referencia
    /// de diseno - todo se ve bien y no se descubre nada. Por eso hay un fondo claro y otro
    /// ruidoso: son los que dicen la verdad.
    /// </summary>
    public sealed class UIPlaygroundBoot : MonoBehaviour
    {
        private enum Backdrop
        {
            Negro = 0,
            GrisMedio = 1,
            BlancoQuemado = 2,
            EscenaRuidosa = 3
        }

        private static readonly string[] BackdropLabels =
        {
            "negro",
            "gris medio",
            "blanco quemado",
            "escena ruidosa"
        };

        private ModeBar bar;
        private Image backdrop;
        private TextMeshProUGUI status;
        private Backdrop currentBackdrop = Backdrop.Negro;
        private Sprite noiseSprite;
        private bool drawLocked;

        private void Start()
        {
            var canvasGo = new GameObject("PlaygroundCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var root = (RectTransform)canvasGo.transform;

            BuildBackdrop(root);
            BuildControls(root);

            // La barra se cuelga del mismo canvas, igual que hara dentro de la app. Si aqui
            // usara canvas propio y alli no, el laboratorio estaria probando otra cosa.
            var barGo = new GameObject("ModeBar");
            barGo.transform.SetParent(transform, false);
            bar = barGo.AddComponent<ModeBar>();
            bar.Build(root);
            bar.ModeSelected += OnModeSelected;

            ApplyBackdrop();
            RefreshStatus();
        }

        private void OnModeSelected(int index)
        {
            Debug.Log("[KIMEVO] Laboratorio: modo " + index);
            RefreshStatus();
        }

        // ---------------------------------------------------------------- fondo

        private void BuildBackdrop(RectTransform root)
        {
            RectTransform rect = NewRect("Backdrop", root);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            backdrop = rect.gameObject.AddComponent<Image>();
            backdrop.raycastTarget = false;

            noiseSprite = BuildNoiseSprite();
        }

        private void ApplyBackdrop()
        {
            switch (currentBackdrop)
            {
                case Backdrop.Negro:
                    backdrop.sprite = null;
                    backdrop.color = Color.black;
                    break;

                case Backdrop.GrisMedio:
                    backdrop.sprite = null;
                    backdrop.color = new Color(0.5f, 0.5f, 0.52f, 1f);
                    break;

                case Backdrop.BlancoQuemado:
                    backdrop.sprite = null;
                    // No blanco puro: una pared al sol en una camara de movil se va a este
                    // entorno, con algo de calidez y sin llegar a saturar del todo.
                    backdrop.color = new Color(0.96f, 0.95f, 0.92f, 1f);
                    break;

                default:
                    backdrop.sprite = noiseSprite;
                    backdrop.color = Color.white;
                    break;
            }
        }

        // ---------------------------------------------------------------- controles

        private void BuildControls(RectTransform root)
        {
            RectTransform panel = NewRect("Controls", root);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -40f);
            panel.sizeDelta = new Vector2(-48f, 210f);

            status = NewLabel(panel, string.Empty, 28f);
            status.rectTransform.anchorMin = new Vector2(0f, 1f);
            status.rectTransform.anchorMax = new Vector2(1f, 1f);
            status.rectTransform.pivot = new Vector2(0.5f, 1f);
            status.rectTransform.anchoredPosition = Vector2.zero;
            status.rectTransform.sizeDelta = new Vector2(0f, 60f);
            status.alignment = TextAlignmentOptions.Center;

            NewButton(panel, "Cambiar fondo", new Vector2(0f, -70f), () =>
            {
                currentBackdrop = (Backdrop)(((int)currentBackdrop + 1) % 4);
                ApplyBackdrop();
                RefreshStatus();
            });

            NewButton(panel, "Bloquear/soltar Dibujar", new Vector2(0f, -140f), () =>
            {
                drawLocked = !drawLocked;
                bar.SetInteractable(2, !drawLocked);
                RefreshStatus();
            });
        }

        private void RefreshStatus()
        {
            if (status == null)
            {
                return;
            }

            status.text = "fondo: " + BackdropLabels[(int)currentBackdrop]
                          + "   ·   modo: " + bar.Current
                          + "   ·   dibujar: " + (drawLocked ? "bloqueado" : "libre");

            // El texto de control se pinta del color opuesto al fondo para poder leerlo
            // mientras se prueba el contraste de lo que si importa, que es la barra.
            bool darkBackdrop = currentBackdrop == Backdrop.Negro || currentBackdrop == Backdrop.EscenaRuidosa;
            status.color = darkBackdrop ? Color.white : Color.black;
        }

        // ---------------------------------------------------------------- utilidades

        private static RectTransform NewRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI NewLabel(RectTransform parent, string text, float size)
        {
            RectTransform rect = NewRect("Label", parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            return label;
        }

        private static void NewButton(RectTransform parent, string label, Vector2 position, Action onClick)
        {
            RectTransform rect = NewRect(label, parent);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(560f, 60f);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.25f, 0.27f, 0.32f, 0.92f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            TextMeshProUGUI text = NewLabel(rect, label, 26f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.color = Color.white;
        }

        /// <summary>
        /// Un fondo que imita lo que ve la camara: degradado con manchas y grano. No pretende
        /// ser bonito, pretende tener bordes y frecuencias medias que compitan con el trazo,
        /// que es donde un neon fino se pierde de verdad.
        /// </summary>
        private static Sprite BuildNoiseSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;

                    float baseTone = Mathf.Lerp(0.22f, 0.78f, v);
                    float blobs = Mathf.PerlinNoise(u * 4.5f, v * 4.5f) * 0.35f;
                    float grain = Mathf.PerlinNoise(u * 48f, v * 48f) * 0.12f;

                    float tone = Mathf.Clamp01(baseTone + blobs - 0.15f + grain);

                    pixels[(y * size) + x] = new Color32(
                        (byte)(tone * 255f),
                        (byte)(tone * 248f),
                        (byte)(tone * 235f),
                        255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
