using Dapr.Workflow;
using SemaphoreWorkflow.Workflows;

namespace SemaphoreWorkflow.Activities
{
    public class RaiseProceedEventActivity : WorkflowActivity<ProceedEvent, bool>
    {
        readonly ILogger logger;
        readonly DaprWorkflowClient _daprWorkflowClient;

        public RaiseProceedEventActivity(ILoggerFactory loggerFactory, DaprWorkflowClient daprWorkflowClient)
        {
            this.logger = loggerFactory.CreateLogger<RaiseProceedEventActivity>();
            this._daprWorkflowClient = daprWorkflowClient;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, ProceedEvent proceed)
        {
            this.logger.LogInformation($"[ {context.InstanceId} ] Raising {proceed.ProceedEventName.ToUpper()} event to : {proceed.InstanceId} workflow");

            await _daprWorkflowClient.RaiseEventAsync(proceed.InstanceId, proceed.ProceedEventName, proceed);

            return true;
        }
    }

    public class RaiseWaitEventActivity : WorkflowActivity<WaitEvent, bool>
    {
        readonly ILogger logger;
        readonly DaprWorkflowClient _daprWorkflowClient;

        public RaiseWaitEventActivity(ILoggerFactory loggerFactory, DaprWorkflowClient daprWorkflowClient)
        {
            this.logger = loggerFactory.CreateLogger<RaiseWaitEventActivity>();
            this._daprWorkflowClient = daprWorkflowClient;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, WaitEvent wait)
        {
            this.logger.LogInformation($"{context.InstanceId} ] Raising WAIT event to 'throttle' workflow");

            await _daprWorkflowClient.RaiseEventAsync("throttle", "wait", wait);

            return true;
        }
    }

    public class RaiseSignalEventActivity : WorkflowActivity<SignalEvent, bool>
    {
        readonly ILogger logger;
        readonly DaprWorkflowClient _daprWorkflowClient;

        public RaiseSignalEventActivity(ILoggerFactory loggerFactory, DaprWorkflowClient daprWorkflowClient)
        {
            this.logger = loggerFactory.CreateLogger<RaiseSignalEventActivity>();
            this._daprWorkflowClient = daprWorkflowClient;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, SignalEvent signal)
        {
            this.logger.LogInformation($"[ {context.InstanceId} ] Raising SIGNAL event to 'throttle' workflow");

            await _daprWorkflowClient.RaiseEventAsync("throttle", "signal", signal);

            return true;
        }
    }
}