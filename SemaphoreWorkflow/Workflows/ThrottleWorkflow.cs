using System.Collections.Concurrent;
using Dapr.Workflow;
using SemaphoreWorkflow.Activities;

namespace SemaphoreWorkflow.Workflows
{
    public class ThrottleWorkflow : Workflow<ThrottleState, bool>
    {
        private void Log(ThrottleState state, LogLevel logLevel, string message)
        {
            if (logLevel >= state.RuntimeConfig.logLevel)
                state.PersistentLog.Add(message);
        }

        public override async Task<bool> RunAsync(WorkflowContext context, ThrottleState state)
        {
            if (state == null)
            {
                state = new ThrottleState();
                context.ContinueAsNew(state, true);
                return true;
            }

            #region Optimisations & Deadlock handling

            // Step 1. Scan for expired waits...

            // Sometimes a downstream workflow will not send it's signal, the consequence of this happening is that
            // eventually with enough failures, the semaphore will become blocked and no new downstream workflows will 
            // be able to progress.

            // So, every 15s we can scan the 'activeWaits' and if it has exceeded its ttl (default 30s) then a virtual signal 
            // is injected to unblock the sempahore.

            // note: we only do the Expiry check on every second itteration of the workflow (state.DoExpiryScan toggles 
            // between false and true on each itteration) - this is because during times where lots of waits are expiring, we 
            // want to occasionally bypass the expiry check and make sure the semaphore is actually running at max 
            // capacity (Step 2 & 3), rather than becoming flooded with only handling expired waits.
            await PerformExpiryScan(context, state);

            // Step 2. Handle any signals first... (which will free-up capacity in the semaphore for step 3)
            if (await HandlePendingSignals(context, state))
            {
                // Technically, we don't have ContinueAsNew Here, but it allows us to do a 
                // GET on the workflow and see the state accurately
                context.ContinueAsNew(state, true);
                return true;
            }
            #endregion

            // Step 3. Ensure that enough work is active (up to the Max Concurrency limit)
            await SendProceedEvents(context, state);

            // Step 4. register runtime to wait for various external events...
            var (wait, signal, adjust, clearLogs, expiryScan, expiryScanCts) = CreateSignals(context);

            context.SetCustomStatus(new ThrottleSummary
            {
                ActiveWaits = state.ActiveWaits.Count(),
                PendingWaits = state.PendingWaits.Count(),
                CompletedWaits = state.CompletedWaits.Count()
            });

            var winner = await Task.WhenAny(wait, signal, adjust, clearLogs, expiryScan);
            if (winner == wait)
            {
                expiryScanCts.Cancel();
                #region Expiry handling
                if (state.RuntimeConfig.DefaultTTLInSeconds > 0 && !wait.Result.Expiry.HasValue)
                    wait.Result.Expiry = context.CurrentUtcDateTime.AddSeconds(state.RuntimeConfig.DefaultTTLInSeconds);
                #endregion
                state.PendingWaits.Enqueue(wait.Result);
                Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received WAIT event from {wait.Result.InstanceId} workflow");
            }
            else if (winner == signal)
            {
                expiryScanCts.Cancel();
                state.PendingSignals.Enqueue(signal.Result);
                Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Received SIGNAL event from {signal.Result.InstanceId} workflow");
            }
            else if (winner == adjust)
            {
                expiryScanCts.Cancel();
                state.RuntimeConfig = adjust.Result;
            }
            else if (winner == clearLogs)
            {
                expiryScanCts.Cancel();
                state.PersistentLog = [];
                Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Logs cleared manually");
            }
            else if (winner == expiryScan)
            { // no-op 
            }
            else
                throw new Exception("unknown event");

            context.ContinueAsNew(state, true);
            return true;
        }

