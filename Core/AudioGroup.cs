namespace CinematicShaders.Core
{
    /// <summary>
    /// Audio category groups for modular volume and mute control.
    /// Each group can have independent volume/mute settings in addition to the master volume.
    /// </summary>
    public enum AudioGroup
    {
        StarConsole = 0,
        // Future expansion slots (values must be stable)
        // GTAO = 1,
        // Starfield = 2,
        // Ambient = 3,
    }
}
