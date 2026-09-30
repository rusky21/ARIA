using System.Text;

namespace AI_Supporter;

public interface IBroadcastService
{
    void Broadcast(string message);
    void BroadcastChat(string message);
    void BroadcastLog(string message, string? requestId = null);
    void BroadcastJson(object data, string? requestId = null);
    void SendJson(string json);

    void SendJsonBytes(byte[] json, string? username = null) =>
        SendJson(json is null ? string.Empty : Encoding.UTF8.GetString(json));
}
