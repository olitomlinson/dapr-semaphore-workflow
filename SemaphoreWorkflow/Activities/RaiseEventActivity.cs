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

            this.logger.LogInformation($"[ {context.InstanceId} ] Raised {proceed.ProceedEventName.ToUpper()} event to : {proceed.InstanceId} workflow");

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
            var semaphoreWorkflowId = Environment.GetEnvironmentVariable("SEMAPHORE_WORKFLOW_INSTANCE_ID");

            this.logger.LogInformation($"[ {context.InstanceId} ] Raising WAIT event to '{semaphoreWorkflowId}' Semaphore Workflow");

            await _daprWorkflowClient.RaiseEventAsync(semaphoreWorkflowId, "wait", wait);

            this.logger.LogInformation($"[ {context.InstanceId} ] Raised WAIT event to '{semaphoreWorkflowId}' Semaphore Workflow");

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
            var semaphoreWorkflowId = Environment.GetEnvironmentVariable("SEMAPHORE_WORKFLOW_INSTANCE_ID");

            this.logger.LogInformation($"[ {context.InstanceId} ] Raising SIGNAL event to '{semaphoreWorkflowId}' Semaphore Workflow");

            await _daprWorkflowClient.RaiseEventAsync(semaphoreWorkflowId, "signal", signal);

            this.logger.LogInformation($"[ {context.InstanceId} ] Raised SIGNAL event to '{semaphoreWorkflowId}' Semaphore Workflow");

            return true;
        }
    }
}