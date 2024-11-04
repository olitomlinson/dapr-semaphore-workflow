using Dapr.Workflow;

namespace SemaphoreWorkflow.Activities
{
    public class VerySlowActivity : WorkflowActivity<string, bool>
    {
        readonly ILogger logger;

        public VerySlowActivity(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<VerySlowActivity>();
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, string message)
        {
            message += $" activated={DateTime.UtcNow.ToString("HH:mm:ss")}";

            await Task.Delay(10000);

            this.logger.LogInformation(message);

            return true;
        }
    }
}