namespace ModbusForge.Services
{
    using ModbusForge.ViewModels.Coordinators;

    public interface ISimulationService
    {
        void Start(SimulationCoordinator coordinator);
        void Stop();
    }
}
