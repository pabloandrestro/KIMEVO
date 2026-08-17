using System.Collections.Generic;
using UnityEngine;

namespace Kimevo.Drawing
{
    /// <summary>
    /// Un trazo. Envuelve un LineRenderer y le impone las tres decisiones que separan un
    /// dibujo que se lee de uno que parece un error de render:
    ///
    /// - alignment = View, para que el trazo se vea como una linea desde cualquier angulo
    ///   en vez de como una cinta plana que desaparece cuando la miras de canto.
    /// - useWorldSpace = false, para que los puntos vivan en el espacio del dibujo y sigan
    ///   al anchor cuando ARCore reajuste su mapa.
    /// - color por vertice y material COMPARTIDO. Asignar .material a cada trazo crearia una
    ///   instancia de material por trazo y rompe el batching: es lo que separa cincuenta
    ///   trazos fluidos de cincuenta trazos que calientan el telefono.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class StrokeRenderer : MonoBehaviour
    {
        private readonly List<Vector3> points = new List<Vector3>(256);
        private LineRenderer line;

        public int PointCount => points.Count;

        public Vector3 LastPoint => points.Count > 0 ? points[points.Count - 1] : Vector3.zero;

        public void Init(Material shared, Color color, float width)
        {
            line = GetComponent<LineRenderer>();

            line.sharedMaterial = shared;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.widthMultiplier = width;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.startColor = color;
            line.endColor = color;
            line.positionCount = 0;

            points.Clear();
        }

        /// <summary>
        /// Anade un punto si esta lo bastante lejos del anterior. El filtro no es un detalle
        /// de optimizacion: un dedo quieto genera un punto por frame, y en pocos segundos el
        /// LineRenderer acumula cientos de puntos superpuestos que lo ahogan.
        /// </summary>
        public bool TryAddPoint(Vector3 localPoint, float minDistance)
        {
            if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], localPoint) < minDistance)
            {
                return false;
            }

            points.Add(localPoint);
            line.positionCount = points.Count;
            line.SetPosition(points.Count - 1, localPoint);
            return true;
        }

        /// <summary>Un trazo de un solo punto no dibuja nada: es un toque, no una linea.</summary>
        public bool IsMeaningful => points.Count >= 2;
    }
}
