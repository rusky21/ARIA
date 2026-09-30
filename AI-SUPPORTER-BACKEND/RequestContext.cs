namespace AI_Supporter;

public static class RequestContext
{
    public static readonly AsyncLocal<string?> Current = new();
}