
using Dapr.Workflow;

namespace SemaphoreWorkflow.Workflows
{
    public class Aggregator2Workflow : Workflow<AggregatorState, bool>
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

            try
            {
                var incr = await context.WaitForExternalEventAsync<bool>("INCR", TimeSpan.FromMilliseconds(1000));
                if (incr == true)
                {
                    state.Total = state.Total + 1;
                    Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received INCR event, total is now {state.Total}");
                }
            }
            catch (TaskCanceledException ex)
            {

            }

            try
            {
                var decr = await context.WaitForExternalEventAsync<bool>("DECR", TimeSpan.FromMilliseconds(1000));
                if (decr == true)
                {
                    state.Total = state.Total - 1;
                    Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received DECR event, total is now {state.Total}");
                }
            }
            catch (TaskCanceledException ex)
            {

            }

            context.ContinueAsNew(state, true);
            return true;
        }
    }
}
