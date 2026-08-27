using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;
using BBT.Workflow.Scripting.Functions;

/// <summary>
/// Domain Lookup Mapping - read path for the cached domain registry lookup.
///
/// Wraps GetInstanceDataTask (task type 13). The requested domain arrives as the "key" query
/// parameter (domain-scoped function, so there is no ambient instance) and becomes the target
/// instance key: the "domain" lifecycle instance IS keyed by domainName - see
/// Workflows/src/RegisterDomainLifecycleMapping.csx, which calls startTask.SetKey(domainName).
///
/// Caching is NOT done here. It is declared on the function itself (attributes.cache) and served
/// by the runtime's read-through function cache: on a HIT this task never runs, so the instance
/// store is never touched. A CacheAsideTask cannot do this job - its source task envelope is built
/// from the task definition alone (no mapping runs for it), so the per-request instance key can
/// never reach the source task.
///
/// "404" is declared in the task config's acceptedStatusCodes, so an unregistered domain is a
/// normal result rather than an ErrorBoundary trigger; the function's output handler turns it into
/// an HTTP 404 response.
/// </summary>
public class DomainLookupMapping : ScriptBase, IMapping
{
    private const string TargetDomain = "discovery";
    private const string TargetFlow = "domain";

    /// <summary>
    /// Reads a dynamic member without throwing. "?." only guards a null receiver; an absent
    /// member on an ExpandoObject / JsonElement-backed dynamic raises RuntimeBinderException.
    /// Every read of context.Body / context.QueryParameters must go through this.
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

    /// <summary>
    /// Resolves the requested domain name from the request.
    ///
    /// "key" is the primary parameter and the ONLY one the function cache key expression reads
    /// (Dynamic Expresso cannot express fallbacks). A request that arrives on a fallback source
    /// still works, it just does not get cached - see domain-lookup.json.
    /// </summary>
    public static string? ResolveRequestedDomain(ScriptContext context)
    {
        return SafeStr(() => context.QueryParameters?["key"])
            ?? SafeStr(() => context.QueryParameters?["domainName"])
            ?? SafeStr(() => context.Headers?["x-domain-name"]);
    }

    public Task<ScriptResponse> InputHandler(WorkflowTask task, ScriptContext context)
    {
        var getInstanceDataTask = task as GetInstanceDataTask;
        if (getInstanceDataTask == null)
        {
            throw new InvalidOperationException("Task must be a GetInstanceDataTask");
        }

        var domainName = ResolveRequestedDomain(context);
        if (string.IsNullOrWhiteSpace(domainName))
        {
            // Fail loudly on purpose: with neither key nor instanceId set the task would fail
            // deeper in the engine with a far less obvious message.
            throw new InvalidOperationException(
                "A 'key' query parameter (domain name) is required for domain-lookup");
        }

        getInstanceDataTask.SetDomain(TargetDomain);
        getInstanceDataTask.SetFlow(TargetFlow);
        getInstanceDataTask.SetKey(domainName);

        LogInformation($"domain-lookup -> {TargetDomain}/{TargetFlow} key={domainName}");

        return Task.FromResult(new ScriptResponse());
    }

    public Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        var requested = ResolveRequestedDomain(context);

        try
        {
            var isSuccess = SafeBool(() => context.Body?.isSuccess)
                         || SafeBool(() => context.Body?.IsSuccess);
            var statusCode = SafeInt(() => context.Body?.statusCode)
                          ?? SafeInt(() => context.Body?.StatusCode);
            var errorCode = SafeStr(() => context.Body?.metadata?.errorCode)
                         ?? SafeStr(() => context.Body?.Metadata?.ErrorCode);

            var notFound = statusCode == 404 || errorCode == "INSTANCE_NOT_FOUND";
            var payloadPresent = Safe(() => context.Body?.data) != null;

            if (notFound || !isSuccess || !payloadPresent)
            {
                return Task.FromResult(new ScriptResponse
                {
                    Key = "domain-lookup-not-found",
                    Data = new
                    {
                        found = false,
                        domainName = requested
                    },
                    Tags = new[] { "domain", "discovery", "lookup", "not-found" }
                });
            }

            // Envelope: { "data": { <instance data> }, "eTag": "W/\"...\"", "extensions": {} }.
            // The Data function may also flatten the payload, so both shapes are probed.
            var eTag = SafeStr(() => context.Body?.data?.eTag)
                    ?? SafeStr(() => context.Body?.data?.etag);

            var baseUrl = SafeStr(() => context.Body?.data?.data?.baseUrl)
                       ?? SafeStr(() => context.Body?.data?.baseUrl);
            var healthUrl = SafeStr(() => context.Body?.data?.data?.healthUrl)
                         ?? SafeStr(() => context.Body?.data?.healthUrl);
            var appId = SafeStr(() => context.Body?.data?.data?.appId)
                     ?? SafeStr(() => context.Body?.data?.appId);
            var domainName = SafeStr(() => context.Body?.data?.data?.domainName)
                          ?? SafeStr(() => context.Body?.data?.domainName)
                          ?? requested;

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                // A registered domain without a baseUrl is unusable for routing - report it as
                // not found rather than handing the caller an endpoint it cannot call.
                return Task.FromResult(new ScriptResponse
                {
                    Key = "domain-lookup-not-found",
                    Data = new
                    {
                        found = false,
                        domainName = domainName
                    },
                    Tags = new[] { "domain", "discovery", "lookup", "not-found" }
                });
            }

            return Task.FromResult(new ScriptResponse
            {
                Key = "domain-lookup-found",
                Data = new
                {
                    found = true,
                    domainName = domainName,
                    baseUrl = baseUrl,
                    healthUrl = healthUrl,
                    appId = appId,
                    eTag = eTag
                },
                Tags = new[] { "domain", "discovery", "lookup", "success" }
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScriptResponse
            {
                Key = "domain-lookup-exception",
                Data = new
                {
                    found = false,
                    domainName = requested,
                    error = "Exception during domain lookup",
                    errorDescription = ex.Message
                },
                Tags = new[] { "domain", "discovery", "lookup", "exception", "error" }
            });
        }
    }
}
