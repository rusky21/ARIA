using System.Text.Json.Nodes;

namespace AI_Supporter;

internal readonly record struct ElementRef(JsonObject Element, JsonArray Siblings, string PageLabel, string Label, bool IsNested);

internal readonly record struct ElementIndexItem(
    string Id,
    string Label,
    string Kind,
    string Page,
    int? X,
    int? Y,
    int? Type,
    bool Nested,
    string? ParentLabel,
    int? Index,
    int? DisplayIndex);

internal readonly record struct IndexResult(List<ElementIndexItem> Items, Dictionary<string, ElementRef> Index, List<ElementRef> Elements);

internal enum ColorAdjustment
{
    None,
    Darken,
    Lighten,
    MoreRed,
    MoreGreen,
    MoreBlue,
    MoreYellow
}

[Flags]
internal enum ColorScope
{
    None = 0,
    Text = 1,
    Background = 2,
    Border = 4
}
