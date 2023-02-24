using Microsoft.Build.Framework;

using System.Diagnostics;

#nullable disable

namespace tortillachip;
public class Logger : INodeLogger
{
    public LoggerVerbosity Verbosity { get => LoggerVerbosity.Minimal; set => value = LoggerVerbosity.Minimal; }
    public string Parameters { get => ""; set => value = ""; }

    Stopwatch _overallTime = new();

    object _lock = new();

    CancellationTokenSource _cts = new();

    NodeStatus[] _nodes;

    readonly Dictionary<ProjectContext, Project> _notableProjects = new();

    readonly Dictionary<ProjectContext, (bool Notable, string Path, string Targets)> _notabilityByContext = new();

    readonly Dictionary<ProjectContext, Stopwatch> _projectTimeCounter = new();

    int _usedNodes = 0;

    public void Initialize(IEventSource eventSource, int nodeCount)
    {
        _nodes = new NodeStatus[nodeCount];

        Initialize(eventSource);
    }

    public void Initialize(IEventSource eventSource)
    {
        // Debugger.Launch();
        eventSource.BuildStarted += new BuildStartedEventHandler(BuildStarted);
        eventSource.BuildFinished += new BuildFinishedEventHandler(BuildFinished);
        eventSource.ProjectStarted += new ProjectStartedEventHandler(ProjectStarted);
        eventSource.ProjectFinished += new ProjectFinishedEventHandler(ProjectFinished);
        eventSource.TargetStarted += new TargetStartedEventHandler(TargetStarted);
        eventSource.TargetFinished += new TargetFinishedEventHandler(TargetFinished);
        eventSource.TaskStarted += new TaskStartedEventHandler(TaskStarted);

        eventSource.MessageRaised += new BuildMessageEventHandler(MessageRaised);
        eventSource.WarningRaised += new BuildWarningEventHandler(WarningRaised);
        eventSource.ErrorRaised += new BuildErrorEventHandler(ErrorRaised);

        Thread refresher = new(ThreadProc);
        refresher.Start();
    }

    private void ThreadProc()
    {
        while (!_cts.IsCancellationRequested)
        {
            Thread.Sleep(1_000 / 30); // poor approx of 30Hz
            lock (_lock)
            {
                EraseNodes();
                DisplayNodes();
            }
        }

        EraseNodes();
    }

    private void BuildStarted(object sender, BuildStartedEventArgs e)
    {
    }

    private void BuildFinished(object sender, BuildFinishedEventArgs e)
    {
    }

    private void ProjectStarted(object sender, ProjectStartedEventArgs e)
    {
        bool notable = IsNotableProject(e);

        _notabilityByContext[new ProjectContext(e)] = (notable, e.ProjectFile, e.TargetNames);

        ProjectContext c = new ProjectContext(e);
        _projectTimeCounter[c] = Stopwatch.StartNew();

        if (!notable) 
        {
            //Console.WriteLine($"{e.ProjectFile}:{e.TargetNames} is not notable");
            return;
        }
        else
        {
            //Console.WriteLine($"{e.ProjectFile}:{e.TargetNames} IS notable");
            _notableProjects[c] = new();
        }
    }

    private bool IsNotableProject(ProjectStartedEventArgs e)
    {
        return e.TargetNames switch
        {
            "" => true,
            "GetTargetFrameworks" or "GetTargetFrameworks" or "GetNativeManifest" or "GetCopyToOutputDirectoryItems" => false,
            _ => true,
        };
        //return e.TargetNames is { Length: 0 } &&
        //    (e.GlobalProperties.ContainsKey("TargetFramework") // is an inner build
        //    ||
        //     false // TODO: e.Properties["TargetFrameworks"] is empty // is single-targeted
        //     );
    }

    private void ProjectFinished(object sender, ProjectFinishedEventArgs e)
    {
        ProjectContext c = new(e);
        if (_notabilityByContext[c].Notable)
        {
            EraseNodes();

            double duration = _notableProjects[c].Stopwatch.Elapsed.TotalSeconds;

            Console.WriteLine($"{e.ProjectFile} \x1b[1mcompleted\x1b[22m ({duration:F1}s)");
            DisplayNodes();
        }
    }

    private void DisplayNodes()
    {
        lock (_lock)
        {
            Console.Write("\x1b"+"7");

            int i = 0;
            foreach (NodeStatus n in _nodes)
            {
                if (n is null)
                {
                    continue;
                }
                Console.WriteLine(FitToWidth(n.ToString()));
                i++;
            }

            _usedNodes = i;
        }
    }

    string FitToWidth(string input)
    {
        return input.Substring(0, Math.Min(input.Length, Console.WindowWidth));
    }

    private void EraseNodes()
    {
        lock (_lock)
        {
            if (_usedNodes == 0)
            {
                return;
            }
            //Console.WriteLine($"\x1b[{_usedNodes + 1}F");
            Console.Write("\x1b" + "8");
            Console.Write($"\x1b[0J");
            //Console.Write($"\x1b[{_usedNodes}F");
        }
    }

    private void TargetStarted(object sender, TargetStartedEventArgs e)
    {
        _nodes[NodeIndexForContext(e.BuildEventContext)] = new(e.ProjectFile, e.TargetName, _projectTimeCounter[new ProjectContext(e)]);
    }

    private int NodeIndexForContext(BuildEventContext context)
    {
        return context.NodeId - 1;
    }

    private void TargetFinished(object sender, TargetFinishedEventArgs e)
    {
    }

    private void TaskStarted(object sender, TaskStartedEventArgs e)
    {
    }

    private void MessageRaised(object sender, BuildMessageEventArgs e)
    {
    }

    private void WarningRaised(object sender, BuildWarningEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void ErrorRaised(object sender, BuildErrorEventArgs e)
    {
        throw new NotImplementedException();
    }

    public void Shutdown()
    {
        _cts.Cancel();
    }
}

internal record ProjectContext(int Id)
{
    public ProjectContext(BuildEventContext context)
        : this(context.ProjectContextId)
    { }

    public ProjectContext(BuildEventArgs e)
        : this(e.BuildEventContext)
    { }
}

internal record NodeStatus(string Project, string Target, Stopwatch Stopwatch)
{
    public override string ToString()
    {
        return $"{Project} {Target} ({Stopwatch.Elapsed.TotalSeconds:F1}s)";
    }
}