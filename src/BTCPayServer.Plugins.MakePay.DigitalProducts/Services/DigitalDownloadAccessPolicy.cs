#nullable enable
using System.Net.Http.Headers;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public static class DigitalDownloadAccessPolicy
{
    private static readonly TimeSpan RangeContinuationWindow = TimeSpan.FromMinutes(30);

    public static bool IsRangeContinuation(DigitalOrder order, RangeHeaderValue? range, DateTimeOffset now)
    {
        if (range is null || range.Ranges.Count != 1 || order.LastDownloadAt is not { } lastDownload ||
            lastDownload < now.Subtract(RangeContinuationWindow))
            return false;

        // A byte-zero request restarts the asset and must consume a new download allowance.
        // Only a later explicit byte offset can continue the transfer that was already counted.
        return range.Ranges.Single().From is > 0;
    }

    public static bool CanStartOrContinue(DigitalOrder order, RangeHeaderValue? range, DateTimeOffset now) =>
        IsRangeContinuation(order, range, now) || order.DownloadCount < order.MaxDownloads;
}
