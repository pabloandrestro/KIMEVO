using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kimevo.UI
{
    /// <summary>
    /// Un boton circular de modo.
    ///
    /// La regla que gobierna toda la clase: despues de construirse, este componente NUNCA
    /// toca una propiedad de su Graphic. Ni color, ni sprite, ni el RectTransform. Cambiar
    /// cualquiera de esas cosas marca el Graphic como sucio y encola una reconstruccion del
    /// Canvas entero; con la barra animandose en bucle eso seria un Canvas.Rebuild por frame
    /// peleandose con el render AR por el mismo presupuesto.
    ///
    /// En vez de eso todo se comunica al shader por floats del material, que no ensucian nada.
    /// Incluida la pulsacion: el encogimiento al 94% lo hace el shader escalando su espacio de
    /// dibujo, no el transform.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class ModeButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private static readonly int NeonId = Shader.PropertyToID("_Neon");
        private static readonly int InkId = Shader.PropertyToID("_Ink");
        private static readonly int ActivationId = Shader.PropertyToID("_Activation");
        private static readonly int PressId = Shader.PropertyToID("_Press");
        private static readonly int DisabledId = Shader.PropertyToID("_Disabled");
        private static readonly int IconId = Shader.PropertyToID("_IconId");
        private static readonly int MotionId = Shader.PropertyToID("_Motion");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        /// <summary>Indice de modo (0 explorar, 1 colocar, 2 dibujar).</summary>
        public int Index { get; private set; }

        public bool Interactable { get; private set; } = true;

        /// <summary>Se dispara al soltar dentro del boton, si esta disponible.</summary>
        public event Action<int> Clicked;

        private Image image;
        private Material material;

        private float activation;
        private float press;
        private float pressTarget;

        public float Activation => activation;

        public void Init(int index, Shader shader, float seed)
        {
            Index = index;

            image = GetComponent<Image>();

            // Una instancia de material por boton. Son tres materiales y por tanto tres
            // llamadas de dibujado en vez de una; se acepta a cambio de que cada boton tenga
            // su propio color, su propia fase y su propio nivel de liquido. Con
            // MaterialPropertyBlock no valdria: uGUI no las respeta al construir sus lotes.
            material = new Material(shader);
            material.SetColor(NeonId, KimevoPalette.ForMode(index));
            material.SetColor(InkId, KimevoPalette.Ink);
            material.SetFloat(IconId, index);
            material.SetFloat(MotionId, 1f);

            // Cada boton arranca en un punto distinto de su bucle. Con la misma fase los tres
            // laten a la vez y el resultado parece un semaforo, no tres objetos vivos.
            material.SetFloat(SeedId, seed);

            image.material = material;

            // El Image necesita ser opaco al raycast para recibir toques, pero su color no
            // pinta nada: todo el dibujo sale del shader.
            image.color = Color.white;
            image.raycastTarget = true;

            Apply();
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        /// <summary>Nivel de llenado, 0 vacio y 1 lleno. Lo conduce ModeBar durante la transicion.</summary>
        public void SetActivation(float value)
        {
            activation = Mathf.Clamp01(value);
            if (material != null)
            {
                material.SetFloat(ActivationId, activation);
            }
        }

        /// <summary>Cantidad de animacion de bucle. A cero los iconos se quedan quietos.</summary>
        public void SetMotion(float value)
        {
            if (material != null)
            {
                material.SetFloat(MotionId, Mathf.Clamp01(value));
            }
        }

        public void SetInteractable(bool value)
        {
            Interactable = value;
            if (material != null)
            {
                material.SetFloat(DisabledId, value ? 0f : 1f);
            }
        }

        private void Update()
        {
            if (Mathf.Approximately(press, pressTarget))
            {
                return;
            }

            // Entra rapido y sale algo mas lento. Simetrico se siente pegajoso al soltar.
            float speed = pressTarget > press ? 22f : 12f;
            press = Mathf.MoveTowards(press, pressTarget, Time.unscaledDeltaTime * speed);

            if (material != null)
            {
                material.SetFloat(PressId, press);
            }
        }

        private void Apply()
        {
            if (material == null)
            {
                return;
            }

            material.SetFloat(ActivationId, activation);
            material.SetFloat(PressId, press);
            material.SetFloat(DisabledId, Interactable ? 0f : 1f);
        }

        // ---------------------------------------------------------------- toques

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable)
            {
                return;
            }

            pressTarget = 1f;
            Haptics.Tap();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressTarget = 0f;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactable)
            {
                return;
            }

            Clicked?.Invoke(Index);
        }
    }
}
