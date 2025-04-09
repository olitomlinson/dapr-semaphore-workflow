### Introduction

This example contains 2 Workflows Types
- Semaphore Workflow (`SemaphoreWorkflow/Workflows/SemaphoreWorkflow.cs`)
- Constrained Workflow (`SemaphoreWorkflow/Workflows/ConstrainedWorkflow.cs`)


| Workflow Type | Purpose |
| --- | --- |
| **Semaphore Workflow** | Contains a Semaphore implementation, which has been adapted to use the Dapr Workflows APIs. The semaphore ensures a target maximum level of concurrency is not* breached by any Constrained Workflow that integrates with it. *The Semaphore Workflow has a deadlock recovery mechanism, which will temporarly exceed the max level of concurrency. See `DefaultTTLInSeconds` below for more information. |
| **Constrained Workflow** | This is mock workflow purely used for demonstrating the capability of the Semaphore Workflow. Inside the Constrained Workflow you will see how it interacts with the Sempahore Workflow to create a critical section in the code which is gated by the capability of the Semaphore Workflow. |

---

### Run the example

`docker compose build`
`docker compose up`

As part of the start-up process of the App, an instance of the Semaphore Workflow will be created automatically. The default Id of the Semaphore workflow instance is simply `my-special-semaphore` -- this can be changed in the `.env` file.

After start-up, observe the logs coming from the App. When you see output `Health is good` , the Semaphore Workflow instance is ready and waiting.

### Get the status of the Sempahore Workflow

```shell
curl --request GET \
  --url http://localhost:5116/semaphore/status \
  --header 'User-Agent: insomnia/10.0.0'
```

#### Response

```json
{
	"summary": {
		"activeWaits": 0,
		"pendingWaits": 0,
		"completedWaits": 0
	},
	"config": {
		"maxConcurrency": 10,
		"defaultTTLInSeconds": 1000,
		"logLevel": 1,
		"logCompletedWaits": true
	},
	"logs": [
    ]
}
```

### Run many Constrained Workflows

The example below will start 20 workflows instances, with 500ms wait between starting each workflow

```shell
curl --request POST \
  --url 'http://localhost:5116/bulk-schedule-constrained-workflows?count=20&sleep=500' \
  --header 'User-Agent: insomnia/10.0.0'
```
#### Response

```json
{
	"session": "eyJJdGVtMSI6IjQwNGQ4NzgwIiwiSXRlbTIiOjIwfQ==",
	"instanceIds": [
		"404d8780-0",
		"404d8780-1",
	]
}
```

### Query the status of the Constrained Workflows

Notice how the `session` value from the HTTP Response of the previous example is used as a query param on the call below

```shell
curl --request GET \
  --url 'http://localhost:5116/check?session=eyJJdGVtMSI6IjQwNGQ4NzgwIiwiSXRlbTIiOjIwfQ%3D%3D' \
  --header 'User-Agent: insomnia/10.0.0'
```

Once a Constrained Workflow is complete, the output of the Constrained Workflow will specify how much additional time was added to the overal duration while it was waiting due to being throttled.

The minimum time for throttling in this Docker Compose environment is around 5ms - 20ms. However, in a real-world scenario, this will depend entirely on the performance of your compute, network and state store.

#### Response
```json
[
	{
		"id": "404d8780-0",
		"rtstatus": 1,
		"status": "SIGNALLED",
		"output": "workflow throttled for 16ms"
	},
	{
		"id": "404d8780-1",
		"rtstatus": 1,
		"status": "SIGNALLED",
		"output": "workflow throttled for 7ms"
	}
]
```

### Customising the Semaphore configuration

| Property | Impact |
| --- | --- | 
| **MaxConcurrency** | Increase or decrease this property to change the max level of concurrency |
| **DefaultTTLInSeconds** | This implementation bias towards temporarily allowing exceeding the maximum concurrency limit, versus, the Semaphore become irrecoverably deadlocked, which, is likely the most favourable outcome for a majority of use-cases. By default, if the Semaphore Workflow doesn't receive a `Signal` event for 120 seconds, it will be auto-signalled. This is to ensure that the Semaphore workflow doesn't become deadlocked waiting on signals that will perhaps never arrive. The TTL can be adjusted to any value which matches the use-case at hand. |
| **logLevel** | The *default* is `Info` (1) but can be set to `Debug` (0) |
| **LogCompletedWaits** | The *default* is `true`. Set to `false` to stop maintaining a log of all completed waits. Probably best to set this to `false` in production to prevent an ever growing collection. |

```shell
curl --request POST \
  --url http://localhost:3500/v1.0/workflows/dapr/my-special-semaphore/raiseEvent/adjust \
  --header 'Content-Type: application/json' \
  --header 'User-Agent: insomnia/10.0.0' \
  --data '{
	"MaxConcurrency":3,
	"DefaultTTLInSeconds":120,
	"logLevel":0,
	"LogCompletedWaits":false
}'
```

> [!IMPORTANT]
> There is a Developer Experience limitation in the current version of Dapr Workflows which prevents one workflow from raising an Event directly to another Workflow. Therefore more boilerplate code is used to work around this. In a future version of Dapr Workflows, one would not need this additional boiler plate so bare that in mind when reviewing this solution. https://github.com/dapr/dapr/issues/8243

