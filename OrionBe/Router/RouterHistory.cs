using System;
using Avalonia.Controls;
using Umbra.Router.Core;
using Umbra.Router.Core.Configuration;
using Umbra.Router.Core.Interfaces;
using Umbra.Router.Core.Services;

namespace OrionBe.Router;

public class RouterHistory<T> : RouterHistoryBase<T, Control> where T : class, IRoutePage
{
    public RouterHistory(IServiceProvider serviceProvider, RouterConfig<T> config, GuardServices<T> guards) : base(serviceProvider, config, guards)
    {
    }

    protected override void ConfigureTView(ref Control? view, T viewModel)
    {
        if (view is not null)
        {
            view.DataContext = viewModel;
        }

        base.ConfigureTView(ref view, viewModel);
    }
}