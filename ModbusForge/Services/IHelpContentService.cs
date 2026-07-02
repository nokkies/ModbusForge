using System.Windows.Documents;

namespace ModbusForge.Services
{
    public interface IHelpContentService
    {
        FlowDocument GetHelpContent(string topicId);
        bool HasTopic(string topicId);
    }
}
