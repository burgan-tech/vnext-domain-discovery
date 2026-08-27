using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;
using BBT.Workflow.Scripting.Functions;

/// <summary>
/// Domain Lookup Output - shapes the function response and owns its HTTP status code.
///
/// This handler exists for ONE reason the task mapping cannot cover: the status code. The legacy
/// single-task extraction path always answers 200, and the runtime's own
/// DomainDiscoveryResolver distinguishes "domain not registered" from "discovery is broken"
/// purely by HTTP 404 vs. other non-2xx. ScriptResponse.StatusCode is only honoured from a
/// function-level output handler (attributes.output), so the envelope is built here.
///
/// Response contract (consumed by BBT.Workflow.Infrastructure.Discovery.DomainDiscoveryResolver,
/// configured through ServiceDiscoveryOptions.DiscoveryEndpointTemplate):
///     200 -> { "data": { "domainName", "baseUrl", "appId", "healthUrl" }, "eTag": "..." }
///     404 -> { "error": "...", "domainName": "..." }
/// The function declares rawResponse: true, so Data is returned verbatim (no function-key wrapper).
///
/// NOTE: the whole FunctionResponseOutput - status code included - is what the function cache
/// stores, so a 404 replays as a 404 for the TTL. That is exactly why the "domain" workflow evicts
/// the cache key on registration as well as on update; see Workflows/src/InvalidateDomainCacheMapping.csx.
/// </summary>
public class DomainLookupOutput : ScriptBase, IOutputHandler
{
    /// <summary>ToVariableName("get-domain-instance-data") - the OutputResponse slot of the task.</summary>
    private const string TaskVariable = "getDomainInstanceData";

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

    public Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        dynamic? result = null;
        if (context.OutputResponse != null && context.OutputResponse.TryGetValue(TaskVariable, out var stored))
        {
            result = stored;
        }

        var found = SafeBool(() => result?.found);
        var domainName = SafeStr(() => result?.domainName);
        var baseUrl = SafeStr(() => result?.baseUrl);

        if (!found || string.IsNullOrWhiteSpace(baseUrl))
        {
            return Task.FromResult(new ScriptResponse
            {
                Key = "domain-lookup-not-found",
                StatusCode = 404,
                Data = new
                {
                    error = "Domain is not registered in the discovery registry",
                    domainName = domainName
                },
                Tags = new[] { "domain", "discovery", "lookup", "not-found" }
            });
        }

        return Task.FromResult(new ScriptResponse
        {
            Key = "domain-lookup-found",
            StatusCode = 200,
            Data = new
            {
                data = new
                {
                    domainName = domainName,
                    baseUrl = baseUrl,
                    appId = SafeStr(() => result?.appId),
                    healthUrl = SafeStr(() => result?.healthUrl)
                },
                eTag = SafeStr(() => result?.eTag)
            },
            Tags = new[] { "domain", "discovery", "lookup", "success" }
        });
    }
}
