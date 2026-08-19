using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kimevo.UI
{
    /// <summary>
    /// La barra de tres modos: Explorar, Colocar, Dibujar.
    ///
    /// El momento que importa es la transicion. Cuando se cambia de modo el liquido no se
    /// desvanece en uno y aparece en otro: el que dejas se vacia mientras el que eliges se
    /// llena, y los dos tramos SE SOLAPAN en el tiempo. Ese solape es todo el truco. Con dos
    /// fades independientes el ojo ve dos sucesos; con el solape ve uno solo, y lo lee como
    /// que el liquido ha pasado de un circulo al otro. Por eso el punto indicador viaja
    /// horizontalmente a la vez: da la trayectoria que el liquido no puede dibujar fuera de
    /// su circulo.
    ///
    /// Todo lo demas de la barra se mantiene deliberadamente quieto. El presupuesto de
    /// animacion se gasta entero aqui.
    ///
    /// No se usa ningun LayoutGroup. Colocar tres circulos de tamano fijo no necesita un
    /// sistema de layout, y un LayoutGroup invalida su rect cada vez que algo dentro cambia,
    /// que es precisamente el coste por frame que esta barra tiene prohibido pagar.
    /// </summary>
    public sealed class ModeBar : MonoBehaviour
    {
        [Header("Medidas en dp")]
        [SerializeField]
        [Tooltip("Diametro del circulo. El brief pide 60-64dp.")]
        private float buttonDp = 62f;

        [SerializeField]
        [Tooltip("Separacion entre circulos.")]
        private float gapDp = 14f;

        [SerializeField]
        [Tooltip("Area tactil minima. Si el circulo es menor, el area de toque se agranda igual.")]
        private float minTouchDp = 44f;

        [SerializeField]
        [Tooltip("Margen desde el borde inferior del area segura.")]
        private float bottomMarginDp = 12f;

        [SerializeField]
        [Tooltip("Alto reservado a la etiqueta bajo el boton activo.")]
        private float captionDp = 26f;

        [Header("Movimiento")]
        [SerializeField]
        [Tooltip("Duracion de la transicion entre modos, en segundos.")]
        private float transitionDuration = 0.46f;

        [SerializeField]
        [Tooltip("Segundos que se muestran las tres etiquetas al arrancar. Despues solo la del modo activo.")]
        private float onboardingSeconds = 4.5f;

        [Header("Rendimiento")]
        [SerializeField]
        [Tooltip("Por debajo de estos fps la barra congela los bucles de los iconos.")]
        private float degradeBelowFps = 50f;

        [SerializeField]
        [Tooltip("Por encima de estos fps los vuelve a encender. La distancia con el umbral de bajada evita que parpadee en la frontera.")]
        private float restoreAboveFps = 56f;

        private static readonly string[] Labels = { "Explorar", "Colocar", "Dibujar" };

        private RectTransform root;
        private RectTransform row;
        private ModeButton[] buttons;
        private RectTransform[] slots;
        private RectTransform indicator;
        private TextMeshProUGUI[] captions;

        private Canvas canvas;
        private Sprite dotSprite;

        private int current;
        private int previous;
        private float transitionT = 1f;
        private bool transitioning;

        private float startedAt;
        private bool onboarding = true;

        private float smoothedFps = 60f;
        private bool degraded;

        private Rect lastSafeArea;
        private float lastScaleFactor;
        private Vector2 lastScreen;

        /// <summary>Modo seleccionado por el usuario.</summary>
        public event Action<int> ModeSelected;

        public int Current => current;

        /// <summary>
        /// Alto que ocupa la barra medido DESDE EL BORDE INFERIOR DEL AREA SEGURA, en unidades
        /// de canvas. Es lo que necesita el HUD para colocar sus filas justo encima sin
        /// solaparse. No incluye el area segura en si porque el HUD ya vive dentro de ella:
        /// sumarla contaria el notch dos veces.
        /// </summary>
        public float OccupiedHeight { get; private set; }

        // ---------------------------------------------------------------- ciclo

        /// <summary>
        /// Construye la barra. Si se pasa un padre, se cuelga de el; si no, crea su propio
        /// Canvas. Lo primero es lo que se usa dentro de la app, donde ya hay un Canvas de HUD
        /// y no tiene sentido pagar otro. Lo segundo es lo que usa la escena de laboratorio.
        /// </summary>
        public void Build(RectTransform parent)
        {
            if (parent == null)
            {
                var canvasGo = new GameObject("ModeBarCanvas", typeof(RectTransform));
                canvasGo.transform.SetParent(transform, false);

                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 120;

                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<GraphicRaycaster>();
                parent = (RectTransform)canvasGo.transform;
            }
            else
            {
                canvas = parent.GetComponentInParent<Canvas>();
            }

            root = NewRect("ModeBar", parent);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            row = NewRect("Row", root);
            row.anchorMin = new Vector2(0.5f, 0f);
            row.anchorMax = new Vector2(0.5f, 0f);
            row.pivot = new Vector2(0.5f, 0f);

            Shader shader = Shader.Find("KIMEVO/ModeButton");
            if (shader == null)
            {
                Debug.LogError("[KIMEVO] No se encuentra el shader KIMEVO/ModeButton. "
                               + "Si esto pasa en un build, hay que anadirlo a Always Included Shaders.");
            }

            dotSprite = BuildDotSprite();

            buttons = new ModeButton[3];
            slots = new RectTransform[3];
            captions = new TextMeshProUGUI[3];

            for (int i = 0; i < 3; i++)
            {
                slots[i] = NewRect("Slot" + i, row);
                slots[i].anchorMin = new Vector2(0.5f, 0f);
                slots[i].anchorMax = new Vector2(0.5f, 0f);
                slots[i].pivot = new Vector2(0.5f, 0f);

                var image = slots[i].gameObject.AddComponent<Image>();
                image.sprite = null;

                var button = slots[i].gameObject.AddComponent<ModeButton>();
                // Un tercio de ciclo de desfase entre botones: los tres iconos respiran, pero
                // no a la vez.
                button.Init(i, shader, i * 2.7f);
                button.Clicked += OnButtonClicked;
                buttons[i] = button;

                captions[i] = NewCaption(row, Labels[i]);
            }

            indicator = NewRect("Indicator", row);
            indicator.anchorMin = new Vector2(0.5f, 0f);
            indicator.anchorMax = new Vector2(0.5f, 0f);
            indicator.pivot = new Vector2(0.5f, 0.5f);

            var dot = indicator.gameObject.AddComponent<Image>();
            dot.sprite = dotSprite;
            dot.raycastTarget = false;
            dot.color = Color.white;

            startedAt = Time.unscaledTime;

            buttons[0].SetActivation(1f);
            current = 0;
            previous = 0;
        }

        private void LateUpdate()
        {
            if (root == null)
            {
                return;
            }

            TrackFrameRate();
            ApplyLayoutIfNeeded();
            AdvanceTransition();
            UpdateCaptions();
        }

        // ---------------------------------------------------------------- estado

        /// <summary>Cambia de modo con la transicion completa. Lo usa el codigo externo.</summary>
        public void SetMode(int index)
        {
            index = Mathf.Clamp(index, 0, 2);

            if (index == current)
            {
                return;
            }

            previous = current;
            current = index;
            transitionT = 0f;
            transitioning = true;
            onboarding = false;
        }

        public void SetInteractable(int index, bool value)
        {
            if (buttons != null && index >= 0 && index < buttons.Length)
            {
                buttons[index].SetInteractable(value);
            }
        }

        private void OnButtonClicked(int index)
        {
            SetMode(index);
            ModeSelected?.Invoke(index);
        }

        private void AdvanceTransition()
        {
            if (!transitioning)
            {
                return;
            }

            transitionT += Time.unscaledDeltaTime / Mathf.Max(transitionDuration, 0.05f);

            if (transitionT >= 1f)
            {
                transitionT = 1f;
                transitioning = false;
            }

            // El que se va empieza a vaciarse de inmediato y termina al 62% del recorrido.
            float drain = Mathf.Clamp01(transitionT / 0.62f);
            // El que llega empieza al 28%, con el otro aun vaciandose. Ese solape del 34% es
            // lo que se lee como trasvase en vez de como dos fundidos.
            float fill = Mathf.Clamp01((transitionT - 0.28f) / 0.72f);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (i == current)
                {
                    buttons[i].SetActivation(EaseOut(fill));
                }
                else if (i == previous)
                {
                    buttons[i].SetActivation(1f - EaseIn(drain));
                }
                else
                {
                    buttons[i].SetActivation(0f);
                }
            }

            if (indicator != null && slots != null)
            {
                float x = Mathf.Lerp(slots[previous].anchoredPosition.x,
                                     slots[current].anchoredPosition.x,
                                     EaseInOut(transitionT));
                Vector2 pos = indicator.anchoredPosition;
                indicator.anchoredPosition = new Vector2(x, pos.y);
            }
        }

        private void UpdateCaptions()
        {
            if (captions == null)
            {
                return;
            }

            if (onboarding && Time.unscaledTime - startedAt > onboardingSeconds)
            {
                onboarding = false;
            }

            for (int i = 0; i < captions.Length; i++)
            {
                if (captions[i] == null)
                {
                    continue;
                }

                bool visible = onboarding || i == current;

                // Se activa el GameObject en vez de tocar el alfa del texto: cambiar el color
                // de un TextMeshPro lo marca sucio y reconstruye su malla cada frame.
                if (captions[i].gameObject.activeSelf != visible)
                {
                    captions[i].gameObject.SetActive(visible);
                }
            }
        }

        // ---------------------------------------------------------------- rendimiento

        private void TrackFrameRate()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                smoothedFps = Mathf.Lerp(smoothedFps, 1f / dt, 0.05f);
            }

            if (!degraded && smoothedFps < degradeBelowFps)
            {
                degraded = true;
                SetMotion(0f);
                Debug.Log("[KIMEVO] Barra de modos: animaciones congeladas a "
                          + Mathf.RoundToInt(smoothedFps) + " fps.");
            }
            else if (degraded && smoothedFps > restoreAboveFps)
            {
                degraded = false;
                SetMotion(1f);
                Debug.Log("[KIMEVO] Barra de modos: animaciones restauradas a "
                          + Mathf.RoundToInt(smoothedFps) + " fps.");
            }
        }

        private void SetMotion(float value)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].SetMotion(value);
            }
        }

        // ---------------------------------------------------------------- layout

        /// <summary>
        /// Recoloca solo cuando algo ha cambiado de verdad: area segura, resolucion o factor
        /// de escala del canvas. Mover RectTransforms es de lo poco que aqui si ensucia el
        /// canvas, asi que se hace una vez y no cada frame.
        /// </summary>
        private void ApplyLayoutIfNeeded()
        {
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            Rect safe = Screen.safeArea;
            var screen = new Vector2(Screen.width, Screen.height);

            if (Mathf.Approximately(scale, lastScaleFactor)
                && safe == lastSafeArea
                && screen == lastScreen)
            {
                return;
            }

            lastScaleFactor = scale;
            lastSafeArea = safe;
            lastScreen = screen;

            if (scale <= 0f)
            {
                return;
            }

            // De dp a unidades de referencia del canvas. Se pasa por canvas.scaleFactor porque
            // es lo unico que sabe de verdad como el CanvasScaler ha resuelto la mezcla entre
            // ancho y alto; calcularlo a mano desde la resolucion de referencia da un tamano
            // equivocado en cuanto la pantalla no tiene la proporcion supuesta.
            float pxPerDp = DensityScale() / 1f;
            float unitsPerDp = pxPerDp / scale;

            float button = buttonDp * unitsPerDp;
            float gap = gapDp * unitsPerDp;
            float touch = Mathf.Max(button, minTouchDp * unitsPerDp);
            float bottomMargin = bottomMarginDp * unitsPerDp;

            // El area segura en unidades de canvas: aqui es donde se respeta la barra de
            // gestos de Android.
            float safeBottom = safe.yMin / scale;

            float captionHeight = captionDp * unitsPerDp;

            // El punto indicador vive POR ENCIMA de los circulos, asi que forma parte del
            // alto ocupado aunque no este dentro de ningun boton. Sin reservarle sitio se
            // colaba por debajo de la fila que el HUD coloca justo encima y se solapaba con
            // ella.
            float indicatorZone = 14f * unitsPerDp;
            float rowHeight = touch + captionHeight + (6f * unitsPerDp) + indicatorZone;

            row.sizeDelta = new Vector2((touch * 3f) + (gap * 2f), rowHeight);
            row.anchoredPosition = new Vector2(0f, safeBottom + bottomMargin);

            OccupiedHeight = rowHeight + bottomMargin;

            float step = touch + gap;
            float firstX = -step;

            for (int i = 0; i < 3; i++)
            {
                slots[i].sizeDelta = new Vector2(touch, touch);
                slots[i].anchoredPosition = new Vector2(firstX + (step * i), captionHeight);

                // Mas ancho que el circulo: "Explorar" es mas largo que su boton y el rect no
                // debe recortarlo aunque no se permita el salto de linea.
                captions[i].rectTransform.sizeDelta = new Vector2(step * 1.8f, captionHeight);
                captions[i].rectTransform.anchoredPosition = new Vector2(firstX + (step * i), 0f);
                captions[i].fontSize = 19f * unitsPerDp;
            }

            float dotSize = 9f * unitsPerDp;
            indicator.sizeDelta = new Vector2(dotSize, dotSize);
            indicator.anchoredPosition = new Vector2(slots[current].anchoredPosition.x,
                                                     captionHeight + touch + (7f * unitsPerDp));

            // El brief da la camara por prioritaria y le reserva el 82% de la pantalla. Si la
            // barra se sale de su 18% conviene enterarse aqui y no mirando capturas.
            float usedFraction = (rowHeight + bottomMargin + safeBottom) * scale / Mathf.Max(Screen.height, 1f);
            if (usedFraction > 0.18f)
            {
                // Se avisa una sola vez por layout, no por frame, y diciendo contra que
                // viewport se ha medido: en un Game view corto la fraccion sale inflada
                // aunque en el telefono, que es mucho mas alto, quede holgada.
                Debug.LogWarning("[KIMEVO] La barra de modos ocupa el "
                                 + Mathf.RoundToInt(usedFraction * 100f) + "% inferior de un viewport de "
                                 + Screen.width + "x" + Screen.height + ", por encima del 18% previsto.");
            }
        }

        // ---------------------------------------------------------------- utilidades

        /// <summary>
        /// Pixeles por dp.
        ///
        /// En un dispositivo real manda Screen.dpi, que es lo unico que da dp de verdad, y los
        /// dp importan: el minimo de 44dp de area tactil es una medida fisica del dedo, no una
        /// proporcion de la pantalla.
        ///
        /// Pero Screen.dpi no siempre sirve. En el editor devuelve la densidad del monitor, y
        /// algunos Android devuelven cero o un valor inventado. El primer intento fue asumir
        /// 420 dpi en esos casos, y salio mal: en un Game view de 400 px de ancho un boton de
        /// 62dp ocupaba 163 px y los tres se salian de la pantalla. Asumir la densidad de un
        /// telefono solo funciona si la pantalla tambien es la de un telefono.
        ///
        /// El respaldo correcto es proporcional: se supone una pantalla de 360dp de ancho, que
        /// es el ancho canonico de un movil Android. Asi la barra guarda la misma proporcion
        /// en un Game view pequeno que en un telefono, que es justo lo que un laboratorio de
        /// interfaz tiene que enseniar.
        /// </summary>
        private static float DensityScale()
        {
            float dpi = Screen.dpi;

            if (!Application.isEditor && dpi >= 200f && dpi <= 800f)
            {
                return dpi / 160f;
            }

            return Mathf.Max(Screen.width, 1) / 360f;
        }

        private static float EaseOut(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float EaseIn(float t) => t * t;
        private static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

        private static RectTransform NewRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private TextMeshProUGUI NewCaption(RectTransform parent, string text)
        {
            RectTransform rect = NewRect("Caption", parent);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = KimevoPalette.Idle;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            // "Explorar" no cabe en el ancho de un circulo y se partia en dos lineas.
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;

            // Contorno oscuro por el mismo motivo que el halo del shader: esta etiqueta cae
            // encima del video de la camara, y texto claro sobre una pared blanca no se lee.
            // El contorno cuesta una instancia de material por etiqueta y resuelve los dos
            // extremos sin tener que elegir un color de texto que funcione en ninguno.
            Material outlined = label.fontMaterial;
            outlined.EnableKeyword(ShaderUtilities.Keyword_Outline);
            // 0.32 era demasiado: el contorno se comia el relleno y las letras salian huecas,
            // como tipografia de cartel. 0.18 marca el borde y deja ver el color del texto.
            outlined.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
            outlined.SetColor(ShaderUtilities.ID_OutlineColor, KimevoPalette.Ink);

            return label;
        }

        /// <summary>
        /// El punto indicador, generado en caliente. Un circulo blanco de 32 px no merece un
        /// PNG en el repositorio ni una entrada en el atlas.
        /// </summary>
        private static Sprite BuildDotSprite()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float radius = half - 1.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float a = Mathf.Clamp01(radius - d);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
