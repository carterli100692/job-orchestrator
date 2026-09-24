# Job Orchestrator - Design

## Architecture

The application has four small responsibilities:

1. **API** - `POST /api/execute` accepts the complete document and returns either validation errors (`400`) or the final execution response (`200`).
2. **Validation** - validates the whole document before execution. This prevents partial side effects caused by discovering an invalid graph after Worker calls have already started.
3. **Execution coordinator** - creates one asynchronous task per step, waits for dependency tasks, and gates each actual Worker HTTP attempt with one submission-wide `SemaphoreSlim`. This naturally schedules independent steps from all jobs together while enforcing the global `maxParallelism` limit.
4. **Worker client** - owns the HTTP protocol details: URL, bearer token, `X-Candidate`, request/response JSON, and classification of HTTP failures.

## Validation rules and assumptions

The document is treated as untrusted input. The implementation validates:

- `maxParallelism > 0`.
- At least one job and at least one step per job.
- Job ids are unique within the document.
- Step ids are unique within each job.
- Ids are 1-100 characters and use letters, digits, `_`, or `-`, beginning with a letter or digit. This keeps `${steps.<id>.output}` parsing unambiguous.
- Every dependency names an existing step in the same job.
- A dependency list cannot contain duplicates or a self-dependency.
- Each job's dependency graph is acyclic.
- Every input step reference is syntactically valid and names an existing step.
- An input may reference only a direct or transitive dependency. This guarantees the referenced output exists before the step becomes eligible.
- A job `output`, when present, is exactly one valid step output reference and names a step in that job.

The JSON array order is ignored.

## Status model

Internal step states are:

- `Succeeded` - Worker returned a successful output.
- `Failed` - a non-retryable Worker failure occurred, or retry attempts were exhausted.
- `Blocked` - at least one dependency did not succeed, so the step was never sent to the Worker.

A job is `Succeeded` only when all of its steps succeed. Otherwise it is `Failed`.

Overall submission status:

- `Succeeded` - every job succeeded.
- `Failed` - no job succeeded.
- `PartiallySucceeded` - at least one job succeeded and at least one job failed.

A configured job output is returned whenever its referenced step succeeded, even if another unrelated step in the same job failed. This follows the requirement to resolve each *available* job output after terminal states are reached.

## Scheduling and parallelism

All jobs are started together. A step task awaits all dependency tasks before becoming eligible. Immediately before each HTTP attempt, the task acquires a shared `SemaphoreSlim` whose size is `maxParallelism`.

The permit is released as soon as the HTTP attempt finishes. Retry delays do **not** hold a permit, because `maxParallelism` limits active Worker calls, not sleeping steps.

## Retry policy

- `200` - success.
- `400` / `401` and other non-timeout 4xx responses - permanent failure, no retry.
- `429` - retry; honor `Retry-After` when present.
- `503`, other 5xx responses, `408`, network failures, and client-side HTTP timeouts - retry.
- Default maximum is 4 attempts.
- Without `Retry-After`, use capped exponential backoff (250ms, 500ms, 1000ms, ... up to 5s by default).

Retry configuration is configurable through the `Worker` configuration section.

## Configuration and secrets

The Worker base URL, access token, and candidate email are supplied at runtime. The access token is not committed to source control.

Environment variables:

- `Worker__BaseUrl`
- `Worker__AccessToken`
- `Worker__CandidateEmail`
- `Worker__MaxAttempts` (optional)
- `Worker__InitialRetryDelayMilliseconds` (optional)
- `Worker__MaxRetryDelayMilliseconds` (optional)
- `Worker__RequestTimeoutSeconds` (optional)

## Known limitations

- Execution is in-memory. If the process restarts while a request is running, that execution is lost.
- The API is intentionally synchronous because the assignment requires it. Very long workflows would be better modeled as asynchronous execution with an execution id and durable storage.
- There is no optional web UI; effort was focused on correctness, testability, and API behavior.
- The retry policy is intentionally small and deterministic. In production, I would add bounded jitter, metrics, distributed tracing, and a server-wide rate limiter if multiple submissions can run concurrently.
- `maxParallelism` is enforced per submitted document, exactly as requested. Multiple simultaneous API submissions each receive their own limit. If the Worker has a shared global quota, a process-wide limiter would be an additional production concern.

## What I would improve with more time

- Add integration tests using an in-process fake HTTP Worker to verify headers and `Retry-After` behavior end-to-end.
- Add OpenTelemetry traces/metrics for queue time, Worker latency, retries, failures, and active calls.
- Add structured error codes in addition to human-readable validation messages.
- Add request body size limits and operational limits (maximum jobs/steps) based on agreed product constraints.
- Add the optional upload/results UI served by the same ASP.NET Core process.
- For production-scale workflows, persist execution state and expose asynchronous APIs.
