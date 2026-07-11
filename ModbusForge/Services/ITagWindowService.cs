namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction for showing tag-related tool windows (Tag Browser, Watch Window).
    /// Keeps ViewModels free of direct WPF Window references and App.Current usage.
    /// </summary>
    public interface ITagWindowService
    {
        /// <summary>Shows the Tag Browser window.</summary>
        void ShowTagBrowser();

        /// <summary>Shows the Watch Window.</summary>
        void ShowWatchWindow();
    }
}
