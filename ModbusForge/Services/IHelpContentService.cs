namespace ModbusForge.Services
{
    public interface IHelpContentService
    {
        string? GetHelpContent(string topicId);
        bool HasTopic(string topicId);
    }
}
