using System.Globalization;
using Microsoft.FeatureManagement.Mvc;

namespace RuItUnion.FeedbackBot.Middlewares;

[FeatureGate("ForceCultureSet")]
public class CultureForceSetterMiddleware(IOptions<AppOptions> options) : FrameMiddleware
{
    private CultureInfo Culture { get; } =
        CultureInfo.GetCultureInfo(options.Value.OverrideCultureTag);

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        context.Properties[@"CultureInfo"] = Culture;
        await Next(update, context, ct).ConfigureAwait(false);
    }
}