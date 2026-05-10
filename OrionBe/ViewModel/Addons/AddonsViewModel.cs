using System.Threading;
using System.Threading.Tasks;
using OrionBe.ViewModel.Shared;

namespace OrionBe.ViewModel.Addons;

public sealed class AddonsViewModel : MainWindowViewModelBase
{
    public override Task OnNavigatedToAsync(CancellationToken ctx) => Task.CompletedTask;
}
