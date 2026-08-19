using UnityEngine;

namespace Kimevo.UI
{
    /// <summary>
    /// Un golpecito corto al pulsar.
    ///
    /// No usa Handheld.Vibrate a proposito: esa llamada dispara medio segundo de vibracion de
    /// cuerpo entero, que es lo que usa una alarma. En un boton se siente como un error, no
    /// como una confirmacion.
    ///
    /// En su lugar se pide al sistema el feedback haptico estandar de pulsacion sobre la vista
    /// de la actividad. Sale mucho mas corto y, lo que importa mas, respeta el ajuste de
    /// vibracion tactil del telefono: si la persona lo tiene apagado, no vibra. Una vibracion
    /// que ignora esa preferencia es una falta de educacion del software.
    ///
    /// Todo va envuelto en try/catch y cacheado. Si algo de la interoperabilidad con Java falla
    /// - otra version de actividad, un fabricante creativo - la interfaz debe seguir
    /// funcionando sin vibrar. Nunca al reves.
    /// </summary>
    public static class Haptics
    {
        // HapticFeedbackConstants.VIRTUAL_KEY. Es el que usan los teclados del sistema para
        // una tecla, que es exactamente la sensacion que queremos.
        private const int VirtualKey = 1;

        // FLAG_IGNORE_GLOBAL_SETTING no se usa justamente por lo dicho arriba.
        private const int FlagIgnoreViewSetting = 1;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject decorView;
        private static bool resolved;
        private static bool available;
#endif

        public static void Tap()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!resolved)
            {
                Resolve();
            }

            if (!available)
            {
                return;
            }

            try
            {
                decorView.Call<bool>("performHapticFeedback", VirtualKey, FlagIgnoreViewSetting);
            }
            catch (System.Exception e)
            {
                available = false;
                Debug.LogWarning("[KIMEVO] Haptica desactivada tras fallar: " + e.Message);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void Resolve()
        {
            resolved = true;

            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                {
                    // El decorView no se libera: se reutiliza en cada pulsacion, y recrearlo
                    // cada vez costaria una travesia del puente JNI por toque.
                    decorView = window.Call<AndroidJavaObject>("getDecorView");
                    available = decorView != null;
                }
            }
            catch (System.Exception e)
            {
                available = false;
                Debug.LogWarning("[KIMEVO] Sin haptica en este dispositivo: " + e.Message);
            }
        }
#endif
    }
}
