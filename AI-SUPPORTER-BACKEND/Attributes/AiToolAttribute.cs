namespace AI_Supporter;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AiToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public AiToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
