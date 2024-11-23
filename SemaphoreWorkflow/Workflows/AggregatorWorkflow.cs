
using Dapr.Workflow;

namespace SemaphoreWorkflow.Workflows
{
    public class AggregatorWorkflow : Workflow<AggregatorState, bool>
    {
        private void Log(AggregatorState state, LogLevel logLevel, string message)
        {
            if (logLevel >= state.RuntimeConfig.logLevel)
                state.PersistentLog.Add(message);
        }

        public override async Task<bool> RunAsync(WorkflowContext context, AggregatorState state)
        {
            if (state == null)
            {
                state = new AggregatorState();
            }

            var incrCts = new CancellationTokenSource();
            var incr = context.WaitForExternalEventAsync<bool>("INCR", incrCts.Token);
            var decrCts = new CancellationTokenSource();
            var decr = context.WaitForExternalEventAsync<bool>("DECR", decrCts.Token);

            var winner = await Task.WhenAny(incr, decr);

            if (winner == incr)
            {
                decrCts.Cancel();

                state.Total = state.Total + 1;
                Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received INCR event, total is now {state.Total}");
            }
            else if (winner == decr)
            {
                incrCts.Cancel();

                state.Total = state.Total - 1;
                Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received DECR event, total is now {state.Total}");
            }
            else
            {
                decrCts.Cancel();
                incrCts.Cancel();
                throw new Exception("unknown event");
            }

            context.ContinueAsNew(state, true);
            return true;
        }
    }

    public class AggregatorState
    {
        public RuntimeConfig RuntimeConfig { get; set; } = new RuntimeConfig();
        public List<string> PersistentLog { get; set; } = new List<string>();
        public int Total { get; set; }
    }
}
