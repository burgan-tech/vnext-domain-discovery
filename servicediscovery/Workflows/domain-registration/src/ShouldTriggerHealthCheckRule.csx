using System.Threading.Tasks;
using BBT.Workflow.Scripting;

/// <summary>
/// Should Trigger Health Check Rule - Checks if health check workflow should be triggered
/// Returns true if healthUrl exists AND (new domain OR healthUrl changed)
/// </summary>
public class ShouldTriggerHealthCheckRule : IConditionMapping
{
    public async Task<bool> Handler(ScriptContext context)
    {
        try
        {
            if (context?.Instance?.Data == null)
                return false;

            // Check if healthUrl was retrieved
            var healthUrlRetrieved = context.Instance.Data.healthUrlRetrieved;
            if (healthUrlRetrieved != true)
                return false;

            // Check if it's a new domain (domain doesn't exist)
            var domainExists = context.Instance.Data.domainExists;
            if (domainExists == false)
                return true;  // New domain, trigger health check

            // For existing domain, check if healthUrl changed
            var existingDomain = context.Instance.Data.existingDomain;
            var domainData = context.Instance.Data.domainData;
            
            if (existingDomain != null && domainData != null)
            {
                var existingHealthUrl = existingDomain._healthUrl?.ToString();
                var newHealthUrl = domainData._healthUrl?.ToString();
                
                // If healthUrl changed, trigger health check
                if (existingHealthUrl != newHealthUrl)
                    return true;
            }

            // HealthUrl exists but didn't change, don't trigger
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

