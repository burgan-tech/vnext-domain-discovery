using System;
using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;
using BBT.Workflow.Scripting.Functions;

/// <summary>
/// Invalidate Domain List Cache Mapping - evicts the cached "all active domains" list.
///
/// Wired into the "domain" workflow alongside InvalidateDomainCacheMapping, so that registering or
/// updating ANY domain drops the entry the domain-list function caches. Uses StateStoreTask (task
/// type 17, delete), which shares the runtime's "custom:" key prefix with the function cache
/// gateway - so deleting the logical key here really does evict what the function wrote.
///
/// Unlike the per-domain eviction, this key does not depend on the instance at all: domain-list
/// takes no parameters and therefore has exactly ONE cache entry. That is why the key is a constant
/// on both sides - attributes.cache.key in Functions/domain-list.json and DomainCacheKey.ListKey
/// here. Never build the key inline; see Mappings/src/DomainCacheKey.csx for why the namespace is
/// "discovery:domains" (plural) and not "discovery:domain".
///
/// Eviction on REGISTRATION matters as much as on update: a domain registered after the last cache
/// write would otherwise be missing from the list for a full TTL, with no error anywhere.
///
/// This task shares order 1 with invalidate-domain-cache, so the two evictions run in PARALLEL
/// (TaskCoordinator groups onExecutionTasks by Order and runs a group of more than one through
/// ExecuteTaskGroupInParallelAsync). A blocking failure in one member stops the group, so the
/// errorBoundary (action 3 = Ignore) on the task reference in domain-workflow.json is what keeps
/// this eviction from taking the per-domain eviction - or the registration itself - down with it.
/// A domain registration or update is far more important than a cache entry, and the TTL cleans up
/// behind us. That guarantee is NOT enforceable from this script, so the mapping throws loudly on a
/// bad state instead of pretending to succeed.
/// </summary>
public class InvalidateDomainListCacheMapping : ScriptBase, IMapping
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

        stateStoreTask.SetCacheKey(DomainCacheKey.ListKey);

        LogInformation($"invalidate-domain-list-cache -> delete {DomainCacheKey.ListKey}");

        return Task.FromResult(new ScriptResponse());
    }

    public Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        // The state store invoker answers { data: { deletedCount }, metadata: { DeletedCount } }.
        var deletedCount = SafeStr(() => context.Body?.data?.deletedCount)
                        ?? SafeStr(() => context.Body?.data?.DeletedCount);

        return Task.FromResult(new ScriptResponse
        {
            Key = "domain-list-cache-invalidated",
            Data = new
            {
                cacheInvalidated = true,
                deletedCount = deletedCount,
                invalidatedAt = DateTime.UtcNow
            },
            Tags = new[] { "domain", "cache", "invalidation", "list", "success" }
        });
    }
}
