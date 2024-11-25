
using Dapr.Workflow;

namespace SemaphoreWorkflow.Workflows
{
    public class Aggregator3Workflow : Workflow<AggregatorState, bool>
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

            var incr = await context.WaitForExternalEventAsync<bool>("INCR");
            if (incr == true)
            {
                state.Total = state.Total + 1;
                Log(state, LogLevel.Info, $"[context time: {context.CurrentUtcDateTime:HH:mm:ss}] [actual time: {DateTime.UtcNow:HH:mm:ss}] Received INCR event, total is now {state.Total}");
            }

            var decr = await context.WaitForExternalEventAsync<bool>("DECR");
            if (decr == true)
            {
                state.Total = state.Total - 1;
                Log(state, LogLevel.Info, $"[context time: {context.CurrentUtcDateTime:HH:mm:ss}] [actual time: {DateTime.UtcNow:HH:mm:ss}] Received DECR event, total is now {state.Total}");
            }

            context.ContinueAsNew(state, true);
            return true;
        }
    }
}
