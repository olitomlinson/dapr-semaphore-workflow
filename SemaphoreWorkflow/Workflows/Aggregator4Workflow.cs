
using Dapr.Workflow;

namespace SemaphoreWorkflow.Workflows
{
    public class Aggregator4Workflow : Workflow<AggregatorState, bool>
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
            state.Seq += 1;

            var incr = context.WaitForExternalEventAsync<bool>("INCR");
            var decr = context.WaitForExternalEventAsync<bool>("DECR");

            await Task.WhenAny(incr, decr);

            await context.CreateTimer(TimeSpan.FromMilliseconds(200));

            if (incr.IsCompletedSuccessfully)
            {
                state.Total += 1;
                Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received INCR event, total is now {state.Total}");
            }

            if (decr.IsCompletedSuccessfully)
            {
                state.Total -= 1;
                Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received DECR event, total is now {state.Total}");
            }

            context.ContinueAsNew(state, true);
            return true;
        }
    }
}
