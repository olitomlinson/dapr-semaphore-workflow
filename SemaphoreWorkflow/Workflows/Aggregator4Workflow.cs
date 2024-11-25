
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

            var incr = context.WaitForExternalEventAsync<string>("INCR");
            var decr = context.WaitForExternalEventAsync<string>("DECR");

            await Task.WhenAny(incr, decr);

            if (incr.IsCompletedSuccessfully)
            {
                state.Total += 1;
                Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:HH:mm:ss.fffffff}] Received INCR event ({incr.Result}), total is now {state.Total}, seq {state.Seq}");
            }

            if (decr.IsCompletedSuccessfully)
            {
                state.Total -= 1;
                Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:HH:mm:ss.fffffff}] Received DECR event ({decr.Result}), total is now {state.Total}, seq {state.Seq}");
            }

            context.ContinueAsNew(state, true);
            return true;
        }
    }
}
