/// <summary>
/// Domain Cache Key - the single source of truth for the discovery domain lookup cache key.
///
/// The SAME key string is produced in two places and they MUST agree byte for byte:
///
///   1. READ  - Functions/domain-lookup.json, attributes.cache.keyExpression (Dynamic Expresso):
///                  "discovery:domain:" + context.QueryParameters.key
///   2. EVICT - Workflows/src/InvalidateDomainCacheMapping.csx (StateStoreTask delete), via For().
///
/// If they ever diverge the cache is never invalidated and domain-lookup serves stale routing data
/// for a full TTL, with no error anywhere. A Dynamic Expresso expression cannot call into this
/// helper (it is not a .csx mapping) and cannot normalize its input, so For() must stay a literal
/// mirror of that expression: concatenation only, NO trimming, NO case folding, NO validation.
/// Changing the format here means changing the keyExpression in domain-lookup.json in the same commit.
///
/// Case sensitivity is intentional and harmless: the same string is also the "domain" workflow's
/// instance key, so a lookup with different casing does not resolve an instance either.
///
/// Returns the UNPREFIXED key. The runtime prepends "custom:" itself on every get/set/delete,
/// for the function cache gateway and the State Store task alike; never write that prefix here.
///
/// ListKey is the SECOND pair maintained here, for the domain-list function:
///
///   1. READ  - Functions/domain-list.json, attributes.cache.key (a STATIC key, not an expression:
///              the function takes no parameters, so there is nothing to compute).
///   2. EVICT - Workflows/src/InvalidateDomainListCacheMapping.csx (StateStoreTask delete).
///
/// Its namespace segment is deliberately "domains" (plural), NOT "domain". Reusing the singular
/// namespace - e.g. "discovery:domain:list" - would collide byte for byte with For("list"), the
/// per-domain key of a domain literally named "list": registering it would evict the whole list
/// cache, and a list-cache eviction would evict that domain's entry. Separate namespaces make the
/// collision unrepresentable rather than merely unlikely.
/// </summary>
public static class DomainCacheKey
{
    public const string Namespace = "discovery:domain";

    /// <summary>
    /// The single cache key of the domain-list function (all active domains). Constant, because the
    /// function takes no parameters: one global entry, evicted on every domain write.
    /// </summary>
    public const string ListKey = "discovery:domains:active";

    /// <summary>
    /// Builds the logical cache key for a domain name. Returns null only when the name is missing,
    /// so callers can decide what to do rather than deleting a garbage key.
    /// </summary>
    public static string For(string domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            return null;
        }

        return Namespace + ":" + domainName;
    }
}
