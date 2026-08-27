using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;
using BBT.Workflow.Scripting.Functions;

/// <summary>
/// Invalidate Domain Cache Mapping - evicts one domain's cached lookup entry.
///
/// Wired into the "domain" workflow so that registering or updating a domain record drops the
/// entry the domain-lookup function caches. Uses StateStoreTask (task type 17, delete), which
/// shares the runtime's "custom:" key prefix with the function cache gateway - so deleting the
/// logical key here really does evict what the function wrote.
///
/// Eviction on REGISTRATION matters as much as on update: domain-lookup caches its 404 response
/// too (acceptedStatusCodes "404" makes a missing instance a normal result), so a lookup that ran
/// before the domain existed would keep answering "not registered" for a full TTL.
///
/// The key MUST match what the function cache computed, byte for byte. Both sides go through
/// DomainCacheKey - see Mappings/src/DomainCacheKey.csx and the keyExpression in
/// Functions/domain-lookup.json. Never build the key inline here.
///
/// A failed eviction must NOT fail the transition: a domain registration or update is far more
/// important than a cache entry, and the TTL cleans up behind us. That guarantee is NOT enforceable
/// from this script - an InputHandler that returns early still lets the task run, and a delete with
/// no key fails in the invoker ("delete requires one of 'key', 'keys' or 'query'"). It is enforced
/// by the errorBoundary (action 3 = Ignore) on the task reference in domain-workflow.json; this
/// mapping therefore throws loudly on a bad state instead of pretending to succeed.
/// </summary>
public class InvalidateDomainCacheMapping : ScriptBase, IMapping
{
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

    public Task<ScriptResponse> InputHandler(WorkflowTask task, ScriptContext context)
    {
        var stateStoreTask = task as StateStoreTask;
        if (stateStoreTask == null)
        {
            throw new InvalidOperationException("Task must be a StateStoreTask");
        }

        // Instance.Key is the strongest invariant available: the "domain" workflow's instance key
        // IS the domainName (RegisterDomainLifecycleMapping.csx sets it). Reading the transition
        // payload instead would mis-key the delete if a caller ever submits an update whose
        // domainName differs from the instance it updates.
        var domainName = context.Instance?.Key;

        if (string.IsNullOrWhiteSpace(domainName))
        {
            domainName = SafeStr(() => context.Instance?.Data?.domainName);
        }

        if (string.IsNullOrWhiteSpace(domainName))
        {
            domainName = SafeStr(() => context.Body?.domainName);
        }

        var cacheKey = DomainCacheKey.For(domainName);
        if (cacheKey == null)
        {
            throw new InvalidOperationException(
                "domainName could not be resolved from the instance; the domain lookup cache entry cannot be evicted");
        }

        stateStoreTask.SetCacheKey(cacheKey);

        LogInformation($"invalidate-domain-cache -> delete {cacheKey}");

        return Task.FromResult(new ScriptResponse());
    }

    public Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        // The state store invoker answers { data: { deletedCount }, metadata: { DeletedCount } }.
        var deletedCount = SafeStr(() => context.Body?.data?.deletedCount)
                        ?? SafeStr(() => context.Body?.data?.DeletedCount);

        return Task.FromResult(new ScriptResponse
        {
            Key = "domain-cache-invalidated",
            Data = new
            {
                cacheInvalidated = true,
                deletedCount = deletedCount,
                invalidatedAt = DateTime.UtcNow
            },
            Tags = new[] { "domain", "cache", "invalidation", "success" }
        });
    }
}
