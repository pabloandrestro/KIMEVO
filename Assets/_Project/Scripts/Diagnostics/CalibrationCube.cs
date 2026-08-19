using UnityEngine;
using Kimevo.AR;

namespace Kimevo.Diagnostics
{
    /// <summary>
    /// Un cubo de 1 m x 1 m x 1 m para comprobar la escala contra el mundo real.
    ///
    /// Es la unica forma honesta de saber si "1 unidad = 1 metro" se cumple de verdad en el
    /// telefono. Un objeto de 12 cm no sirve de referencia porque no hay nada de 12 cm a mano
    /// con lo que compararlo; en cambio un metro se compara de un vistazo contra una puerta
    /// (2,0-2,1 m), una mesa (0,72-0,75 m) o una silla (0,45 m de asiento).
    ///
    /// Se apoya sobre la superficie de la reticula cuando la hay, porque un cubo flotando no
    /// deja juzgar la altura. Si no hay superficie se planta delante de la camara, que al
    /// menos permite comprobar la anchura.
    /// </summary>
    public sealed class CalibrationCube : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Distancia a la que se planta el cubo si no hay superficie bajo la reticula.")]
        private float fallbackDistance = 2f;

        private ARSurfaceService surface;
        private Camera arCamera;
        private GameObject cube;

        public bool Visible => cube != null;

        private void Awake()
        {
            surface = FindAnyObjectByType<ARSurfaceService>(FindObjectsInactive.Include);
            arCamera = Camera.main;
        }

        public void Toggle()
        {
            if (cube != null)
            {
                Destroy(cube);
                cube = null;
                Debug.Log("[KIMEVO-M] cubo de calibracion retirado");
                return;
            }

            Spawn();
        }

        private void Spawn()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
            }

            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "CalibrationCube_1m";

            // Sin collider: no debe estorbar a ningun raycast de colocacion ni de dibujo.
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            // Escala 1 exacta. Este cubo NO se ajusta por objectSize ni por nada: si sale de
            // otro tamano en pantalla, el problema es la escala del mundo AR y eso es
            // justamente lo que venimos a detectar.
            cube.transform.localScale = Vector3.one;

            Vector3 position;
            Quaternion rotation = Quaternion.identity;

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (surface != null && surface.TryGetSurfaceHit(center, out SurfaceHit hit))
            {
                // Medio metro por encima del impacto: el cubo se apoya sobre la superficie en
                // vez de quedar medio enterrado, y asi su cara inferior marca el suelo real.
                position = hit.Pose.position + (hit.Pose.up * 0.5f);
                rotation = hit.Pose.rotation;
            }
            else if (arCamera != null)
            {
                position = arCamera.transform.position + (arCamera.transform.forward * fallbackDistance);
            }
            else
            {
                position = Vector3.forward * fallbackDistance;
            }

            cube.transform.SetPositionAndRotation(position, rotation);

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Material creado en caliente y a proposito: es una herramienta de depuracion
                // y no merece un asset propio que luego aparezca en el build de produccion.
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    var material = new Material(shader);
                    material.SetFloat("_Surface", 1f); // transparente
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_ZWrite", 0f);
                    material.renderQueue = 3000;
                    material.SetColor("_BaseColor", new Color(0.05f, 0.9f, 0.9f, 0.35f));
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    renderer.sharedMaterial = material;
                }

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Debug.Log("[KIMEVO-M] cubo de calibracion de 1 m plantado en " + position.ToString("F2"));
        }
    }
}