        private async Task SendProceedEvents(WorkflowContext context, ThrottleState state)
        {
            while (state.PendingWaits.Any() &&
                            (state.ActiveWaits.Count() < state.RuntimeConfig.MaxConcurrency))
            {
                WaitEvent waitEvent = state.PendingWaits.Dequeue();
                state.ActiveWaits.TryAdd(waitEvent.InstanceId, waitEvent);

                // https://github.com/dapr/dapr/issues/8243   
                // context.SendEvent(waitEvent.InstanceId, waitEvent.ProceedEventName, null);
                var proceedEvent = new ProceedEvent()
                {
                    InstanceId = waitEvent.InstanceId,
                    ProceedEventName = waitEvent.ProceedEventName,
                };
                Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Sending {proceedEvent.ProceedEventName} event to {proceedEvent.InstanceId} workflow");
                var r1 = await context.CallActivityAsync<bool>(nameof(RaiseProceedEventActivity), proceedEvent);
                Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Sent {proceedEvent.ProceedEventName} event to {proceedEvent.InstanceId} workflow");
            }
            Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] Active {state.ActiveWaits.Count}, Pending {state.PendingWaits.Count}");
        }
        private static (Task<WaitEvent> wait, Task<SignalEvent> signal, Task<RuntimeConfig> adjust, Task<bool> clearLogs, Task expiryScan, CancellationTokenSource cts) CreateSignals(WorkflowContext context)
        {
            var wait = context.WaitForExternalEventAsync<WaitEvent>("wait");
            var signal = context.WaitForExternalEventAsync<SignalEvent>("signal");
            var adjust = context.WaitForExternalEventAsync<RuntimeConfig>("adjust");
            var clearLogs = context.WaitForExternalEventAsync<bool>("clearlogs");
            var expiryScanCts = new CancellationTokenSource();
            var expiryScan = context.CreateTimer(TimeSpan.FromSeconds(15), expiryScanCts.Token);

            return (wait, signal, adjust, clearLogs, expiryScan, expiryScanCts);
        }

        private async Task<bool> HandlePendingSignals(WorkflowContext context, ThrottleState state)
        {
            while (state.PendingSignals.Any())
            {
                var signal1 = state.PendingSignals.Dequeue();
                state.ActiveWaits.Remove(signal1.InstanceId, out WaitEvent _);
                if (state.RuntimeConfig.LogCompletedWaits)
                    state.CompletedWaits.Add(signal1.InstanceId, context.CurrentUtcDateTime);

                // return true if all new pending signals have been processed
                if (!state.PendingSignals.Any())
                    return true;
            }
            return false;
        }
        private async Task PerformExpiryScan(WorkflowContext context, ThrottleState state)
        {
            if (state.DoExpiryScan)
            {
                state.DoExpiryScan = false;
                Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] [Expiry Check] Starting");
                var expiryWatermark = context.CurrentUtcDateTime;
                foreach (var activeWait in state.ActiveWaits
                    .Where(x => x.Value.Expiry.HasValue)
                    .Where(x => expiryWatermark > x.Value.Expiry))
                {
                    state.PendingSignals.Enqueue(new SignalEvent() { InstanceId = activeWait.Key });
                    Log(state, LogLevel.Info, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] [Expiry Check] Active wait for {activeWait.Key} has expired [ts-now: {expiryWatermark}, ts-expiry: {activeWait.Value.Expiry.Value}, delta: {activeWait.Value.Expiry.Value.Subtract(expiryWatermark).TotalSeconds} seconds]");
                }
            }
            else
            {
                state.DoExpiryScan = true;
                Log(state, LogLevel.Debug, $"[{context.CurrentUtcDateTime:yyyy-MM-dd HH:mm:ss}] [Expiry Check] Skipped");
            }
        }
    }

    public class WaitEvent
    {
        public string InstanceId { get; set; }

        public string ProceedEventName { get; set; }

        public DateTime? Expiry { get; set; }
    }

    public class SignalEvent
    {
        public string InstanceId { get; set; }
    }

    public class ProceedEvent
    {
        public string InstanceId { get; set; }

        public string ProceedEventName { get; set; }
    }

    public class ThrottleState
    {
        public RuntimeConfig RuntimeConfig { get; set; } = new RuntimeConfig();
        public Queue<WaitEvent> PendingWaits { get; set; } = new Queue<WaitEvent>();
        public ConcurrentDictionary<string, WaitEvent> ActiveWaits { get; set; } = new ConcurrentDictionary<string, WaitEvent>();
        public Queue<SignalEvent> PendingSignals { get; set; } = new Queue<SignalEvent>();
        public Dictionary<string, DateTime> CompletedWaits { get; set; } = new Dictionary<string, DateTime>();
        public List<string> PersistentLog { get; set; } = new List<string>();
        public bool DoExpiryScan { get; set; }
    }

    public class RuntimeConfig
    {
        public int MaxConcurrency { get; set; } = 10;

        public int DefaultTTLInSeconds { get; set; } = 1000;

        public LogLevel logLevel { get; set; } = LogLevel.Info;

        public bool LogCompletedWaits { get; set; } = true;
    }

    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
    }

    public class ThrottleSummary
    {
        public int ActiveWaits { get; set; }
        public int PendingWaits { get; set; }
        public int CompletedWaits { get; set; }
    }
}
