// Copyright © Puneeth DC Ltd. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace HtmlLogViewer.Internal;

/// <summary>
/// Applies the configured <see cref="LogViewerOptions.RoutePrefix"/> to
/// <see cref="LogViewerController"/> at application startup, so consumers do not
/// need to hard-code the route and can change it through options alone.
/// </summary>
internal sealed class LogViewerRouteConvention : IControllerModelConvention
{
    private readonly LogViewerOptions _options;

    internal LogViewerRouteConvention(LogViewerOptions options) => _options = options;

    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerType != typeof(LogViewerController))
            return;

        controller.Selectors.Clear();
        controller.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel(
                new RouteAttribute(_options.RoutePrefix))
        });
    }
}
