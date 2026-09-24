# Take-Home Assessment: Job Orchestrator

## Overview

Build a small application that accepts job definitions, validates their dependency graphs, and executes their steps through a provided HTTP Worker service.

Your solution must include:

- An ASP.NET Core backend responsible for validation, scheduling, execution, retries, and status tracking.
- An HTTP API for submitting a JSON definition and receiving the final execution result.
- A Dockerfile for the Orchestrator.

Optionally, you may include a simple web interface for uploading a JSON definition, starting an execution, and viewing final job statuses and outputs. A web interface is not required for a complete submission.

You are responsible for implementing the Orchestrator. The Worker is a running HTTP service that we operate.

## Input Document

The application receives one JSON document with the following structure:

```json
{
  "maxParallelism": 4,
  "jobs": [
    {
      "id": "customer-summary",
      "output": "${steps.publish.output}",
      "steps": [
        {
          "id": "load",
          "input": "customer-42"
        },
        {
          "id": "publish",
          "dependsOn": ["load"],
          "input": "value=${steps.load.output}"
        }
      ]
    },
    {
      "id": "audit-export",
      "output": "${steps.export.output}",
      "steps": [
        {
          "id": "read",
          "input": "audit-2026-09"
        },
        {
          "id": "export",
          "dependsOn": ["read"],
          "input": "records=${steps.read.output}"
        }
      ]
    }
  ]
}
```

`input` is optional. It is a string template that may contain zero or more references in this form:

```text
${steps.<step-id>.output}
```

Before a step is submitted to the Worker, every reference in its input must be replaced with the corresponding successful step output.

`dependsOn` is optional and defaults to an empty list.

`output` is optional. When present, it must be exactly one reference to a step output, such as `${steps.publish.output}`. The Orchestrator resolves it after execution and returns it through the API and, if provided, displays it in the web interface.

The document describes dependencies rather than execution order. The order of jobs and steps in the JSON arrays has no execution meaning.

## Validation

Treat submitted documents as untrusted input. Validate the complete document before sending any steps to the Worker, and return useful feedback when a document cannot be executed. Decide which validations are needed for safe and predictable execution, and document any assumptions you make.

## Execution Rules

- All jobs in one submitted document are scheduled together.
- A step becomes eligible only after all its dependencies have succeeded.
- No more than `maxParallelism` Worker calls may be active across the entire submission, including all jobs.
- Resolve each available job output after all jobs reach a terminal state.

Define and document the status model you choose, including how step outcomes determine the status of their job and the overall result.

## Starting an Execution

The Orchestrator must allow execution to be started by sending the input document as the request body of this API endpoint:

```http
POST /api/execute
Content-Type: application/json
```

The endpoint is synchronous: it keeps the request open until every job reaches a terminal state and then returns `200 OK` with this response shape:

```json
{
  "status": "PartiallySucceeded",
  "jobs": [
    {
      "id": "customer-summary",
      "status": "Succeeded",
      "output": "out_abc123"
    },
    {
      "id": "audit-export",
      "status": "Failed",
      "output": null
    }
  ]
}
```

If you provide a web interface, it must use the same validation and orchestration behavior as the API. Do not implement two separate execution paths with different semantics.

## Worker Contract

The Worker exposes one endpoint, `POST /execute`, at the `<WORKER_URL>` base URL. We supply `<WORKER_URL>` separately. Keep the base URL configurable rather than hard-coded in source.

Every request must carry two headers:

- `Authorization: Bearer <WORKER_ACCESS_TOKEN>` - we supply the token separately. Treat it as a credential: keep it out of source control and out of your committed configuration files.
- `X-Candidate` - the e-mail address you applied with. We log it to correlate requests.

A complete request:

```http
POST <WORKER_URL>/execute
Authorization: Bearer <WORKER_ACCESS_TOKEN>
X-Candidate: you@example.com
Content-Type: application/json

{
  "input": "customer-42"
}
```

The successful response:

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "output": "out_cfeb9a25be617c80a862febf784eb5526144906d40b72502fed4bc4b3d1602dc"
}
```

The output is an opaque string. The Orchestrator must not interpret it.

The Worker may also return:

- `400 Bad Request` for an invalid request.
- `401 Unauthorized` when either required header is missing or wrong.
- `429 Too Many Requests` when you exceed the rate limit. The response carries a `Retry-After` header; wait rather than keep hammering the service.
- `503 Service Unavailable` for a transient failure.

The Worker is stateless. Successful calls with identical inputs return identical outputs, but each call has a small independent chance of returning `503` instead.

## Web Interface (Optional)

If you choose to include a web interface, it must allow a user to:

1. Upload a JSON job definition file.
2. Start execution.
3. See the final status and output of each job.

Any additional interface features are at your discretion.

If included, the web interface must be served by the ASP.NET Core Orchestrator. You may choose the frontend technology and visual presentation. A separate frontend source project and build step are allowed, but the resulting application must be hosted by the Orchestrator. A separate frontend process must not be required at runtime.

## Containerization

Provide a Dockerfile for the Orchestrator and document how to build and run it, for example:

```bash
docker build -t job-orchestrator .
docker run --rm -p 8080:8080 job-orchestrator
```

The Orchestrator must reach the Worker through configuration supplied at startup, so the same image works against any Worker URL and token without being rebuilt.

## Testing and Documentation

Include:

- Automated tests for behavior you consider important.
- Clear build, test, and run instructions.
- A short `DESIGN.md` covering:
  - Architecture and important decisions.
  - Assumptions and known limitations.
  - What you would improve with more time.
  - How AI tools were used, if applicable.

AI tools are allowed. You remain responsible for the complete solution and should be prepared to explain and modify any part of it during a follow-up interview.

## Evaluation

We will consider correctness, robustness, design quality, automated testing, usability, and the reasoning documented with the solution.