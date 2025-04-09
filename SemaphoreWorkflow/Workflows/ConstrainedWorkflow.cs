using Dapr.Workflow;
using SemaphoreWorkflow.Activities;

namespace SemaphoreWorkflow.Workflows
{
    public class ConstrainedWorkflow : Workflow<object, string>
    {
        public override async Task<string> RunAsync(WorkflowContext context, object input)
        {
            context.SetCustomStatus("STARTED");

            // 1. let's tell the Sempahore that we want to be told when its our turn to proceeed
            var waitEvent = new WaitEvent() { InstanceId = context.InstanceId, ProceedEventName = "proceed" };
            var proceedEvent = context.WaitForExternalEventAsync<ProceedEvent>(waitEvent.ProceedEventName);
            var r1 = await context.CallActivityAsync<bool>(nameof(RaiseWaitEventActivity), waitEvent);
            var startTime = context.CurrentUtcDateTime;
            context.SetCustomStatus("WAITING_FOR_TURN");

            // 2. START THE CRITICAL SECTION gaurded by the Semaphore workflow
            var r2 = await proceedEvent;
            var endTime = context.CurrentUtcDateTime;
            context.SetCustomStatus("PROCEED");

            // 3. DO THE EXPENSIVE / CONSTRAINED PROCESS
            await context.CallActivityAsync<bool>(nameof(VerySlowActivity), $"{context.InstanceId} - {nameof(VerySlowActivity)} - scheduled={context.CurrentUtcDateTime:HH:mm:ss}");

            // 4. END THE CRITICAL SECTION by Signalling the sempahore workflow that we are done (allowing the semaphore workflow to allow other work to proceed)
            context.SetCustomStatus("DONE");
            var signalEvent = new SignalEvent() { InstanceId = context.InstanceId };
            var r4 = await context.CallActivityAsync<bool>(nameof(RaiseSignalEventActivity), signalEvent);
            context.SetCustomStatus("SIGNALLED");

            // 4. Echo back how long this workflow waited for due to throttling
            return $"workflow throttled for {Math.Round((endTime - startTime).TotalMilliseconds)}ms";
        }
    }
}
