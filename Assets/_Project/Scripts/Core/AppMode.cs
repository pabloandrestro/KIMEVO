namespace Kimevo.Core
{
    /// <summary>
    /// Los tres estados de la experiencia. No son escenas: comparten camara, sesion AR,
    /// planos y anchors, y separarlos obligaria a serializar y restaurar todo eso en cada
    /// transicion. Son modos de un mismo mundo.
    /// </summary>
    public enum AppMode
    {
        Explore = 0,
        Place = 1,
        Draw = 2
    }
}
