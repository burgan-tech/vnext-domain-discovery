using System.Threading.Tasks;
using BBT.Workflow.Scripting;

/// <summary>
/// Registration Failure Rule - Complement of RegisterSuccessRule / UpdateSuccessRule.
/// Routes the register and update states to the error state when the lifecycle call failed,
/// so a failed instance cannot get stuck in a state with no matching outgoing transition.
/// </summary>
public class RegistrationFailureRule : IConditionMapping
{
    public async Task<bool> Handler(ScriptContext context)
    {
        try
        {
            if (context?.Instance?.Data == null)
                return true;  // If no data, consider it a failure

            var domainRegistration = context.Instance.Data.domainRegistration;

            if (domainRegistration == null)
                return true;  // If no registration result, consider it a failure

            return domainRegistration.success == false;
        }
        catch (Exception)
        {
            return true;  // On error, consider it a failure
        }
    }
}
