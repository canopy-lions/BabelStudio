using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BabelStudio.Architecture.Tests;

/// <summary>
/// Enforces that <c>AGENTS.md</c>'s "Strict dependency direction" diagram matches
/// every <c>src/BabelStudio.*/*.csproj</c>'s actual <c>ProjectReference</c>
/// set. The diagram is the source of truth; the tests fail if either the
/// diagram or a csproj drifts.
/// </summary>
public sealed class DependencyGraphTests
{
    [Fact]
    public void AgentsMdDiagramMatchesEveryCsprojProjectReference()
    {
        var repoRoot = FindRepoRoot();
        var diagram = ParseAgentsMdDiagram(Path.Combine(repoRoot, "AGENTS.md"));
        var csprojs = ReadAllSrcCsprojDependencies(Path.Combine(repoRoot, "src"));

        var diagramProjects = diagram.Keys.ToHashSet();
        var csprojProjects = csprojs.Keys.ToHashSet();

        var missingFromDiagram = csprojProjects.Except(diagramProjects).OrderBy(x => x).ToList();
        var missingFromCsprojs = diagramProjects.Except(csprojProjects).OrderBy(x => x).ToList();

        Assert.True(
            missingFromDiagram.Count == 0,
            $"src/ contains csproj(s) not listed in AGENTS.md diagram: {string.Join(", ", missingFromDiagram)}");
        Assert.True(
            missingFromCsprojs.Count == 0,
            $"AGENTS.md diagram lists project(s) that do not exist under src/: {string.Join(", ", missingFromCsprojs)}");

        var mismatches = new List<string>();
        foreach (var (project, diagramDeps) in diagram.OrderBy(kv => kv.Key))
        {
            var actual = csprojs[project];
            var expected = diagramDeps;

            var extraInCsproj = actual.Except(expected).OrderBy(x => x).ToList();
            var extraInDiagram = expected.Except(actual).OrderBy(x => x).ToList();

            if (extraInCsproj.Count > 0 || extraInDiagram.Count > 0)
            {
                mismatches.Add(
                    $"  {project}: " +
                    (extraInCsproj.Count > 0 ? $"csproj has extra [{string.Join(", ", extraInCsproj)}] " : "") +
                    (extraInDiagram.Count > 0 ? $"diagram has extra [{string.Join(", ", extraInDiagram)}]" : ""));
            }
        }

        Assert.True(
            mismatches.Count == 0,
            "AGENTS.md dependency diagram does not match csproj ProjectReferences:\n" +
            string.Join("\n", mismatches));
    }

    [Fact]
    public void DomainHasNoProjectReferences()
    {
        var repoRoot = FindRepoRoot();
        var csprojs = ReadAllSrcCsprojDependencies(Path.Combine(repoRoot, "src"));
        Assert.Empty(csprojs["BabelStudio.Domain"]);
    }

    [Fact]
    public void ContractsHasNoProjectReferences()
    {
        var repoRoot = FindRepoRoot();
        var csprojs = ReadAllSrcCsprojDependencies(Path.Combine(repoRoot, "src"));
        Assert.Empty(csprojs["BabelStudio.Contracts"]);
    }

    [Fact]
    public void DependencyGraphIsAcyclic()
    {
        var repoRoot = FindRepoRoot();
        var csprojs = ReadAllSrcCsprojDependencies(Path.Combine(repoRoot, "src"));

        var color = new Dictionary<string, int>();
        foreach (var node in csprojs.Keys)
        {
            color[node] = 0;
        }

        foreach (var start in csprojs.Keys)
        {
            var path = new Stack<string>();
            if (HasCycle(start, csprojs, color, path, out var cyclePath))
            {
                Assert.Fail($"Cycle detected in project dependencies: {cyclePath}");
            }
        }
    }

    private static bool HasCycle(
        string node,
        IReadOnlyDictionary<string, HashSet<string>> graph,
        Dictionary<string, int> color,
        Stack<string> path,
        out string cyclePath)
    {
        cyclePath = string.Empty;
        if (color[node] == 1)
        {
            cyclePath = string.Join(" -> ", path.Reverse().Append(node));
            return true;
        }
        if (color[node] == 2)
        {
            return false;
        }

        color[node] = 1;
        path.Push(node);
        foreach (var next in graph[node])
        {
            if (!graph.ContainsKey(next))
            {
                continue;
            }
            if (HasCycle(next, graph, color, path, out cyclePath))
            {
                return true;
            }
        }
        path.Pop();
        color[node] = 2;
        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BabelStudio.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate BabelStudio.sln from the test runner base directory.");
    }

    private static IReadOnlyDictionary<string, HashSet<string>> ParseAgentsMdDiagram(string agentsMdPath)
    {
        var text = File.ReadAllText(agentsMdPath);
        var blockMatch = Regex.Match(
            text,
            @"Strict dependency direction.*?```\s*(?<body>.*?)```",
            RegexOptions.Singleline);
        Assert.True(blockMatch.Success, "AGENTS.md is missing the expected 'Strict dependency direction' fenced code block.");

        var body = blockMatch.Groups["body"].Value;
        var result = new Dictionary<string, HashSet<string>>();
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('→');
            Assert.Equal(2, parts.Length);

            var project = Normalize(parts[0]);

            var rhs = parts[1].Trim();
            var deps = new HashSet<string>();
            if (!rhs.StartsWith("(", StringComparison.Ordinal))
            {
                foreach (var raw in rhs.Split(','))
                {
                    deps.Add(Normalize(raw));
                }
            }

            result[project] = deps;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, HashSet<string>> ReadAllSrcCsprojDependencies(string srcRoot)
    {
        var result = new Dictionary<string, HashSet<string>>();
        foreach (var csproj in Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(csproj);
            var doc = XDocument.Load(csproj);
            var refs = new HashSet<string>();
            foreach (var pr in doc.Descendants("ProjectReference"))
            {
                var include = pr.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include))
                {
                    continue;
                }
                var normalized = include.Replace('\\', '/');
                var refName = Path.GetFileNameWithoutExtension(normalized);
                refs.Add(refName);
            }
            result[name] = refs;
        }
        return result;
    }

    private static string Normalize(string rawShortName)
    {
        var trimmed = rawShortName.Trim();
        return trimmed.StartsWith("BabelStudio.", StringComparison.Ordinal)
            ? trimmed
            : "BabelStudio." + trimmed;
    }
}
