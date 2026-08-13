using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace Kimevo.Core
{
    /// <summary>
    /// Animacion de apertura de KIMEVO y puerta de entrada a la experiencia AR.
    ///
    /// Se anima con Image.fillAmount y RectTransform, sin Animator ni librerias externas:
    /// la secuencia es lineal y de un solo disparo, asi que una maquina de estados solo
    /// anadiria piezas que mantener. Las curvas son campos serializados, de modo que el
    /// easing se ajusta desde el Inspector con el editor de curvas de Unity.
    ///
    /// Mientras el logo anima se comprueba si el dispositivo soporta AR y se precarga
    /// ARWorld. Al terminar: o entra a la experiencia, o explica por que no puede.
    /// </summary>
    public sealed class SplashController : MonoBehaviour
    {
        [Header("Piezas del logo")]
        [SerializeField] private CanvasGroup logoRoot;
        [SerializeField] private Image wordmark;
        [SerializeField] private Image circle;
        [SerializeField] private RectTransform diamond;
        [SerializeField] private CanvasGroup diamondGroup;
        [SerializeField] private Image bar;

        [Header("Mensaje de incompatibilidad")]
        [SerializeField] private CanvasGroup unsupportedPanel;

        [SerializeField]
        [Tooltip("Solo para pruebas: fuerza el camino de dispositivo incompatible sin preguntar a ARCore. Util porque CheckAvailability puede devolver Ready en telefonos no certificados que tengan Play Services for AR instalado por fuera.")]
        private bool debugForceUnsupported;

        [Header("Tiempos (segundos)")]
        [SerializeField] private float wordmarkStart = 0.00f;
        [SerializeField] private float wordmarkDuration = 0.55f;
        [SerializeField] private float circleStart = 0.45f;
        [SerializeField] private float circleDuration = 0.40f;
        [SerializeField] private float diamondStart = 0.70f;
        [SerializeField] private float diamondDuration = 0.35f;
        [SerializeField] private float barStart = 0.95f;
        [SerializeField] private float barDuration = 0.25f;
        [SerializeField] private float restUntil = 1.55f;
        [SerializeField] private float exitDuration = 0.45f;

        [Header("Easing")]
        [SerializeField]
        private AnimationCurve easeOut = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2.6f), new Keyframe(1f, 1f, 0f, 0f));

        [SerializeField]
        private AnimationCurve overshoot = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 3.2f), new Keyframe(0.62f, 1.05f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("Salida")]
        [SerializeField] private string nextScene = "ARWorld";
        [SerializeField] private float exitScale = 1.03f;

        [SerializeField]
        [Tooltip("Margen antes de lanzar la comprobacion de AR y la carga de ARWorld. Deja que la animacion este ya en pantalla para que su coste no se vea.")]
        private float heavyWorkDelay = 0.30f;

        private const float DiamondStartAngle = -45f;
        private const float DiamondStartScale = 0.45f;

        private bool arSupported;

        private IEnumerator Start()
        {
            ResetToStart();

            // Dos frames para que la pantalla pinte el estado inicial antes de tocar nada.
            yield return null;
            yield return null;

            // La animacion arranca PRIMERO. Antes se lanzaban aqui la comprobacion de AR y
            // la carga de ARWorld, y su coste en el hilo principal retrasaba el primer
            // fotograma: se veian dos segundos de blanco quieto antes de que apareciera nada.
            var intro = StartCoroutine(PlayIntro());

            yield return new WaitForSeconds(heavyWorkDelay);

            // Con el logo ya moviendose, el trabajo pesado deja de percibirse.
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            StartCoroutine(CheckArSupport());

            AsyncOperation load = null;
            if (!string.IsNullOrEmpty(nextScene))
            {
                load = SceneManager.LoadSceneAsync(nextScene);
                load.allowSceneActivation = false;
            }

            yield return intro;

            // Si la comprobacion aun no ha respondido, esperamos aqui y no antes.
            while (!checkFinished)
            {
                yield return null;
            }

            if (!arSupported)
            {
                if (load != null)
                {
                    load.allowSceneActivation = false;
                }
                yield return ShowUnsupported();
                yield break;
            }

            yield return FadeOut();

            if (load != null)
            {
                load.allowSceneActivation = true;
            }
        }

        private void ResetToStart()
        {
            if (logoRoot != null)
            {
                logoRoot.alpha = 1f;
                logoRoot.transform.localScale = Vector3.one * BaseScale();
            }
            if (wordmark != null) { wordmark.type = Image.Type.Filled; wordmark.fillMethod = Image.FillMethod.Horizontal; wordmark.fillOrigin = (int)Image.OriginHorizontal.Left; wordmark.fillAmount = 0f; }
            if (circle != null) { circle.type = Image.Type.Filled; circle.fillMethod = Image.FillMethod.Radial360; circle.fillOrigin = (int)Image.Origin360.Top; circle.fillAmount = 0f; }
            if (bar != null) { bar.type = Image.Type.Filled; bar.fillMethod = Image.FillMethod.Vertical; bar.fillOrigin = (int)Image.OriginVertical.Bottom; bar.fillAmount = 0f; }
            if (diamondGroup != null) { diamondGroup.alpha = 0f; }
            if (diamond != null)
            {
                diamond.localRotation = Quaternion.Euler(0f, 0f, DiamondStartAngle);
                diamond.localScale = Vector3.one * DiamondStartScale;
            }
            if (unsupportedPanel != null)
            {
                unsupportedPanel.alpha = 0f;
                unsupportedPanel.gameObject.SetActive(false);
            }
        }

        private float BaseScale()
        {
            return logoRoot != null ? logoRoot.transform.localScale.x : 1f;
        }

        private IEnumerator PlayIntro()
        {
            float t = 0f;
            float baseScale = logoRoot != null ? logoRoot.transform.localScale.x : 1f;

            while (t < restUntil)
            {
                t += Time.deltaTime;

                if (wordmark != null) wordmark.fillAmount = Eased(t, wordmarkStart, wordmarkDuration, easeOut);
                if (circle != null) circle.fillAmount = Eased(t, circleStart, circleDuration, easeOut);
                if (bar != null) bar.fillAmount = Eased(t, barStart, barDuration, easeOut);

                if (diamond != null)
                {
                    float raw = Progress(t, diamondStart, diamondDuration);
                    float e = overshoot.Evaluate(raw);
                    diamond.localScale = Vector3.one * Mathf.LerpUnclamped(DiamondStartScale, 1f, e);
                    diamond.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(DiamondStartAngle, 0f, e));
                    if (diamondGroup != null) diamondGroup.alpha = Mathf.Clamp01(raw * 3f);
                }

                yield return null;
            }

            if (wordmark != null) wordmark.fillAmount = 1f;
            if (circle != null) circle.fillAmount = 1f;
            if (bar != null) bar.fillAmount = 1f;
            if (diamond != null)
            {
                diamond.localScale = Vector3.one;
                diamond.localRotation = Quaternion.identity;
            }
            if (diamondGroup != null) diamondGroup.alpha = 1f;
            if (logoRoot != null) logoRoot.transform.localScale = Vector3.one * baseScale;
        }

        private IEnumerator FadeOut()
        {
            float baseScale = logoRoot != null ? logoRoot.transform.localScale.x : 1f;
            float t = 0f;
            while (t < exitDuration)
            {
                t += Time.deltaTime;
                float e = easeOut.Evaluate(Mathf.Clamp01(t / exitDuration));
                if (logoRoot != null)
                {
                    logoRoot.alpha = 1f - e;
                    logoRoot.transform.localScale = Vector3.one * Mathf.Lerp(baseScale, baseScale * exitScale, e);
                }
                yield return null;
            }
            if (logoRoot != null) logoRoot.alpha = 0f;
        }

        private IEnumerator ShowUnsupported()
        {
            if (unsupportedPanel == null) yield break;

            unsupportedPanel.gameObject.SetActive(true);
            float t = 0f;
            const float fade = 0.35f;
            while (t < fade)
            {
                t += Time.deltaTime;
                float e = Mathf.Clamp01(t / fade);
                unsupportedPanel.alpha = e;
                // El logo se atenua, pero sigue leyendose: es una pantalla de marca con una
                // explicacion, no un error. Al 25% parecia rota.
                if (logoRoot != null) logoRoot.alpha = 1f - (e * 0.40f);
                yield return null;
            }
            unsupportedPanel.alpha = 1f;
        }

        private bool checkFinished;

        private IEnumerator CheckArSupport()
        {
            if (debugForceUnsupported)
            {
                arSupported = false;
                checkFinished = true;
                Debug.Log("[KIMEVO] debugForceUnsupported activo: se fuerza el camino de incompatible.");
                yield break;
            }

            yield return ARSession.CheckAvailability();

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                yield return ARSession.Install();
            }

            arSupported = ARSession.state != ARSessionState.Unsupported
                          && ARSession.state != ARSessionState.NeedsInstall
                          && ARSession.state != ARSessionState.None;

            Debug.Log("[KIMEVO] Estado AR: " + ARSession.state + " -> soportado=" + arSupported);
            checkFinished = true;
        }

        private static float Progress(float t, float start, float duration)
        {
            if (duration <= 0f) return t >= start ? 1f : 0f;
            return Mathf.Clamp01((t - start) / duration);
        }

        private float Eased(float t, float start, float duration, AnimationCurve curve)
        {
            return Mathf.Clamp01(curve.Evaluate(Progress(t, start, duration)));
        }
    }
}
