using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;

/// <summary>
/// Check Domain Exists Mapping - Queries Redis for domain existence
/// </summary>
public class CheckDomainExistsMapping : IMapping
{
    public Task<ScriptResponse> InputHandler(WorkflowTask task, ScriptContext context)
    {
        try
        {
            var httpTask = task as HttpTask;
            if (httpTask == null)
            {
                throw new InvalidOperationException("Task must be an HttpTask");
            }

            var domainName = context.Instance?.Data?.domainName;
            
            // Replace {domainName} placeholder in URL
            if (httpTask.Url != null)
            {
                httpTask.SetUrl(httpTask.Url.Replace("{domainName}", domainName?.ToString() ?? ""));
            }

            return Task.FromResult(new ScriptResponse());
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScriptResponse
            {
                Key = "check-domain-error",
                Data = new { error = ex.Message }
            });
        }
    }

    public async Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        try
        {
            // Dapr State Store API returns:
            // - 200 OK with value if key exists
            // - 204 No Content if key doesn't exist
            // - 404 Not Found if key doesn't exist (alternative)
            var statusCode = context.Body?.statusCode ?? 500;
            var responseData = context.Body;
            
            // Domain exists (200 OK with data)
            if (statusCode == 200 && responseData != null)
            {
                return new ScriptResponse
                {
                    Key = "domain-exists",
                    Data = new
                    {
                        domainExists = true,
                        existingDomain = responseData,
                        checkedAt = DateTime.UtcNow
                    },
                    Tags = new[] { "domain", "exists", "redis", "found" }
                };
            }
            // Domain not found (204 No Content or 404 Not Found)
            else if (statusCode == 204 || statusCode == 404)
            {
                return new ScriptResponse
                {
                    Key = "domain-not-exists",
                    Data = new
                    {
                        domainExists = false,
                        existingDomain = (object?)null,  // Explicitly set to null for healthUrl comparison
                        checkedAt = DateTime.UtcNow
                    },
                    Tags = new[] { "domain", "not-exists", "redis", "not-found" }
                };
            }
            // Unexpected status code
            else
            {
                return new ScriptResponse
                {
                    Key = "domain-check-exception",
                    Data = new
                    {
                        domainExists = false,
                        existingDomain = (object?)null,  // Set to null on error
                        error = "Unexpected status code during domain check",
                        statusCode = statusCode,
                        checkedAt = DateTime.UtcNow
                    },
                    Tags = new[] { "domain", "exception", "error" }
                };
            }
        }
        catch (Exception ex)
        {
            return new ScriptResponse
            {
                Key = "domain-check-exception",
                Data = new
                {
                    domainExists = false,
                    existingDomain = (object?)null,  // Set to null on exception
                    error = "Exception during domain check",
                    errorDescription = ex.Message,
                    checkedAt = DateTime.UtcNow
                },
                Tags = new[] { "domain", "exception", "error" }
            };
        }
    }
}

