namespace CinematicShaders.UI.Screens
{
    public interface ILayer
    {
        int Order { get; }  // 1, 2, 3...
        string LayerName { get; }
        bool IsDirty { get; set; }
        
        void Render(float typeOnProgress);
        void MarkDirty();
    }
}
