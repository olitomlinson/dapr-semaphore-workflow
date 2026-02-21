using System.Text;
using System.Text.Json;
using Dapr.Workflow;
using Microsoft.AspNetCore.Mvc;
using SemaphoreWorkflow;
using SemaphoreWorkflow.Activities;
using SemaphoreWorkflow.Workflows;

var builder = WebApplication.CreateBuilder(args);

var semaphoreWorkflowId = Environment.GetEnvironmentVariable("SEMAPHORE_WORKFLOW_INSTANCE_ID");

builder.Services.AddHttpClient();
builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<SemaphoreWorkflow.Workflows.SemaphoreWorkflow>();
    options.RegisterWorkflow<ConstrainedWorkflow>();

    options.RegisterActivity<VerySlowActivity>();
    options.RegisterActivity<RaiseProceedEventActivity>();
    options.RegisterActivity<RaiseSignalEventActivity>();
    options.RegisterActivity<RaiseWaitEventActivity>();
});

builder.Services.AddHttpClient<DaprJobsService>(client =>
{
    var daprHttpPort = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT");
    client.BaseAddress = new Uri($"http://localhost:{daprHttpPort}/v1.0-alpha1/jobs/");
});

var app = builder.Build();


app.MapGet("/health", async (DaprJobsService jobsService) =>
{
    await jobsService.EnsureSemaphoreJobIsRunning(semaphoreWorkflowId);
    app.Logger.LogInformation($"Health is good");
});

app.MapGet("/semaphore/status", async (DaprWorkflowClient grpcClient) =>
{
    var semaphoreState = await grpcClient.GetWorkflowStateAsync(semaphoreWorkflowId, true);
    var result = new
    {
        Summary = semaphoreState.ReadCustomStatusAs<SemaphoreSummary>(),
        Config = semaphoreState.ReadInputAs<SemaphoreState>()?.RuntimeConfig,
        Logs = semaphoreState.ReadInputAs<SemaphoreState>()?.PersistentLog,
        CompletedWaits = semaphoreState.ReadInputAs<SemaphoreState>()?.CompletedWaits
    };
    return result;
});

app.MapPost("/job/{semaphoreWorkflowId}", async (DaprWorkflowClient workflowClient, string semaphoreWorkflowId) =>
{
    var createSemaphoreWorkflowInstance = false;

    try
    {
        var SemaphoreState = await workflowClient.GetWorkflowStateAsync(semaphoreWorkflowId, false);
        if (SemaphoreState is null)
            createSemaphoreWorkflowInstance = true;
        else if (!SemaphoreState.IsWorkflowRunning)
            createSemaphoreWorkflowInstance = true;
    }
    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unknown)
    {
        // TODO : refactor this when the wfruntime handles 404 workflows properly
        createSemaphoreWorkflowInstance = true;
    }

    if (!createSemaphoreWorkflowInstance)
        return;

    app.Logger.LogWarning($"'{semaphoreWorkflowId}' workflow instance does not exist, attempting to schedule it");

    await workflowClient.ScheduleNewWorkflowAsync(nameof(SemaphoreWorkflow.Workflows.SemaphoreWorkflow), semaphoreWorkflowId, null);
});

app.MapPost("/bulk-schedule-constrained-workflows", async ([FromQuery(Name = "prefix")] string? prefix, [FromQuery(Name = "count")] int? count, [FromQuery(Name = "sleep")] int? sleep, DaprWorkflowClient grpcClient) =>
{
    prefix = string.IsNullOrEmpty(prefix) ? Guid.NewGuid().ToString().Substring(0, 8) : prefix;
    count = !count.HasValue ? 1 : count.Value;
    sleep = !sleep.HasValue ? 0 : sleep.Value;

    var session = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Tuple<string, int>(prefix, count.Value))));

    var cts = new CancellationTokenSource();
    var options = new ParallelOptions() { MaxDegreeOfParallelism = 1, CancellationToken = cts.Token };
    var ids = new List<string>();
    await Parallel.ForEachAsync(Enumerable.Range(0, count.Value), options, async (i, token) =>
    {
        await Task.Delay(sleep.Value);

        var lol = await grpcClient.ScheduleNewWorkflowAsync(nameof(ConstrainedWorkflow), $"{prefix}-{i}", true);
        ids.Add(lol);
    });

    return new
    {
        session,
        instanceIds = ids
    };
});

app.MapGet("/check", async ([FromQuery(Name = "session")] string? session, [FromQuery(Name = "prefix")] string? prefix, [FromQuery(Name = "count")] int? count, DaprWorkflowClient workflowClient) =>
{
    if (!string.IsNullOrEmpty(session))
    {
        (prefix, count) = JsonSerializer.Deserialize<Tuple<string, int>>(Encoding.UTF8.GetString(Convert.FromBase64String(session)));
    }

    count = !count.HasValue ? 1 : count.Value;


    var cts = new CancellationTokenSource();
    var options = new ParallelOptions() { MaxDegreeOfParallelism = 1, CancellationToken = cts.Token };
    var ids = new Dictionary<string, WorkflowState>();
    for (int i = 0; i < count.Value; i++)
    {
        var res1 = await workflowClient.GetWorkflowStateAsync($"{prefix}-{i}", true);
        ids.Add($"{prefix}-{i}", res1);
    }

    var res = ids.Select(x => new
    {
        id = x.Key,
        rtstatus = x.Value.RuntimeStatus,
        state = x.Value,
        status = x.Value.ReadCustomStatusAs<string>(),
    });

    var res2 = res.Select(x => new
    {
        x.id,
        x.rtstatus,
        x.status,
        output = x.state.IsWorkflowCompleted ? x.state.ReadOutputAs<string>() : "n/a"
    });

    return res2;
});

app.MapGet("/", () => "Hello World!");

app.Run();