using System.Globalization;

namespace RuItUnion.FeedbackBot.Middlewares;

public class CultureForceSetterMiddleware(IOptions<AppOptions> options) : FrameMiddleware
{
    private bool Enabled { get; } = options.Value.OverrideCultureEnabled;

    private CultureInfo Culture { get; } =
        CultureInfo.GetCultureInfoByIetfLanguageTag(options.Value.OverrideCultureTag);

    public override async Task InvokeAsync(Update update, FrameContext context, CancellationToken ct = default)
    {
        if (Enabled)
        {
            context.GetCultureInfo();
            context.Properties[@"CultureInfo"] = Culture;
        }

        await Next(update, context, ct).ConfigureAwait(false);
    }
}