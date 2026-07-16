using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class DigitalOrderAdminQueryTests
{
    [Fact]
    public void Combined_filters_are_applied_before_pagination_and_aggregates_remain_global()
    {
        var products = new[]
        {
            Product("guide", "Commerce Field Guide", "commerce-field-guide"),
            Product("audio", "Focus Session", "focus-session")
        };
        var orders = Enumerable.Range(1, 15)
            .Select(index => Order($"match-{index:00}", "guide", DigitalOrderStatus.Paid, $"buyer-{index}@example.com", index))
            .Concat(Enumerable.Range(1, 12).Select(index =>
                Order($"other-{index:00}", "audio", DigitalOrderStatus.Pending, $"listener-{index}@example.com", 100 + index)))
            .ToList();

        var result = DigitalOrderAdminQuery.Apply(
            orders, products, "buyer", "guide", "paid", page: 2, pageSize: 10);

        Assert.Equal(15, result.FilteredCount);
        Assert.Equal(5, result.Items.Count);
        Assert.All(result.Items, order =>
        {
            Assert.Equal("guide", order.ProductId);
            Assert.Equal(DigitalOrderStatus.Paid, order.Status);
            Assert.Contains("buyer", order.BuyerEmail, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(27, result.TotalCount);
        Assert.Equal(15, result.PaidCount);
        Assert.Equal(orders.Sum(order => order.DownloadCount), result.DeliveryCount);
        Assert.Equal(11, result.FirstItem);
        Assert.Equal(15, result.LastItem);
    }

    [Theory]
    [InlineData("order-reference")]
    [InlineData("checkout-reference")]
    [InlineData("invoice-reference")]
    [InlineData("customer@example.com")]
    [InlineData("current product name")]
    [InlineData("current-product-slug")]
    [InlineData("historical product name")]
    [InlineData("product-id")]
    public void Search_covers_order_customer_and_current_or_snapshotted_product_fields(string search)
    {
        var order = Order("order-reference", "product-id", DigitalOrderStatus.Paid, "customer@example.com", 1);
        order.CheckoutId = "checkout-reference";
        order.InvoiceId = "invoice-reference";
        order.ProductSnapshot = new DigitalProductSnapshot { Id = "product-id", Name = "Historical Product Name" };

        var result = DigitalOrderAdminQuery.Apply(
            [order],
            [Product("product-id", "Current Product Name", "current-product-slug")],
            search,
            null,
            null,
            1,
            10);

        Assert.Equal("order-reference", Assert.Single(result.Items).Id);
    }

    [Fact]
    public void Invalid_parameters_are_normalized_and_out_of_range_page_is_clamped()
    {
        var orders = Enumerable.Range(1, 30)
            .Select(index => Order($"order-{index:00}", "product", DigitalOrderStatus.Pending, $"buyer-{index}@example.com", index))
            .ToList();

        var result = DigitalOrderAdminQuery.Apply(
            orders, [Product("product", "Product", "product")], "  buyer  ", null, "not-a-status", 999, 999);

        Assert.Equal("buyer", result.Search);
        Assert.Null(result.Status);
        Assert.Equal(25, result.PageSize);
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(26, result.FirstItem);
        Assert.Equal(30, result.LastItem);
    }

    [Fact]
    public void Empty_results_use_a_safe_first_page_and_zero_range()
    {
        var result = DigitalOrderAdminQuery.Apply(
            [Order("one", "product", DigitalOrderStatus.Paid, "buyer@example.com", 1)],
            [Product("product", "Product", "product")],
            "does-not-exist",
            null,
            null,
            -50,
            10);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(0, result.FirstItem);
        Assert.Equal(0, result.LastItem);
    }

    [Fact]
    public void Equal_timestamps_have_a_stable_descending_id_tie_breaker()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        var orders = new[]
        {
            Order("order-a", "product", DigitalOrderStatus.Paid, "a@example.com", 1, timestamp),
            Order("order-c", "product", DigitalOrderStatus.Paid, "c@example.com", 2, timestamp),
            Order("order-b", "product", DigitalOrderStatus.Paid, "b@example.com", 3, timestamp)
        };

        var result = DigitalOrderAdminQuery.Apply(
            orders, [Product("product", "Product", "product")], null, null, null, 1, 10);

        Assert.Equal(["order-c", "order-b", "order-a"], result.Items.Select(order => order.Id));
    }

    [Fact]
    public void Product_options_include_historical_products_no_longer_in_the_catalog()
    {
        var historical = Order("order", "deleted-product", DigitalOrderStatus.Paid, "buyer@example.com", 1);
        historical.ProductSnapshot = new DigitalProductSnapshot { Id = "deleted-product", Name = "Archived Field Guide" };

        var result = DigitalOrderAdminQuery.Apply(
            [historical], [Product("current", "Current Product", "current")], null, null, null, 1, 10);

        Assert.Contains(result.ProductOptions, option => option.Id == "deleted-product" && option.Name == "Archived Field Guide");
        Assert.Contains(result.ProductOptions, option => option.Id == "current" && option.Name == "Current Product");
    }

    [Fact]
    public void Dashboard_uses_server_side_order_filters_and_pagination_without_a_hundred_order_cap()
    {
        var view = Source("Views", "DigitalDownloads", "Index.cshtml");
        var controller = Source("Controllers", "DigitalDownloadsAdminController.cs");

        Assert.Contains("name=\"orderSearch\"", view, StringComparison.Ordinal);
        Assert.Contains("name=\"orderProductId\"", view, StringComparison.Ordinal);
        Assert.Contains("name=\"orderStatus\"", view, StringComparison.Ordinal);
        Assert.Contains("name=\"orderPageSize\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-fragment=\"orders\"", view, StringComparison.Ordinal);
        Assert.Contains("Showing <strong>@orderPage.FirstItem", view, StringComparison.Ordinal);
        Assert.DoesNotContain("selected=\"@(orderPage", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Orders = (await repository.GetOrders(storeId)).Take(100)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("The latest 100 purchases", view, StringComparison.Ordinal);
    }

    private static DigitalProduct Product(string id, string name, string slug) => new()
    {
        Id = id,
        Name = name,
        Slug = slug
    };

    private static DigitalOrder Order(
        string id,
        string productId,
        DigitalOrderStatus status,
        string email,
        int downloadCount,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = id,
        ProductId = productId,
        Status = status,
        BuyerEmail = email,
        DownloadCount = downloadCount,
        CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch.AddMinutes(downloadCount)
    };

    private static string Source(params string[] segments) =>
        File.ReadAllText(CustomDomainGuideTests.RepositoryFile(["src", "BTCPayServer.Plugins.MakePay.DigitalProducts", .. segments]));
}
