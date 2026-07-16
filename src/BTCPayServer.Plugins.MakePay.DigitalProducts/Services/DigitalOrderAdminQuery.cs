#nullable enable
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

internal static class DigitalOrderAdminQuery
{
    private const int DefaultPageSize = 25;
    private const int MaximumSearchLength = 200;
    private const int MaximumProductIdLength = 120;
    private static readonly IReadOnlyList<int> AllowedPageSizes = Array.AsReadOnly<int>([10, 25, 50, 100]);

    internal static DigitalOrderPageViewModel Apply(
        IEnumerable<DigitalOrder> source,
        IEnumerable<DigitalProduct> products,
        string? search,
        string? productId,
        string? status,
        int page,
        int pageSize)
    {
        var allOrders = source.ToList();
        var allProducts = products.ToList();
        var productsById = allProducts
            .GroupBy(product => product.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        search = Normalize(search, MaximumSearchLength);
        productId = Normalize(productId, MaximumProductIdLength);
        var selectedStatus = Enum.TryParse<DigitalOrderStatus>(status, true, out var parsedStatus) &&
                             Enum.IsDefined(parsedStatus)
            ? parsedStatus
            : (DigitalOrderStatus?)null;
        pageSize = AllowedPageSizes.Contains(pageSize) ? pageSize : DefaultPageSize;

        IEnumerable<DigitalOrder> filtered = allOrders;
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(order =>
            {
                productsById.TryGetValue(order.ProductId, out var currentProduct);
                var snapshot = order.ProductSnapshot;
                return Contains(order.Id, search) ||
                       Contains(order.CheckoutId, search) ||
                       Contains(order.InvoiceId, search) ||
                       Contains(order.BuyerEmail, search) ||
                       Contains(order.ProductId, search) ||
                       Contains(snapshot?.Id, search) ||
                       Contains(snapshot?.Name, search) ||
                       Contains(currentProduct?.Name, search) ||
                       Contains(currentProduct?.Slug, search);
            });
        }

        if (!string.IsNullOrEmpty(productId))
            filtered = filtered.Where(order => order.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase));
        if (selectedStatus is { } requiredStatus)
            filtered = filtered.Where(order => order.Status == requiredStatus);

        var ordered = filtered
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var filteredCount = ordered.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new DigitalOrderPageViewModel
        {
            Items = items,
            ProductOptions = BuildProductOptions(allOrders, allProducts),
            PageSizeOptions = AllowedPageSizes,
            Search = search ?? "",
            ProductId = productId ?? "",
            Status = selectedStatus,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            FilteredCount = filteredCount,
            TotalCount = allOrders.Count,
            PaidCount = allOrders.Count(order => order.Status == DigitalOrderStatus.Paid),
            DeliveryCount = allOrders.Sum(order => order.DownloadCount)
        };
    }

    private static IReadOnlyList<DigitalOrderProductFilterOption> BuildProductOptions(
        IReadOnlyList<DigitalOrder> orders,
        IReadOnlyList<DigitalProduct> products)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products)
        {
            if (!string.IsNullOrWhiteSpace(product.Id)) options[product.Id] = product.Name;
        }

        foreach (var order in orders)
        {
            if (string.IsNullOrWhiteSpace(order.ProductId) || options.ContainsKey(order.ProductId)) continue;
            options[order.ProductId] = order.ProductSnapshot?.Name ?? $"Deleted product ({ShortId(order.ProductId)})";
        }

        return options
            .Select(option => new DigitalOrderProductFilterOption { Id = option.Key, Name = option.Value })
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) is true;

    private static string ShortId(string value) => value[..Math.Min(8, value.Length)];
}
