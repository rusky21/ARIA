using System.Text.Json;

namespace AI_Supporter;

internal sealed record TaskCall(string Function, Dictionary<string, JsonElement> Args, string? Message)
{
    public static TaskCall Empty => new(string.Empty, new Dictionary<string, JsonElement>(), null);

    public bool IsClarify => Function.Equals("clarify", StringComparison.OrdinalIgnoreCase);
    public bool IsNone => Function.Equals("none", StringComparison.OrdinalIgnoreCase);
}
