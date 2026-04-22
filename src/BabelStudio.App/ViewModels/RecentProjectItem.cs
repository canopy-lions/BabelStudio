namespace BabelStudio.App.ViewModels;

public sealed record RecentProjectItem(
    string ProjectName,
    string ProjectPath,
    DateTimeOffset LastOpenedAtUtc)
{
    public string LastOpenedLabel => LastOpenedAtUtc.ToLocalTime().ToString("g");
}
