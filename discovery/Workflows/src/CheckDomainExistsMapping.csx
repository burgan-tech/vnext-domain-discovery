using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;
using BBT.Workflow.Scripting.Functions;

/// <summary>
/// Check Domain Exists Mapping - Reads the "domain" lifecycle instance keyed by domainName
/// using GetInstanceDataTask (task type 13) inside the discovery domain.
/// "404" is declared in the task config's acceptedStatusCodes, so a missing instance is a
/// normal result rather than an ErrorBoundary trigger.
///
/// Response envelope: { "isSuccess": true, "data": { "data": { ... }, "eTag": "W/\"...\"", "extensions": {} } }
/// The Data function may also flatten the payload, so both shapes are handled below.
///
/// NOTE: the tag property is "eTag" (capital T), not "etag". Reading the wrong casing on a
/// dynamic throws RuntimeBinderException instead of returning null - see Safe() below.
/// </summary>
public class CheckDomainExistsMapping : ScriptBase, IMapping
{
    private const string TargetDomain = "discovery";
    private const string TargetFlow = "domain";

    /// <summary>
    /// Reads a dynamic member without throwing. "?." only guards a null receiver; an absent
    /// member on an ExpandoObject / JsonElement-backed dynamic raises RuntimeBinderException.
    /// Every read of context.Body / context.Instance.Data must go through this.
    /// </summary>
    private static object? Safe(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? SafeStr(Func<object?> read)
    {
        var value = Safe(read)?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool SafeBool(Func<object?> read)
    {
        var value = Safe(read);
        if (value is bool flag)
            return flag;
        return bool.TryParse(value?.ToString(), out var parsed) && parsed;
    }

    private static int? SafeInt(Func<object?> read)
    {
        var value = Safe(read);
        if (value == null)
            return null;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : (int?)null;
    }

    public Task<ScriptResponse> InputHandler(WorkflowTask task, ScriptContext context)
    {
        var getInstanceDataTask = task as GetInstanceDataTask;
        if (getInstanceDataTask == null)
        {
            throw new InvalidOperationException("Task must be a GetInstanceDataTask");
        }

        var domainName = SafeStr(() => context.Instance?.Data?.domainName);
        if (string.IsNullOrWhiteSpace(domainName))
        {
            // Fail loudly on purpose: with neither key nor instanceId set the engine falls back
            // to the CURRENT instance id and would read this workflow's own instance.
            throw new InvalidOperationException(
                "domainName is required to resolve the target domain lifecycle instance");
        }

        getInstanceDataTask.SetDomain(TargetDomain);
        getInstanceDataTask.SetFlow(TargetFlow);
        getInstanceDataTask.SetKey(domainName);

        LogInformation($"check-domain-exists -> {TargetDomain}/{TargetFlow} key={domainName}");

        return Task.FromResult(new ScriptResponse());
    }

    public Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        try
        {
            var isSuccess = SafeBool(() => context.Body?.isSuccess)
                         || SafeBool(() => context.Body?.IsSuccess);
            var statusCode = SafeInt(() => context.Body?.statusCode)
                          ?? SafeInt(() => context.Body?.StatusCode);
            var errorCode = SafeStr(() => context.Body?.metadata?.errorCode)
                         ?? SafeStr(() => context.Body?.Metadata?.ErrorCode);
            var errorMessage = SafeStr(() => context.Body?.errorMessage)
                            ?? SafeStr(() => context.Body?.ErrorMessage);

            var notFound = statusCode == 404 || errorCode == "INSTANCE_NOT_FOUND";
            var payloadPresent = Safe(() => context.Body?.data) != null;

            // Instance found
            if (!notFound && isSuccess && payloadPresent)
            {
                // "eTag" is the documented casing; the instance envelope of GetInstance
                // responses uses lowercase "etag", so accept both.
                var eTag = SafeStr(() => context.Body?.data?.eTag)
                        ?? SafeStr(() => context.Body?.data?.etag);

                // The payload is either { data: {...}, eTag, extensions } or the flattened
                // instance data itself. Probe the nested shape first.
                var existingHealthUrl = SafeStr(() => context.Body?.data?.data?.healthUrl)
                                     ?? SafeStr(() => context.Body?.data?.healthUrl);
                var existingBaseUrl = SafeStr(() => context.Body?.data?.data?.baseUrl)
                                   ?? SafeStr(() => context.Body?.data?.baseUrl);
                var existingAppId = SafeStr(() => context.Body?.data?.data?.appId)
                                 ?? SafeStr(() => context.Body?.data?.appId);

                return Task.FromResult(new ScriptResponse
                {
                    Key = "domain-exists",
                    Data = new
                    {
                        domainExists = true,
                        instanceETag = eTag,
                        existingHealthUrl = existingHealthUrl,
                        existingBaseUrl = existingBaseUrl,
                        existingAppId = existingAppId,
                        checkedAt = DateTime.UtcNow
                    },
                    Tags = new[] { "domain", "exists", "workflow", "instance", "found" }
                });
            }

            // Instance does not exist
            if (notFound)
            {
                return Task.FromResult(new ScriptResponse
                {
                    Key = "domain-not-exists",
                    Data = new
                    {
                        domainExists = false,
                        instanceETag = (string?)null,
                        existingHealthUrl = (string?)null,
                        existingBaseUrl = (string?)null,
                        existingAppId = (string?)null,
                        checkedAt = DateTime.UtcNow
                    },
                    Tags = new[] { "domain", "not-exists", "workflow", "instance", "not-found" }
                });
            }

            // Anything else - unexpected response
            return Task.FromResult(new ScriptResponse
            {
                Key = "domain-check-exception",
                Data = new
                {
                    domainExists = false,
                    instanceETag = (string?)null,
                    existingHealthUrl = (string?)null,
                    existingBaseUrl = (string?)null,
                    existingAppId = (string?)null,
                    error = "Unexpected response during domain instance check",
                    errorDescription = errorMessage ?? errorCode,
                    statusCode = statusCode,
                    checkedAt = DateTime.UtcNow
                },
                Tags = new[] { "domain", "exception", "error" }
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScriptResponse
            {
                Key = "domain-check-exception",
                Data = new
                {
                    domainExists = false,
                    instanceETag = (string?)null,
                    existingHealthUrl = (string?)null,
                    existingBaseUrl = (string?)null,
                    existingAppId = (string?)null,
                    error = "Exception during domain instance check",
                    errorDescription = ex.Message,
                    checkedAt = DateTime.UtcNow
                },
                Tags = new[] { "domain", "exception", "error" }
            });
        }
    }
}
