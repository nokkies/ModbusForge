using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    public interface ICustomEntryService
    {
        Task SaveCustomAsync(ObservableCollection<CustomEntry> entries);
        Task<ObservableCollection<CustomEntry>> LoadCustomAsync();
    }
}
