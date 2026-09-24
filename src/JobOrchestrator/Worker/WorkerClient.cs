using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace JobOrchestrator.Worker;

public sealed class WorkerClient : IWorkerClient
{
    private readonly HttpClient _httpClient;
    private readonly WorkerOptions _options;

    public WorkerClient(HttpClient httpClient, IOptions<WorkerOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<WorkerAttemptResult> ExecuteOnceAsync(string? input, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "execute")
        {
            Content = JsonContent.Create(new WorkerRequest(input))
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Candidate", _options.CandidateEmail);

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadFromJsonAsync<WorkerResponse>(cancellationToken: cancellationToken);
                if (body?.Output is null)
                {
                    return WorkerAttemptResult.Permanent("Worker returned 200 OK without an output value.");
                }

                return WorkerAttemptResult.Success(body.Output);
            }

            if ((int)response.StatusCode == 429)
            {
                return WorkerAttemptResult.Retryable(
                    "Worker rate limit reached (429).",
                    GetRetryAfter(response.Headers.RetryAfter));
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable || (int)response.StatusCode >= 500)
            {
                return WorkerAttemptResult.Retryable($"Worker returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                return WorkerAttemptResult.Retryable("Worker returned 408 Request Timeout.");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = string.IsNullOrWhiteSpace(responseBody)
                ? $"Worker returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : $"Worker returned {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}";

            return WorkerAttemptResult.Permanent(detail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WorkerAttemptResult.Retryable("Worker request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return WorkerAttemptResult.Retryable($"Worker request failed: {ex.Message}");
        }
    }

    private static TimeSpan? GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private sealed record WorkerRequest(string? Input);
    private sealed record WorkerResponse(string Output);
}
