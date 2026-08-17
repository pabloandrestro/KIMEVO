using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kimevo.EditorTools
{
    /// <summary>
    /// Sube el numero de build y estampa la fecha en la version, en cada compilacion.
    ///
    /// Existe por una hora perdida el 17 de agosto de 2026. Todos los builds eran
    /// versionName 0.1.0 y versionCode 1, asi que desde el telefono eran indistinguibles:
    /// cuando un APK no llego a instalarse, "no se actualizo" y "se actualizo pero no puedo
    /// saberlo" se veian exactamente igual, y se busco el fallo en el codigo durante un rato
    /// largo antes de mirar la fecha de instalacion.
    ///
    /// La version resultante se lee en pantalla, en la linea de diagnostico, de modo que la
    /// pregunta "¿esto es el build nuevo?" se responde de un vistazo y sin cable.
    /// </summary>
    public sealed class BuildStamp : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            int code = PlayerSettings.Android.bundleVersionCode + 1;
            PlayerSettings.Android.bundleVersionCode = code;

            // Solo ASCII: versionName acaba en el manifiesto de Android y no es sitio para
            // adornos tipograficos. Formato yyMMdd.HHmm, que ordena bien alfabeticamente.
            string stamp = DateTime.Now.ToString("yyMMdd.HHmm");
            PlayerSettings.bundleVersion = "0.2." + code + "-" + stamp;

            Debug.Log("[KIMEVO] Build " + code + " sellado como " + PlayerSettings.bundleVersion);
        }
    }
}
