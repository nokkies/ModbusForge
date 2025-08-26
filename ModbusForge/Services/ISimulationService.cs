namespace ModbusForge.Services
{
    using ModbusForge.ViewModels;

    public interface ISimulationService
    {
        void Start(MainViewModel viewModel);
        void Stop();
    }
}
