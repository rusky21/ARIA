using System.Reflection;

namespace AI_Supporter;

public sealed record ToolParameterDescriptor(
    string Name,
    Type Type,
    string Description,
    bool Required,
    object? Minimum,
    object? Maximum
);

public sealed record ToolDescriptor(
    string Name,
    string Description,
    MethodInfo Method,
    object Instance,
    IReadOnlyList<ToolParameterDescriptor> Parameters
);
