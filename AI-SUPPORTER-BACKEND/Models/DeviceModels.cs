using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_Supporter;

public enum DeviceType
{
    Unknown,
    Camera,
    Web,
    Osc,
    ArtNet,
    Switch,
    Computer,
    Printer
}

public sealed class DiscoveredDevice
{
    public string Ip { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceType Type { get; set; } = DeviceType.Unknown;
    public bool Online { get; set; }
    public List<string> Services { get; set; } = [];
    public string? MjpegUrl { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string? Mac { get; set; }
    public string? Vendor { get; set; }
    public int ResponseMs { get; set; }
    public string? Notes { get; set; }
}

public sealed class DeviceSnapshot
{
    public DateTime ScanTime { get; set; } = DateTime.UtcNow;
    public List<DiscoveredDevice> Devices { get; set; } = [];
}
