namespace AI_Supporter;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class AiParameterAttribute : Attribute
{
    public string Description { get; }
    public bool Required { get; }
    public object? Minimum { get; }
    public object? Maximum { get; }

    public AiParameterAttribute(string description, bool required = true, object? minimum = null, object? maximum = null)
    {
        Description = description;
        Required = required;
        Minimum = minimum;
        Maximum = maximum;
    }
}
