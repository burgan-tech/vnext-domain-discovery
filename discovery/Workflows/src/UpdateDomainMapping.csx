using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;

/// <summary>
/// Update Domain Mapping - Updates existing domain in Redis
/// </summary>
public class UpdateDomainMapping : IMapping
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
            var baseUrl = context.Instance?.Data?.baseUrl;
            var healthUrl = context.Instance?.Data?.healthUrl;
            var appId = context.Instance?.Data?.appId;
            // var flows = context.Instance?.Data?.flows;
            var existingDomain = context.Instance?.Data?.existingDomain;

            // Merge existing domain data with updates
            var updateData = new Dictionary<string, dynamic>();
            
            // Copy existing data
            if (existingDomain != null)
            {
                if (existingDomain.name != null) updateData["name"] = existingDomain.name;
                if (existingDomain._baseUrl != null) updateData["_baseUrl"] = existingDomain._baseUrl;
                if (existingDomain._healthUrl != null) updateData["_healthUrl"] = existingDomain._healthUrl;
                if (existingDomain._appId != null) updateData["_appId"] = existingDomain._appId;
                if (existingDomain._isHealthy != null) updateData["_isHealthy"] = existingDomain._isHealthy;
                // if (existingDomain.flows != null) updateData["flows"] = existingDomain.flows;
            }
            
            // Apply updates (only if provided)
            if (baseUrl != null)
                updateData["_baseUrl"] = baseUrl;
            
            if (healthUrl != null)
                updateData["_healthUrl"] = healthUrl;
            
            if (appId != null)
                updateData["_appId"] = appId;
            
            // if (flows != null)
            //     updateData["flows"] = flows;
            
            updateData["_updatedAt"] = DateTime.UtcNow.ToString("o");
            
            // Ensure name is set
            if (domainName != null)
                updateData["name"] = domainName;

            // Prepare state item for Dapr State Store API
            var stateItem = new
            {
                key = $"domain:{domainName}",
                value = updateData
            };

            // Set body for HTTP POST request
            httpTask.SetBody(new[] { stateItem });

            return Task.FromResult(new ScriptResponse());
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScriptResponse
            {
                Key = "update-domain-error",
                Data = new { error = ex.Message }
            });
        }
    }

    public async Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        try
        {
            // Dapr State Store API returns 204 No Content on success
            // Check HTTP status code from context
            var statusCode = context.Body?.statusCode ?? 500;
            var isSuccess = statusCode >= 200 && statusCode < 300;

            // Successful update (204 No Content is success for state store)
            if (isSuccess || statusCode == 204)
            {
                return new ScriptResponse
                {
                    Key = "domain-updated",
                    Data = new
                    {
                        domainRegistration = new
                        {
                            success = true,
                            operation = "update",
                            updatedAt = DateTime.UtcNow
                        }
                    },
                    Tags = new[] { "domain", "updated", "redis", "success" }
                };
            }
            // Update failed
            else
            {
                return new ScriptResponse
                {
                    Key = "domain-update-failed",
                    Data = new
                    {
                        domainRegistration = new
                        {
                            success = false,
                            operation = "update",
                            error = "Failed to update domain",
                            statusCode = statusCode,
                            updatedAt = DateTime.UtcNow
                        }
                    },
                    Tags = new[] { "domain", "update-failed", "redis", "error" }
                };
            }
        }
        catch (Exception ex)
        {
            return new ScriptResponse
            {
                Key = "domain-update-exception",
                Data = new
                {
                    domainRegistration = new
                    {
                        success = false,
                        operation = "update",
                        error = "Exception during domain update",
                        errorDescription = ex.Message,
                        updatedAt = DateTime.UtcNow
                    }
                },
                Tags = new[] { "domain", "exception", "error" }
            };
        }
    }
}

