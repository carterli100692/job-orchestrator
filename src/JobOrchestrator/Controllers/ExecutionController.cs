using JobOrchestrator.Execution;
using JobOrchestrator.Models;
using Microsoft.AspNetCore.Mvc;

namespace JobOrchestrator.Controllers;

[ApiController]
[Route("api")]
public sealed class ExecutionController : ControllerBase
{
    private readonly IExecutionCoordinator _coordinator;

    public ExecutionController(IExecutionCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [HttpPost("execute")]
    [ProducesResponseType<ExecutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Execute(
        [FromBody] ExecutionDefinition definition,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.ExecuteAsync(definition, cancellationToken);

        if (!result.IsValid)
        {
            return BadRequest(new ValidationErrorResponse("Invalid job definition", result.ValidationErrors));
        }

        return Ok(result.Response);
    }
}
