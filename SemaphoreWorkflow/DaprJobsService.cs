namespace SemaphoreWorkflow
{
    public sealed class DaprJobsService : IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly ILogger<DaprJobsService> logger;

        public DaprJobsService(HttpClient httpClient, ILogger<DaprJobsService> logger)
        {
            this.httpClient = httpClient;
            this.logger = logger;
        }
        public async Task EnsureSemaphoreJobIsRunning(string semaphoreWorkflowId)
        {
            var result = await httpClient.GetAsync(semaphoreWorkflowId);
            logger.LogDebug($"GET job `{semaphoreWorkflowId}` result : {result.StatusCode.ToString()}");
            if (!result.IsSuccessStatusCode)
            {
                var createResult = await httpClient.PostAsJsonAsync(semaphoreWorkflowId, new
                {
                    data = new
                    {
                        scheduled = DateTime.UtcNow
                    },
                    schedule = "@every 10s"
                });
                logger.LogInformation($"CREATE job `{semaphoreWorkflowId}` result : {createResult.StatusCode.ToString()}");
                createResult.EnsureSuccessStatusCode();
            }
        }

        public void Dispose() => httpClient?.Dispose();
    }

}