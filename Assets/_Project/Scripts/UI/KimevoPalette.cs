using UnityEngine;

namespace Kimevo.UI
{
    /// <summary>
    /// Los colores de la barra de modos.
    ///
    /// Cada neon sale de un color de marca subido en luminancia y saturacion, no de una paleta
    /// nueva: el magenta y el teal ya viven en la paleta de dibujo y en el HUD, y meter tonos
    /// ajenos partiria la identidad justo en el elemento mas visible de la pantalla.
    ///
    /// El problema de fondo con el neon sobre AR es que el fondo no lo elegimos nosotros: la
    /// misma barra puede caer sobre una pared blanca a pleno sol o sobre una habitacion a
    /// oscuras. Subir el brillo del trazo resuelve el fondo oscuro y empeora el claro. Por eso
    /// el contraste no se fia del color sino de <see cref="Ink"/>: un halo oscuro detras del
    /// trazo, dibujado en el shader, que garantiza el borde contra cualquier fondo. El neon
    /// aporta identidad; el halo aporta legibilidad.
    /// </summary>
    public static class KimevoPalette
    {
        /// <summary>Explorar. Teal de marca (0.04,0.56,0.56) llevado a neon.</summary>
        public static readonly Color Explore = new Color32(0x22, 0xE8, 0xE0, 0xFF);

        /// <summary>Colocar. Magenta de marca (0.84,0.17,0.39) llevado a neon.</summary>
        public static readonly Color Place = new Color32(0xFF, 0x3D, 0x7F, 0xFF);

        /// <summary>Dibujar. Naranja de marca (0.85,0.40,0.03) llevado a ambar neon.</summary>
        public static readonly Color Draw = new Color32(0xFF, 0xB0, 0x20, 0xFF);

        /// <summary>Halo oscuro detras del trazo. Es lo que hace legible la barra sobre video claro.</summary>
        public static readonly Color Ink = new Color32(0x06, 0x08, 0x0D, 0xFF);

        /// <summary>Trazo e icono en reposo.</summary>
        public static readonly Color Idle = new Color32(0xE8, 0xEC, 0xF2, 0xFF);

        /// <summary>Estado no disponible.</summary>
        public static readonly Color Disabled = new Color32(0x8A, 0x93, 0xA3, 0xFF);

        public static Color ForMode(int index)
        {
            switch (index)
            {
                case 1: return Place;
                case 2: return Draw;
                default: return Explore;
            }
        }
    }
}
