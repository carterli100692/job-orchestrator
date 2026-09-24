# Job Orchestrator

ASP.NET Core implementation of the take-home Job Orchestrator assessment.

## Prerequisites

- .NET 10 SDK
- Docker Desktop (only required for the container run)
- Git
- VS Code + C# Dev Kit

Verify:

```bash
dotnet --version
docker --version
```

## Build and test

From the repository root:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## Configure the Worker

Do not place the access token in `appsettings.json`.

```powershell
$env:Worker__BaseUrl = "https://worker.example.com"
$env:Worker__AccessToken = "YOUR_TOKEN"
$env:Worker__CandidateEmail = "you@example.com"
```

## Run locally

```bash
dotnet run --project src/JobOrchestrator --urls http://localhost:8080
```

Health check:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:8080/health"
```

Execute the sample document:

```powershell
Invoke-RestMethod \
  -Method Post \
  -Uri "http://localhost:8080/api/execute" \
  -ContentType "application/json" \
  -InFile ".\sample-job.json"
```

## Docker

Build:

```bash
docker build -t job-orchestrator .
```

Run on port 8080:

```powershell
docker run --rm -p 8080:8080 `
  -e Worker__BaseUrl="https://worker.example.com" `
  -e Worker__AccessToken="YOUR_TOKEN" `
  -e Worker__CandidateEmail="you@example.com" `
  job-orchestrator
```

## Expected API behavior

- Valid executable document: waits for all jobs to become terminal and returns `200 OK`.
- Invalid document: returns `400 Bad Request` with a list of `{ path, message }` validation errors and makes no Worker calls.
- Failed dependency: dependent step is blocked and is not submitted to the Worker.
- Independent steps across all jobs compete for the same per-submission `maxParallelism` Worker-call slots.
- `429` respects `Retry-After`; transient failures are retried with capped exponential backoff.

See `DESIGN.md` for architecture, assumptions, status semantics, limitations, and improvement ideas.
