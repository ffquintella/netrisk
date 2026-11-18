using System;
using System.Threading.Tasks;
using Model.Exceptions;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>
/// The base of the Risk index administration tabs: a write that reports a refusal in the reader's language
/// (<c>RiskApi_‹code›MSG</c>, S40 §10.5) and falls back to the server's own sentence — never a generic "failed".
/// </summary>
public abstract class RiskAdminViewModelBase : ViewModelBase
{
    /// <summary>Runs a write with busy indication; true when it succeeded, the success toast shown.</summary>
    protected async Task<bool> RunRiskAsync(string successMessage, Func<Task> operation)
    {
        try
        {
            await WithBusyAsync(operation);
            if (!string.IsNullOrWhiteSpace(successMessage)) Toasts.Success(successMessage);
            return true;
        }
        catch (Exception ex) when (ex is InvalidHttpRequestException or DataNotFoundException or RestComunicationException)
        {
            Logger.Warning("Risk index administration refused: {Message}", ex.Message);
            Toasts.Error(RiskAdminText.Explain(ex));
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected failure in the risk index administration");
            Toasts.Error(Localizer["ErrorSavingMSG"]);
            return false;
        }
    }
}
